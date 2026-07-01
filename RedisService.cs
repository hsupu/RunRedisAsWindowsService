using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace RedisService;

/// <summary>
/// Hosts a single <c>redis-server</c> child process for the lifetime of the Windows service.
/// If the child exits on its own (crash, config error, manual SHUTDOWN) the service stops itself so the SCM can apply its recovery policy instead of reporting a healthy service with a dead Redis.
/// </summary>
public sealed class RedisService : BackgroundService
{
    private readonly string _exePath;
    private readonly string _workDir;
    private readonly IReadOnlyList<string> _forwardedArgs;
    private readonly ILogger<RedisService> _logger;
    private readonly IHostApplicationLifetime _lifetime;

    private volatile Process? _process;

    /// <summary>The child process, exposed for tests only. Null before start / after stop.</summary>
    internal Process? ChildProcess => _process;

    /// <summary>How long to wait for a graceful (Ctrl-C) shutdown before killing the process tree. Exposed for tests.</summary>
    internal TimeSpan GracefulStopTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>Whether the most recent stop completed gracefully (the child honored Ctrl-C). Exposed for tests.</summary>
    internal bool LastStopWasGraceful { get; private set; }

    /// <summary>
    /// How a graceful stop is requested from the child (returns false when unavailable). Defaults to a Windows console Ctrl-C.
    /// This is a seam: firing a real console event inside a test host would kill the test host, so tests substitute a fake and the real path is verified out-of-process.
    /// </summary>
    internal Func<int, bool> SendGracefulStopSignal { get; set; }

    public RedisService(string exePath, string workDir, IReadOnlyList<string> forwardedArgs, ILogger<RedisService> logger, IHostApplicationLifetime lifetime)
    {
        _exePath = exePath;
        _workDir = workDir;
        _forwardedArgs = forwardedArgs;
        _logger = logger;
        _lifetime = lifetime;
        SendGracefulStopSignal = pid => NativeConsole.TrySendCtrlC(pid, _logger);
    }

    public override Task StartAsync(CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo(_exePath) { WorkingDirectory = _workDir };
        foreach (var arg in _forwardedArgs)
        {
            startInfo.ArgumentList.Add(arg);
        }

        _logger.LogInformation("Starting redis-server: {Exe} {Args} (working directory: {WorkDir})", _exePath, string.Join(' ', _forwardedArgs), _workDir);

        try
        {
            _process = Process.Start(startInfo);
        }
        catch (Exception ex)
        {
            // Throwing from StartAsync signals a startup failure to the SCM, which is exactly what we want when redis-server cannot launch.
            _logger.LogError(ex, "Failed to start redis-server at {Exe}.", _exePath);
            throw new InvalidOperationException($"Failed to start redis-server at '{_exePath}'.", ex);
        }

        if (_process is null)
        {
            _logger.LogError("Process.Start returned null for {Exe}.", _exePath);
            throw new InvalidOperationException($"Process.Start returned null for '{_exePath}'.");
        }

        _process.EnableRaisingEvents = true;
        _process.Exited += OnProcessExited;

        // Guard against the child exiting before we subscribed to Exited.
        if (_process.HasExited)
        {
            OnProcessExited(_process, EventArgs.Empty);
        }

        return base.StartAsync(cancellationToken);
    }

    private void OnProcessExited(object? sender, EventArgs e)
    {
        _logger.LogWarning("redis-server exited with code {ExitCode}; stopping the service.", TryGetExitCode());

        // Ask the host to shut down; StopAsync then runs and disposes cleanly.
        _lifetime.StopApplication();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Nothing to do here: the child process runs on its own. Park until the service stops.
        // The OperationCanceledException raised on shutdown is handled by BackgroundService.
        await Task.Delay(Timeout.Infinite, stoppingToken);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        var process = _process;
        if (process is not null)
        {
            process.Exited -= OnProcessExited;

            try
            {
                if (!process.HasExited)
                {
                    LastStopWasGraceful = await TryGracefulStopAsync(process, cancellationToken);

                    if (LastStopWasGraceful)
                    {
                        _logger.LogInformation("redis-server stopped gracefully.");
                    }
                    else if (!process.HasExited)
                    {
                        _logger.LogInformation("Graceful stop did not complete; killing the process tree (pid {Pid}).", process.Id);
                        process.Kill(entireProcessTree: true);
                        await process.WaitForExitAsync(CancellationToken.None);
                    }
                }
            }
            catch (Exception ex)
            {
                // A race where the process exits between HasExited and Kill can throw; that is fine, it is already gone. A cancelled token (SCM stop timeout) also lands here.
                _logger.LogWarning(ex, "Error while stopping redis-server.");
            }
            finally
            {
                process.Dispose();
                _process = null;
            }
        }

        // Use None so the final host cleanup is not itself cancelled by an expired SCM stop token.
        await base.StopAsync(CancellationToken.None);
    }

    private async Task<bool> TryGracefulStopAsync(Process process, CancellationToken cancellationToken)
    {
        // Ask redis to shut down the way it would on SIGINT: a console Ctrl-C. It persists and exits on its own.
        if (!SendGracefulStopSignal(process.Id))
        {
            return false;
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(GracefulStopTimeout);

        try
        {
            await process.WaitForExitAsync(timeout.Token);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    private int TryGetExitCode()
    {
        var process = _process;
        try
        {
            return process?.ExitCode ?? -1;
        }
        catch (InvalidOperationException)
        {
            return -1;
        }
    }
}

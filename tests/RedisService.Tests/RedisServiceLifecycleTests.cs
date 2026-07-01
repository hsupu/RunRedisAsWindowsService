using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

using FluentAssertions;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

using Xunit;

namespace RedisService.Tests;

/// <summary>
/// Behavior tests for the process lifecycle. These use a real child process (cmd.exe) as a stand-in for redis-server so the crash-detection and kill-tree paths are actually exercised.
/// The real console Ctrl-C signal is injected via a seam, because firing a real CTRL_C_EVENT inside the test host would terminate the test host itself; that Win32 path is verified out-of-process instead.
/// </summary>
public sealed class RedisServiceLifecycleTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    /// <summary>Records whether the host was asked to stop, so we can assert on crash detection.</summary>
    private sealed class FakeLifetime : IHostApplicationLifetime
    {
        public readonly ManualResetEventSlim StopRequested = new();
        public CancellationToken ApplicationStarted => default;
        public CancellationToken ApplicationStopping => default;
        public CancellationToken ApplicationStopped => default;
        public void StopApplication() => StopRequested.Set();
    }

    private static RedisService NewService(FakeLifetime lifetime, params string[] cmdArgs) =>
        new("cmd.exe", Environment.CurrentDirectory, cmdArgs, NullLogger<RedisService>.Instance, lifetime);

    private static RedisService NewLongRunningService(FakeLifetime lifetime) =>
        NewService(lifetime, "/c", "ping -n 30 127.0.0.1 >nul");

    [Fact]
    public async Task StopAsync_ReportsGraceful_WhenTheSignalStopsTheChild()
    {
        var lifetime = new FakeLifetime();
        var service = NewLongRunningService(lifetime);
        // Simulate a child that honors the signal and exits on its own.
        service.SendGracefulStopSignal = pid =>
        {
            Process.GetProcessById(pid).Kill(entireProcessTree: true);
            return true;
        };

        await service.StartAsync(CancellationToken.None);
        var pid = service.ChildProcess!.Id;

        await service.StopAsync(CancellationToken.None);

        service.LastStopWasGraceful.Should().BeTrue();
        var lookup = () => Process.GetProcessById(pid);
        lookup.Should().Throw<ArgumentException>();
    }

    [Fact]
    public async Task StopAsync_FallsBackToKill_WhenSignalIsUnavailable()
    {
        var lifetime = new FakeLifetime();
        var service = NewLongRunningService(lifetime);
        // Simulate a child with no console to signal.
        service.SendGracefulStopSignal = _ => false;

        await service.StartAsync(CancellationToken.None);
        var pid = service.ChildProcess!.Id;

        await service.StopAsync(CancellationToken.None);

        service.LastStopWasGraceful.Should().BeFalse();
        var lookup = () => Process.GetProcessById(pid);
        lookup.Should().Throw<ArgumentException>("the real kill-tree fallback must still terminate the process");
    }

    [Fact]
    public async Task StopAsync_FallsBackToKill_WhenGracefulTimesOut()
    {
        var lifetime = new FakeLifetime();
        var service = NewLongRunningService(lifetime);
        // Pretend we signaled, but the child ignores it; the timeout then triggers the kill fallback.
        service.SendGracefulStopSignal = _ => true;
        service.GracefulStopTimeout = TimeSpan.FromMilliseconds(200);

        await service.StartAsync(CancellationToken.None);
        var pid = service.ChildProcess!.Id;

        await service.StopAsync(CancellationToken.None);

        service.LastStopWasGraceful.Should().BeFalse();
        var lookup = () => Process.GetProcessById(pid);
        lookup.Should().Throw<ArgumentException>();
    }

    [Fact]
    public async Task Service_RequestsHostShutdown_WhenChildExitsOnItsOwn()
    {
        var lifetime = new FakeLifetime();
        var service = NewService(lifetime, "/c", "exit 0");

        await service.StartAsync(CancellationToken.None);

        lifetime.StopRequested.Wait(Timeout).Should().BeTrue("the service must stop itself when redis-server dies");

        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task StartAsync_Throws_WhenExecutableIsMissing()
    {
        var service = new RedisService(
            @"Z:\definitely\missing\redis-server.exe",
            Environment.CurrentDirectory,
            Array.Empty<string>(),
            NullLogger<RedisService>.Instance,
            new FakeLifetime());

        await FluentActions
            .Awaiting(() => service.StartAsync(CancellationToken.None))
            .Should().ThrowAsync<InvalidOperationException>();
    }
}

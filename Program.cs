using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using CommandLine;

namespace RedisService;

class Program
{
    public class Options
    {
        [Option('e', "exe", Required = false, HelpText = "Path to redis-server.exe")]
        public string ExePath { get; set; } = "redis-server.exe";

        [Option('d', "dir", Required = false, HelpText = "Working directory")]
        public string WorkingDirectory { get; set; } = ".";

        [Option('c', "config", Required = false, HelpText = "Path to redis-server.conf")]
        public string ConfigFilePath { get; set; } = string.Empty;
    }

    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(Options))]
    static void Main(string[] args)
    {
        var parser = new Parser(settings =>
        {
            // Enable -- to separate known and unknown args.
            settings.EnableDashDash = true;
            // Ignores unknown arguments so we can forward them.
            // settings.IgnoreUnknownArguments = true;
        });

        var parseResult = parser.ParseArguments<Options>(args);
        parseResult.WithParsed(options => MainImpl(options, args));
    }

    static void MainImpl(Options options, string[] originalArgs)
    {
        string workDir = Path.GetFullPath(options.WorkingDirectory);

        string? configFileCygwinPath = null;
        if (!string.IsNullOrWhiteSpace(options.ConfigFilePath))
        {
            configFileCygwinPath = options.ConfigFilePath;
            if (configFileCygwinPath.StartsWith("/cygdrive/"))
            {
                // already cygwin style
            }
            else
            {
                if (!Path.IsPathRooted(configFileCygwinPath))
                {
                    // AppContext.BaseDirectory is the exe directory not the working directory
                    configFileCygwinPath = Path.Combine(workDir, configFileCygwinPath);
                    configFileCygwinPath = Path.GetFullPath(configFileCygwinPath);
                }
                var diskLetter = configFileCygwinPath[..configFileCygwinPath.IndexOf(":")];
                configFileCygwinPath = configFileCygwinPath.Replace(diskLetter + ":", "/cygdrive/" + diskLetter).Replace("\\", "/");
            }
        }

        // Only support forwarding arguments that appear after a standalone "--" separator.
        var extraArgs = new List<string>();
        bool foundSeparator = false;
        foreach (var token in originalArgs)
        {
            if (!foundSeparator)
            {
                if (token == "--")
                {
                    foundSeparator = true;
                }
                continue;
            }
            extraArgs.Add(token);
        }

        // Build final argument list for redis-server.
        // Usage: redis-server [/path/to/redis.conf] [options] [-]
        var forwardedArgs = new List<string>();
        if (configFileCygwinPath is not null)
        {
            forwardedArgs.Add(configFileCygwinPath);
        }
        forwardedArgs.AddRange(extraArgs);

        IHost host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddHostedService(serviceProvider => new RedisService(options.ExePath, workDir, forwardedArgs));
            })
            .UseWindowsService(options =>
            {
                // options.ServiceName = "Redis";
            })
            .UseContentRoot(workDir)
            .Build();

        host.Run();
    }
}


public class RedisService(string exePath, string workDir, IEnumerable<string> forwardedArgs) : BackgroundService
{

    private Process? process = new();

    public override Task StartAsync(CancellationToken stoppingToken)
    {
        ProcessStartInfo processStartInfo = new(exePath, forwardedArgs)
        {
            WorkingDirectory = workDir,
        };

        process = Process.Start(processStartInfo);

        return Task.CompletedTask;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(-1, stoppingToken);
    }

    public override Task StopAsync(CancellationToken stoppingToken)
    {
        if (process != null)
        {
            process.Kill();
            process.Dispose();
        }

        return Task.CompletedTask;
    }
}

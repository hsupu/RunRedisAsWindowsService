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
        public string ConfigFilePath { get; set; } = "redis-server.conf";
    }

    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(Options))]
    static void Main(string[] args)
    {
        Parser.Default.ParseArguments<Options>(args).WithParsed(MainImpl);
    }

    static void MainImpl(Options options)
    {
        string workDir = Path.GetFullPath(options.WorkingDirectory);

        string configFileCygwinPath = options.ConfigFilePath;

        if (configFileCygwinPath.StartsWith("/cygdrive/"))
        {
            // nop
        }
        else
        {
            if (!Path.IsPathRooted(configFileCygwinPath))
            {
                // AppContext.BaseDirectory
                configFileCygwinPath = Path.Combine(workDir, configFileCygwinPath);
                configFileCygwinPath = Path.GetFullPath(configFileCygwinPath);
            }

            var diskLetter = configFileCygwinPath[..configFileCygwinPath.IndexOf(":")];
            configFileCygwinPath = configFileCygwinPath.Replace(diskLetter + ":", "/cygdrive/" + diskLetter).Replace("\\", "/");
        }

        IHost host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddHostedService(serviceProvider => new RedisService(options.ExePath, workDir, configFileCygwinPath));
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


public class RedisService(string exePath, string workDir, string configFileCygwinPath) : BackgroundService
{

    private Process? process = new();

    public override Task StartAsync(CancellationToken stoppingToken)
    {
        // Usage: [/path/to/redis.conf] [options] [-]
        ProcessStartInfo processStartInfo = new(exePath, new []{ configFileCygwinPath })
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

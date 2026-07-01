using System.Diagnostics.CodeAnalysis;
using System.IO;

using CommandLine;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace RedisService;

internal static class Program
{
    private sealed class Options
    {
        [Option('e', "exe", Required = false, HelpText = "Path to redis-server.exe")]
        public string ExePath { get; set; } = "redis-server.exe";

        [Option('d', "dir", Required = false, HelpText = "Working directory")]
        public string WorkingDirectory { get; set; } = ".";

        [Option('c', "config", Required = false, HelpText = "Path to redis-server.conf")]
        public string ConfigFilePath { get; set; } = string.Empty;
    }

    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(Options))]
    private static void Main(string[] args)
    {
        var parser = new Parser(settings =>
        {
            // Enable "--" to separate wrapper args from forwarded redis args.
            settings.EnableDashDash = true;
            // Ignores unknown arguments so we can forward them.
            // settings.IgnoreUnknownArguments = true;
        });

        var parseResult = parser.ParseArguments<Options>(args);
        parseResult.WithParsed(options => MainImpl(options, args));
    }

    private static void MainImpl(Options options, string[] originalArgs)
    {
        var workDir = Path.GetFullPath(options.WorkingDirectory);

        var cygwinConfigPath = RedisArguments.ToCygwinConfigPath(options.ConfigFilePath, workDir);
        var extraArgs = RedisArguments.ExtractForwardedArgs(originalArgs);
        var forwardedArgs = RedisArguments.BuildForwardedArgs(cygwinConfigPath, extraArgs);

        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            // Deliberately no Args: this service reads no configuration from the command line, and passing them would leak the redis options that follow "--" into IConfiguration.
            ContentRootPath = workDir,
        });

        builder.Services.AddWindowsService(serviceOptions =>
        {
            // serviceOptions.ServiceName = "Redis";
        });

        builder.Services.AddHostedService(sp => new RedisService(
            options.ExePath,
            workDir,
            forwardedArgs,
            sp.GetRequiredService<ILogger<RedisService>>(),
            sp.GetRequiredService<IHostApplicationLifetime>()));

        builder.Build().Run();
    }
}

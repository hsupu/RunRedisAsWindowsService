using System.Collections.Generic;
using System.IO;

namespace RedisService;

/// <summary>
/// Pure, side-effect-free helpers for turning wrapper input into the argument list handed to <c>redis-server</c>.
/// Kept separate from process management so the fiddly path/parsing rules can be unit tested in isolation.
/// </summary>
public static class RedisArguments
{
    private const string CygdrivePrefix = "/cygdrive/";

    /// <summary>
    /// Converts a Windows/relative config path into the cygwin-style path that the redis-server Windows build expects (e.g. <c>C:\a\b.conf</c> becomes <c>/cygdrive/c/a/b.conf</c>).
    /// Returns <c>null</c> when no config is set. A path already in cygwin form is returned unchanged.
    /// </summary>
    public static string? ToCygwinConfigPath(string? configFilePath, string workDir)
    {
        if (string.IsNullOrWhiteSpace(configFilePath))
        {
            return null;
        }

        var path = configFilePath;

        if (path.StartsWith(CygdrivePrefix, System.StringComparison.Ordinal))
        {
            return path; // Already in cygwin form
        }

        if (!Path.IsPathRooted(path))
        {
            // Resolve relative to the working directory, not the exe directory (AppContext.BaseDirectory would be the exe directory).
            path = Path.GetFullPath(Path.Combine(workDir, path));
        }

        var colon = path.IndexOf(':');
        if (colon <= 0)
        {
            // No drive letter (e.g. a UNC path); just normalize separators.
            return path.Replace('\\', '/');
        }

        // cygwin's /cygdrive mount uses lowercase drive letters by convention.
        var driveLetter = path[..colon];
        var cygdrive = CygdrivePrefix + driveLetter.ToLowerInvariant();
        return path.Replace(driveLetter + ":", cygdrive).Replace('\\', '/');
    }

    /// <summary>
    /// Returns only the tokens that appear after a standalone <c>--</c> separator.
    /// Everything before (and the separator itself) is dropped; those belong to the wrapper.
    /// Returns an empty list when there is no separator.
    /// </summary>
    public static IReadOnlyList<string> ExtractForwardedArgs(IReadOnlyList<string> originalArgs)
    {
        var forwarded = new List<string>();
        var foundSeparator = false;

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

            forwarded.Add(token);
        }

        return forwarded;
    }

    /// <summary>
    /// Builds the final redis-server argument list. The config file, when present, must be the first argument; forwarded options follow.
    /// Usage: <c>redis-server [/path/to/redis.conf] [options]</c>.
    /// </summary>
    public static IReadOnlyList<string> BuildForwardedArgs(string? cygwinConfigPath, IEnumerable<string> extraArgs)
    {
        var result = new List<string>();

        if (cygwinConfigPath is not null)
        {
            result.Add(cygwinConfigPath);
        }

        result.AddRange(extraArgs);
        return result;
    }
}

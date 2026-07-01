using System.Collections.Generic;

using FluentAssertions;

using Xunit;

namespace RedisService.Tests;

public sealed class RedisArgumentsTests
{
    // ToCygwinConfigPath ---------------------------------------------------

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ToCygwinConfigPath_ReturnsNull_WhenConfigMissing(string? config)
    {
        var result = RedisArguments.ToCygwinConfigPath(config, @"C:\work");

        result.Should().BeNull();
    }

    [Fact]
    public void ToCygwinConfigPath_ConvertsAbsoluteWindowsPath_ToCygdrive()
    {
        var result = RedisArguments.ToCygwinConfigPath(@"C:\redis\redis.conf", @"C:\work");

        result.Should().Be("/cygdrive/c/redis/redis.conf");
    }

    [Fact]
    public void ToCygwinConfigPath_ResolvesRelativePath_AgainstWorkingDirectory()
    {
        var result = RedisArguments.ToCygwinConfigPath("redis.conf", @"D:\data\redis");

        result.Should().Be("/cygdrive/d/data/redis/redis.conf");
    }

    [Fact]
    public void ToCygwinConfigPath_LeavesCygwinStylePath_Unchanged()
    {
        const string cygwin = "/cygdrive/e/redis/redis.conf";

        var result = RedisArguments.ToCygwinConfigPath(cygwin, @"C:\work");

        result.Should().Be(cygwin);
    }

    // ExtractForwardedArgs -------------------------------------------------

    [Fact]
    public void ExtractForwardedArgs_ReturnsEmpty_WhenNoSeparator()
    {
        var args = new[] { "-e", "redis-server.exe", "-d", "C:\\work" };

        var result = RedisArguments.ExtractForwardedArgs(args);

        result.Should().BeEmpty();
    }

    [Fact]
    public void ExtractForwardedArgs_ReturnsOnlyTokensAfterSeparator()
    {
        var args = new[] { "-e", "redis.exe", "--", "--port", "6390", "--loglevel", "verbose" };

        var result = RedisArguments.ExtractForwardedArgs(args);

        result.Should().Equal("--port", "6390", "--loglevel", "verbose");
    }

    [Fact]
    public void ExtractForwardedArgs_StopsAtFirstSeparatorOnly()
    {
        var args = new[] { "--", "--port", "6390", "--", "keep-this" };

        var result = RedisArguments.ExtractForwardedArgs(args);

        // The second "--" is a normal token forwarded to redis-server.
        result.Should().Equal("--port", "6390", "--", "keep-this");
    }

    // BuildForwardedArgs ---------------------------------------------------

    [Fact]
    public void BuildForwardedArgs_PutsConfigFirst_ThenExtras()
    {
        var extras = new List<string> { "--port", "6390" };

        var result = RedisArguments.BuildForwardedArgs("/cygdrive/C/redis/redis.conf", extras);

        result.Should().Equal("/cygdrive/C/redis/redis.conf", "--port", "6390");
    }

    [Fact]
    public void BuildForwardedArgs_OmitsConfig_WhenNull()
    {
        var extras = new List<string> { "--port", "6380" };

        var result = RedisArguments.BuildForwardedArgs(null, extras);

        result.Should().Equal("--port", "6380");
    }
}

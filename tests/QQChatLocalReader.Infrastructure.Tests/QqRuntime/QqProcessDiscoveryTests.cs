using QQChatLocalReader.Infrastructure.QqRuntime;

namespace QQChatLocalReader.Infrastructure.Tests.QqRuntime;

public sealed class QqProcessDiscoveryTests
{
    [Fact]
    public void TryParseWrapperPathReadsVersionDirectory()
    {
        var wrapperPath = Path.Combine(
            Path.GetPathRoot(Environment.SystemDirectory)!,
            "QQ",
            "versions",
            "9.9.33-52230",
            "resources",
            "app",
            "wrapper.node");

        var success = QqProcessDiscovery.TryParseWrapperPath(
            wrapperPath,
            out var version,
            out var resourceDirectory);

        Assert.True(success);
        Assert.Equal("9.9.33-52230", version);
        Assert.Equal(Path.GetDirectoryName(Path.GetDirectoryName(wrapperPath)), resourceDirectory);
    }

    [Fact]
    public void TryParseWrapperPathRejectsUnexpectedLayout()
    {
        var wrapperPath = Path.Combine(Path.GetTempPath(), "wrapper.node");

        var success = QqProcessDiscovery.TryParseWrapperPath(
            wrapperPath,
            out var version,
            out var resourceDirectory);

        Assert.False(success);
        Assert.Empty(version);
        Assert.Empty(resourceDirectory);
    }
}

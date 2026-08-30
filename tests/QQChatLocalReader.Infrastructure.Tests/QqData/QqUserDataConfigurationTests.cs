using QQChatLocalReader.Infrastructure.QqData;

namespace QQChatLocalReader.Infrastructure.Tests.QqData;

public sealed class QqUserDataConfigurationTests
{
    [Fact]
    public void ParseDataRootReadsQuotedAbsolutePath()
    {
        var expected = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "QQ Data"));

        var actual = QqUserDataConfiguration.ParseDataRoot(
        [
            "[UserDataSet]",
            $"UserDataSavePath=\"{expected}\"",
        ]);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void ParseDataRootRejectsRelativePath()
    {
        Assert.Throws<InvalidDataException>(() =>
            QqUserDataConfiguration.ParseDataRoot(["UserDataSavePath=QQ Data"]));
    }
}

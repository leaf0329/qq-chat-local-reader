using QQChatLocalReader.Infrastructure.Security;

namespace QQChatLocalReader.Infrastructure.Tests;

public sealed class McpAuthorizationProfileStoreTests
{
    [Fact]
    public void ProfileIsProtectedAndTrustCanBeRevoked()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"qclr-mcp-profile-{Guid.NewGuid():N}");
        try
        {
            var store = McpAuthorizationProfileStore.Open(directory);
            var created = store.Create("test-profile-name");

            Assert.False(created.IsTrusted);
            Assert.DoesNotContain("test-profile-name", File.ReadAllText(Directory.GetFiles(directory).Single()), StringComparison.Ordinal);
            Assert.True(store.SetTrusted(created.Id, true).IsTrusted);
            Assert.True(store.Read(created.Id).IsTrusted);
            Assert.False(store.SetTrusted(created.Id, false).IsTrusted);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }
}

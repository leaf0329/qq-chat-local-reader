using System.Net.Http.Headers;
using System.Text.Json;

namespace QQChatLocalReader.Application.Updates;

public static class GitHubReleaseUpdateChecker
{
    private static readonly Uri LatestReleaseApi = new("https://api.github.com/repos/leaf0329/qq-chat-local-reader/releases/latest");

    public static async Task<UpdateCheckResult?> CheckAsync(
        Version currentVersion,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(currentVersion);
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("qq-chat-local-reader", currentVersion.ToString()));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        using var response = await client.GetAsync(LatestReleaseApi, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        var tag = document.RootElement.GetProperty("tag_name").GetString();
        var urlText = document.RootElement.GetProperty("html_url").GetString();
        if (tag is null || urlText is null || !Uri.TryCreate(urlText, UriKind.Absolute, out var url) ||
            url.Scheme != Uri.UriSchemeHttps || !url.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("GitHub release metadata is invalid.");
        }

        var versionText = tag.TrimStart('v').Split('-', 2)[0];
        if (!Version.TryParse(versionText, out var latestVersion)) throw new InvalidDataException("GitHub release version is invalid.");
        return new UpdateCheckResult(tag, url, latestVersion > currentVersion);
    }
}

public sealed record UpdateCheckResult(string VersionTag, Uri ReleasePage, bool IsNewer);

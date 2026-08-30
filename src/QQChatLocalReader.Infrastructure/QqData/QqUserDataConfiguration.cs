namespace QQChatLocalReader.Infrastructure.QqData;

public sealed class QqUserDataConfiguration
{
    private const string SavePathKey = "UserDataSavePath";

    public static string GetDefaultConfigurationPath()
    {
        var commonDocuments = Environment.GetFolderPath(Environment.SpecialFolder.CommonDocuments);
        return Path.Combine(commonDocuments, "Tencent", "QQ", "UserDataInfo.ini");
    }

    public static string ParseDataRoot(IEnumerable<string> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);

        foreach (var line in lines)
        {
            var separatorIndex = line.IndexOf('=');
            if (separatorIndex < 0 ||
                !line[..separatorIndex].Trim().Equals(SavePathKey, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var value = line[(separatorIndex + 1)..].Trim().Trim('"');
            if (value.Length == 0)
            {
                throw new InvalidDataException($"{SavePathKey} is empty.");
            }

            if (!Path.IsPathFullyQualified(value))
            {
                throw new InvalidDataException($"{SavePathKey} must be an absolute path.");
            }

            return Path.GetFullPath(value);
        }

        throw new InvalidDataException($"{SavePathKey} was not found.");
    }

    public static async Task<string> ReadDataRootAsync(
        string? configurationPath = null,
        CancellationToken cancellationToken = default)
    {
        var path = configurationPath ?? GetDefaultConfigurationPath();
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 4096,
            useAsync: true);
        using var reader = new StreamReader(stream);

        var lines = new List<string>();
        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
        {
            lines.Add(line);
        }

        return ParseDataRoot(lines);
    }
}

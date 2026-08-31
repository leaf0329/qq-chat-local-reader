using System.Text.Json;

namespace QQChatLocalReader.Infrastructure.Security;

public static class ApplicationPreferencesStore
{
    private const string FileName = "preferences.json";

    public static ApplicationPreferences Read()
    {
        var path = GetPath();
        if (!File.Exists(path)) return new ApplicationPreferences(true);
        try { return JsonSerializer.Deserialize<ApplicationPreferences>(File.ReadAllBytes(path)) ?? new ApplicationPreferences(true); }
        catch (JsonException) { return new ApplicationPreferences(true); }
    }

    public static void Write(ApplicationPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        var path = GetPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporaryPath = path + $".{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllBytes(temporaryPath, JsonSerializer.SerializeToUtf8Bytes(preferences));
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    private static string GetPath()
    {
        var localApplicationData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localApplicationData)) throw new InvalidOperationException("The local application data directory is unavailable.");
        return Path.Combine(localApplicationData, "QQChatLocalReader", FileName);
    }
}

public sealed record ApplicationPreferences(bool CheckForUpdates);

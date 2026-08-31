using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text.Json;

namespace QQChatLocalReader.Infrastructure.Security;

public sealed class McpAuthorizationProfileStore
{
    private const int MaximumFileSize = 64 * 1024;
    private static readonly byte[] Header = "QCLRMCP1"u8.ToArray();
    private static readonly byte[] Entropy = "qq-chat-local-reader:mcp-profile:v1"u8.ToArray();
    private readonly string directoryPath;

    private McpAuthorizationProfileStore(string directoryPath)
    {
        this.directoryPath = directoryPath;
        Directory.CreateDirectory(directoryPath);
        RestrictDirectory(directoryPath);
    }

    public static McpAuthorizationProfileStore OpenDefault()
    {
        var localApplicationData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localApplicationData))
        {
            throw new InvalidOperationException("The local application data directory is unavailable.");
        }

        return new McpAuthorizationProfileStore(Path.Combine(localApplicationData, "QQChatLocalReader", "mcp-profiles-v1"));
    }

    public static McpAuthorizationProfileStore Open(string directoryPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryPath);
        return new McpAuthorizationProfileStore(Path.GetFullPath(directoryPath));
    }

    public McpAuthorizationProfile Create(string displayName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        var profile = new McpAuthorizationProfile(Guid.NewGuid(), displayName.Trim(), false, DateTimeOffset.UtcNow);
        Write(profile, createNew: true);
        return profile;
    }

    public McpAuthorizationProfile Read(Guid id)
    {
        var path = ProfilePath(id);
        if (!File.Exists(path)) throw new KeyNotFoundException("The MCP authorization profile was not found.");
        var envelope = File.ReadAllBytes(path);
        byte[]? clear = null;
        try
        {
            if (envelope.Length <= Header.Length || envelope.Length > MaximumFileSize ||
                !envelope.AsSpan(0, Header.Length).SequenceEqual(Header))
            {
                throw new InvalidDataException("The MCP authorization profile is invalid.");
            }

            clear = ProtectedData.Unprotect(envelope.AsSpan(Header.Length).ToArray(), Entropy, DataProtectionScope.CurrentUser);
            var profile = JsonSerializer.Deserialize<McpAuthorizationProfile>(clear) ??
                throw new InvalidDataException("The MCP authorization profile is invalid.");
            return profile.Id == id ? profile : throw new InvalidDataException("The MCP authorization profile identity does not match.");
        }
        catch (CryptographicException exception)
        {
            throw new InvalidDataException("The MCP authorization profile cannot be opened by the current Windows user.", exception);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(envelope);
            if (clear is not null) CryptographicOperations.ZeroMemory(clear);
        }
    }

    public McpAuthorizationProfile SetTrusted(Guid id, bool trusted)
    {
        var current = Read(id);
        var updated = current with { IsTrusted = trusted };
        Write(updated, createNew: false);
        return updated;
    }

    public bool Delete(Guid id)
    {
        var path = ProfilePath(id);
        if (!File.Exists(path)) return false;
        File.Delete(path);
        return true;
    }

    public IReadOnlyList<McpAuthorizationProfile> List() => Directory.EnumerateFiles(directoryPath, "*.profile")
        .Select(Path.GetFileNameWithoutExtension)
        .Select(value => Guid.TryParseExact(value, "N", out var id) ? id : (Guid?)null)
        .Where(id => id.HasValue)
        .Select(id => Read(id!.Value))
        .OrderBy(profile => profile.CreatedUtc)
        .ToArray();

    private void Write(McpAuthorizationProfile profile, bool createNew)
    {
        var clear = JsonSerializer.SerializeToUtf8Bytes(profile);
        byte[]? protectedBytes = null;
        var path = ProfilePath(profile.Id);
        var temporaryPath = path + $".{Guid.NewGuid():N}.tmp";
        try
        {
            protectedBytes = ProtectedData.Protect(clear, Entropy, DataProtectionScope.CurrentUser);
            using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                stream.Write(Header);
                stream.Write(protectedBytes);
                stream.Flush(true);
            }

            if (createNew)
            {
                File.Move(temporaryPath, path);
            }
            else
            {
                File.Move(temporaryPath, path, overwrite: true);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(clear);
            if (protectedBytes is not null) CryptographicOperations.ZeroMemory(protectedBytes);
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    private string ProfilePath(Guid id) => Path.Combine(directoryPath, $"{id:N}.profile");

    private static void RestrictDirectory(string path)
    {
        using var identity = WindowsIdentity.GetCurrent();
        var user = identity.User ?? throw new InvalidOperationException("The current Windows user SID is unavailable.");
        var security = new DirectorySecurity();
        security.SetOwner(user);
        security.SetAccessRuleProtection(true, false);
        security.AddAccessRule(new FileSystemAccessRule(
            user,
            FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None,
            AccessControlType.Allow));
        new DirectoryInfo(path).SetAccessControl(security);
    }
}

public sealed record McpAuthorizationProfile(Guid Id, string DisplayName, bool IsTrusted, DateTimeOffset CreatedUtc);

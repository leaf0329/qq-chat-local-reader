using System.Security.Cryptography;
using System.Security.AccessControl;
using System.Security.Principal;

namespace QQChatLocalReader.Infrastructure.Indexing;

internal static class WindowsIndexKeyStore
{
    internal const string KeyFileName = "index.key";
    private const int MaximumProtectedKeySize = 64 * 1024;
    private static readonly byte[] Header = "QCLRKEY1"u8.ToArray();
    private static readonly byte[] Entropy = "qq-chat-local-reader:index-key:v1"u8.ToArray();

    public static IndexDatabaseKey OpenOrCreate(string directoryPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryPath);
        var fullDirectoryPath = Path.GetFullPath(directoryPath);
        Directory.CreateDirectory(fullDirectoryPath);
        RestrictDirectoryToCurrentUser(fullDirectoryPath);
        var keyPath = Path.Combine(fullDirectoryPath, KeyFileName);

        if (!File.Exists(keyPath))
        {
            CreateKeyFile(keyPath);
        }

        return ReadKeyFile(keyPath);
    }

    private static void RestrictDirectoryToCurrentUser(string directoryPath)
    {
        using var identity = WindowsIdentity.GetCurrent();
        var user = identity.User ?? throw new InvalidOperationException("The current Windows user SID is unavailable.");
        var security = new DirectorySecurity();
        security.SetOwner(user);
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new FileSystemAccessRule(
            user,
            FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None,
            AccessControlType.Allow));
        new DirectoryInfo(directoryPath).SetAccessControl(security);
    }

    private static void CreateKeyFile(string keyPath)
    {
        var rawKey = RandomNumberGenerator.GetBytes(32);
        byte[]? protectedKey = null;
        var temporaryPath = Path.Combine(
            Path.GetDirectoryName(keyPath)!,
            $".{KeyFileName}.{Guid.NewGuid():N}.tmp");
        try
        {
            protectedKey = ProtectedData.Protect(rawKey, Entropy, DataProtectionScope.CurrentUser);
            using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                FileOptions.WriteThrough))
            {
                stream.Write(Header);
                stream.Write(protectedKey);
                stream.Flush(flushToDisk: true);
            }

            try
            {
                File.Move(temporaryPath, keyPath);
            }
            catch (IOException) when (File.Exists(keyPath))
            {
                File.Delete(temporaryPath);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(rawKey);
            if (protectedKey is not null)
            {
                CryptographicOperations.ZeroMemory(protectedKey);
            }

            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static IndexDatabaseKey ReadKeyFile(string keyPath)
    {
        var file = new FileInfo(keyPath);
        if (file.Length <= Header.Length || file.Length > MaximumProtectedKeySize)
        {
            throw new InvalidDataException("The protected index key file is invalid.");
        }

        var envelope = File.ReadAllBytes(keyPath);
        byte[]? protectedKey = null;
        byte[]? rawKey = null;
        try
        {
            if (!envelope.AsSpan(0, Header.Length).SequenceEqual(Header))
            {
                throw new InvalidDataException("The protected index key file has an unsupported format.");
            }

            protectedKey = envelope.AsSpan(Header.Length).ToArray();
            rawKey = ProtectedData.Unprotect(
                protectedKey,
                Entropy,
                DataProtectionScope.CurrentUser);
            if (rawKey.Length != 32)
            {
                throw new InvalidDataException("The protected index key has an invalid length.");
            }

            return new IndexDatabaseKey(Interlocked.Exchange(ref rawKey, null)!);
        }
        catch (CryptographicException exception)
        {
            throw new InvalidDataException("The index key cannot be opened by the current Windows user.", exception);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(envelope);
            if (protectedKey is not null)
            {
                CryptographicOperations.ZeroMemory(protectedKey);
            }

            if (rawKey is not null)
            {
                CryptographicOperations.ZeroMemory(rawKey);
            }
        }
    }
}

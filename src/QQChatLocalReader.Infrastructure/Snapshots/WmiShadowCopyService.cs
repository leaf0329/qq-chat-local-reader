using System.Globalization;
using System.Management;
using System.Security.Principal;

namespace QQChatLocalReader.Infrastructure.Snapshots;

public sealed class WmiShadowCopyService : IShadowCopyService
{
    public ValueTask<IShadowCopyLease> CreateAsync(
        string volumeRoot,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(volumeRoot);
        cancellationToken.ThrowIfCancellationRequested();

        if (!IsAdministrator())
        {
            throw new ShadowCopyException("Creating a database snapshot requires temporary administrator approval.");
        }

        try
        {
            using var shadowClass = new ManagementClass("Win32_ShadowCopy");
            using var input = shadowClass.GetMethodParameters("Create");
            input["Volume"] = NormalizeVolumeRoot(volumeRoot);
            input["Context"] = "ClientAccessible";

            using var output = shadowClass.InvokeMethod("Create", input, null)
                ?? throw new ShadowCopyException("Windows did not return a shadow copy result.");
            var returnCode = Convert.ToUInt32(output["ReturnValue"], CultureInfo.InvariantCulture);
            if (returnCode != 0)
            {
                throw new ShadowCopyException($"Windows could not create the shadow copy (code {returnCode}).");
            }

            var idText = Convert.ToString(output["ShadowID"], CultureInfo.InvariantCulture);
            if (!Guid.TryParse(idText, out var shadowId))
            {
                throw new ShadowCopyException("Windows returned an invalid shadow copy identifier.");
            }

            try
            {
                var devicePath = FindDevicePath(shadowId)
                    ?? throw new ShadowCopyException("The created shadow copy could not be found.");
                return ValueTask.FromResult<IShadowCopyLease>(new WmiShadowCopyLease(shadowId, devicePath));
            }
            catch
            {
                Delete(shadowId);
                throw;
            }
        }
        catch (ShadowCopyException)
        {
            throw;
        }
        catch (Exception exception) when (exception is ManagementException or UnauthorizedAccessException)
        {
            throw new ShadowCopyException("Windows rejected the shadow copy operation.", exception);
        }
    }

    private static string NormalizeVolumeRoot(string volumeRoot)
    {
        var root = Path.GetPathRoot(Path.GetFullPath(volumeRoot));
        return root ?? throw new ArgumentException("The path has no volume root.", nameof(volumeRoot));
    }

    private static bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static string? FindDevicePath(Guid shadowId)
    {
        using var searcher = new ManagementObjectSearcher(
            "SELECT ID, DeviceObject FROM Win32_ShadowCopy");
        using var results = searcher.Get();

        foreach (ManagementObject shadow in results)
        {
            using (shadow)
            {
                if (!Guid.TryParse(Convert.ToString(shadow["ID"], CultureInfo.InvariantCulture), out var id) ||
                    id != shadowId)
                {
                    continue;
                }

                return Convert.ToString(shadow["DeviceObject"], CultureInfo.InvariantCulture);
            }
        }

        return null;
    }

    private static void Delete(Guid shadowId)
    {
        using var searcher = new ManagementObjectSearcher("SELECT ID FROM Win32_ShadowCopy");
        using var results = searcher.Get();

        foreach (ManagementObject shadow in results)
        {
            using (shadow)
            {
                if (Guid.TryParse(Convert.ToString(shadow["ID"], CultureInfo.InvariantCulture), out var id) &&
                    id == shadowId)
                {
                    shadow.Delete();
                    return;
                }
            }
        }
    }

    private sealed class WmiShadowCopyLease(Guid shadowId, string devicePath) : IShadowCopyLease
    {
        private bool disposed;

        public string DevicePath { get; } = devicePath;

        public ValueTask DisposeAsync()
        {
            if (!disposed)
            {
                disposed = true;
                Delete(shadowId);
            }

            return ValueTask.CompletedTask;
        }
    }
}

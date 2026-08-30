using System.ComponentModel;
using System.Diagnostics;

namespace QQChatLocalReader.Infrastructure.QqRuntime;

public static class QqProcessDiscovery
{
    private const string WrapperFileName = "wrapper.node";

    public static IReadOnlyList<QqRuntimeInstallation> Discover()
    {
        var installations = new List<QqRuntimeInstallation>();

        foreach (var process in Process.GetProcessesByName("QQ"))
        {
            using (process)
            {
                try
                {
                    var wrapperPath = process.Modules
                        .Cast<ProcessModule>()
                        .Select(module => module.FileName)
                        .FirstOrDefault(path =>
                            Path.GetFileName(path).Equals(WrapperFileName, StringComparison.OrdinalIgnoreCase));

                    var executablePath = process.MainModule?.FileName;
                    if (wrapperPath is null ||
                        executablePath is null ||
                        !TryParseWrapperPath(wrapperPath, out var version, out var resourceDirectory))
                    {
                        continue;
                    }

                    installations.Add(new QqRuntimeInstallation(
                        process.Id,
                        version,
                        executablePath,
                        resourceDirectory));
                }
                catch (Exception exception) when (IsProcessInspectionFailure(exception))
                {
                    continue;
                }
            }
        }

        return installations
            .OrderBy(installation => installation.ProcessId)
            .ToArray();
    }

    public static bool TryParseWrapperPath(
        string wrapperPath,
        out string version,
        out string resourceDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(wrapperPath);

        var wrapperFile = new FileInfo(Path.GetFullPath(wrapperPath));
        var appDirectory = wrapperFile.Directory;
        var resourcesDirectory = appDirectory?.Parent;
        var versionDirectory = resourcesDirectory?.Parent;

        if (appDirectory is null ||
            resourcesDirectory is null ||
            versionDirectory is null ||
            !wrapperFile.Name.Equals(WrapperFileName, StringComparison.OrdinalIgnoreCase) ||
            !appDirectory.Name.Equals("app", StringComparison.OrdinalIgnoreCase) ||
            !resourcesDirectory.Name.Equals("resources", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(versionDirectory.Name))
        {
            version = string.Empty;
            resourceDirectory = string.Empty;
            return false;
        }

        version = versionDirectory.Name;
        resourceDirectory = resourcesDirectory.FullName;
        return true;
    }

    private static bool IsProcessInspectionFailure(Exception exception) =>
        exception is Win32Exception or InvalidOperationException or NotSupportedException;
}

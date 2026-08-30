namespace QQChatLocalReader.Infrastructure.QqRuntime;

public sealed class QqRuntimeInstallation
{
    public QqRuntimeInstallation(int processId, string version, string executablePath, string resourceDirectory)
    {
        ProcessId = processId;
        Version = version;
        ExecutablePath = executablePath;
        ResourceDirectory = resourceDirectory;
    }

    public int ProcessId { get; }

    public string Version { get; }

    public string ExecutablePath { get; }

    public string ResourceDirectory { get; }

    public override string ToString() => $"{nameof(QqRuntimeInstallation)} {{ Version = {Version} }}";
}

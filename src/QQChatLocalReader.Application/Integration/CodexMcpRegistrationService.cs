using System.Diagnostics;
using System.Text.Json;
using QQChatLocalReader.Infrastructure.Security;

namespace QQChatLocalReader.Application.Integration;

public static class CodexMcpRegistrationService
{
    public const string RegistrationName = "qq-chat-local-reader";

    public static async Task RegisterAsync(string executablePath, CancellationToken cancellationToken = default)
    {
        var fullExecutablePath = Path.GetFullPath(executablePath);
        var codexPath = FindCodexExecutable() ??
            throw new FileNotFoundException("未找到 Codex 官方命令行程序，未修改任何配置。可在应用中复制通用 MCP 配置后手动添加。");
        var existing = await GetAsync(codexPath, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            if (PointsTo(existing.RootElement, fullExecutablePath)) return;
            throw new InvalidOperationException("Codex 中已存在同名但指向其他程序的 MCP 配置，已保留原配置。请先自行处理该同名项。");
        }

        var profileStore = McpAuthorizationProfileStore.OpenDefault();
        var profile = profileStore.Create("Codex");
        try
        {
            var result = await RunAsync(
                codexPath,
                ["mcp", "add", RegistrationName, "--", fullExecutablePath, "mcp", "--profile", profile.Id.ToString("D")],
                cancellationToken).ConfigureAwait(false);
            if (result.ExitCode != 0)
            {
                throw new InvalidOperationException("Codex MCP 注册失败，未覆盖其他配置。");
            }
        }
        catch
        {
            profileStore.Delete(profile.Id);
            throw;
        }
    }

    public static async Task UnregisterAsync(string executablePath, CancellationToken cancellationToken = default)
    {
        var codexPath = FindCodexExecutable();
        if (codexPath is null) return;
        var existing = await GetAsync(codexPath, cancellationToken).ConfigureAwait(false);
        if (existing is null || !PointsTo(existing.RootElement, Path.GetFullPath(executablePath))) return;
        var result = await RunAsync(codexPath, ["mcp", "remove", RegistrationName], cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0) throw new InvalidOperationException("无法移除本程序创建的 Codex MCP 配置。");
    }

    private static string? FindCodexExecutable()
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        return path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(directory => Path.Combine(directory, "codex.exe"))
            .FirstOrDefault(File.Exists);
    }

    private static async Task<JsonDocument?> GetAsync(string codexPath, CancellationToken cancellationToken)
    {
        var result = await RunAsync(codexPath, ["mcp", "get", RegistrationName, "--json"], cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0) return null;
        try { return JsonDocument.Parse(result.StandardOutput); }
        catch (JsonException exception) { throw new InvalidDataException("Codex 返回了无法识别的 MCP 配置。", exception); }
    }

    private static bool PointsTo(JsonElement root, string executablePath)
    {
        if (TryPropertyRecursive(root, "command", out var command) && command.ValueKind == JsonValueKind.String)
        {
            var candidate = command.GetString();
            return candidate is not null && Path.GetFullPath(candidate).Equals(executablePath, StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    private static bool TryPropertyRecursive(JsonElement element, string name, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty(name, out value)) return true;
            foreach (var property in element.EnumerateObject())
                if (TryPropertyRecursive(property.Value, name, out value)) return true;
        }

        value = default;
        return false;
    }

    private static async Task<ProcessResult> RunAsync(
        string executablePath,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo(executablePath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("无法启动 Codex 官方命令行程序。");
        var standardOutput = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var standardError = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        return new ProcessResult(process.ExitCode, await standardOutput.ConfigureAwait(false), await standardError.ConfigureAwait(false));
    }

    private sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);
}

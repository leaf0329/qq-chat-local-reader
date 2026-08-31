using System.Globalization;
using System.Text.Json;
using QQChatLocalReader.Application.Mcp;
using QQChatLocalReader.Application.Integration;
using QQChatLocalReader.Infrastructure.Indexing;

namespace QQChatLocalReader.Application.CommandLine;

public static class CommandLineRunner
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    public static async Task<int> RunAsync(
        IReadOnlyList<string> args,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);
        if (args.Count == 0 || args[0] is "help" or "--help" or "-h")
        {
            await output.WriteLineAsync(HelpText).ConfigureAwait(false);
            return 0;
        }

        try
        {
            if (args[0] == "clear-index")
            {
                if (!Flag(args, "--yes"))
                {
                    throw new ArgumentException("清除索引必须明确添加 --yes；该操作不会删除 QQ 原始聊天记录。");
                }

                EncryptedMessageIndex.DeleteDefault();
                await output.WriteLineAsync("本地加密聊天索引已清除；QQ 原始聊天记录未被修改。").ConfigureAwait(false);
                return 0;
            }

            if (args[0] is "register-codex" or "unregister-codex")
            {
                var executablePath = Environment.ProcessPath ?? throw new InvalidOperationException("无法确定当前程序路径。");
                if (args[0] == "register-codex")
                    await CodexMcpRegistrationService.RegisterAsync(executablePath, cancellationToken).ConfigureAwait(false);
                else
                    await CodexMcpRegistrationService.UnregisterAsync(executablePath, cancellationToken).ConfigureAwait(false);
                await output.WriteLineAsync(args[0] == "register-codex" ? "Codex MCP 注册完成。" : "Codex MCP 注册已按安全规则移除。").ConfigureAwait(false);
                return 0;
            }

            using var runtime = ApplicationRuntime.OpenDefault();
            var tools = new QqReaderMcpTools(runtime);
            object result = args[0] switch
            {
                "list" => tools.ListIndexedConversations(Value(args, "--account")),
                "search" => tools.SearchMessages(
                    Required(args, "--account"),
                    Required(args, "--type"),
                    Required(args, "--conversation"),
                    Value(args, "--keyword"),
                    Value(args, "--sender"),
                    Value(args, "--start"),
                    Value(args, "--end"),
                    Integer(args, "--page-size", 100),
                    Value(args, "--cursor")),
                "context" => tools.ReadContext(
                    Required(args, "--account"),
                    Required(args, "--type"),
                    Required(args, "--conversation"),
                    Required(args, "--message"),
                    Integer(args, "--before", 20),
                    Integer(args, "--after", 20)),
                "sync" => tools.StartSync(
                    Required(args, "--account"),
                    Required(args, "--type"),
                    Required(args, "--conversation"),
                    Value(args, "--start"),
                    Value(args, "--end"),
                    Flag(args, "--include-forwarded")),
                "job" => tools.GetSyncJob(Guid.Parse(Required(args, "--id"))),
                "cancel" => tools.CancelSyncJob(Guid.Parse(Required(args, "--id"))),
                "export" => await tools.ExportMessages(
                    Required(args, "--account"),
                    Required(args, "--type"),
                    Required(args, "--conversation"),
                    Required(args, "--output"),
                    Value(args, "--format") ?? "markdown",
                    Value(args, "--privacy") ?? "basic",
                    Value(args, "--start"),
                    Value(args, "--end"),
                    cancellationToken).ConfigureAwait(false),
                _ => throw new ArgumentException("未知命令。使用 help 查看可用命令。"),
            };
            await output.WriteLineAsync(JsonSerializer.Serialize(result, JsonOptions)).ConfigureAwait(false);
            return 0;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await error.WriteLineAsync($"错误：{exception.Message}").ConfigureAwait(false);
            return 2;
        }
    }

    private static string Required(IReadOnlyList<string> args, string name) =>
        Value(args, name) ?? throw new ArgumentException($"缺少参数 {name}。");

    private static string? Value(IReadOnlyList<string> args, string name)
    {
        for (var index = 1; index < args.Count; index++)
        {
            if (args[index].Equals(name, StringComparison.Ordinal))
            {
                return index + 1 < args.Count
                    ? args[index + 1]
                    : throw new ArgumentException($"参数 {name} 缺少值。");
            }
        }

        return null;
    }

    private static int Integer(IReadOnlyList<string> args, string name, int fallback)
    {
        var value = Value(args, name);
        return value is null ? fallback : int.Parse(value, CultureInfo.InvariantCulture);
    }

    private static bool Flag(IReadOnlyList<string> args, string name) =>
        args.Skip(1).Any(item => item.Equals(name, StringComparison.Ordinal));

    private const string HelpText =
        """
        QQ Chat Local Reader

        不带参数                 打开图形界面
        mcp                      启动本地 STDIO MCP 服务
        list [--account 账号]    列出已索引会话
        search --account 账号 --type group|private --conversation 会话 [筛选项]
        context --account 账号 --type group|private --conversation 会话 --message 消息ID
        sync --account 账号 --type group|private --conversation 会话 [--start 时间 --end 时间]
        job --id 任务ID          查询同步任务
        cancel --id 任务ID       取消同步任务
        export --account 账号 --type group|private --conversation 会话 --output 目录
        register-codex            使用 Codex 官方命令注册本程序 MCP
        unregister-codex          仅移除仍指向本程序的 Codex MCP 注册
        clear-index --yes         清除本地加密索引（不修改 QQ 原始记录）

        时间省略时默认最近七个自然日。search 支持 --keyword、--sender、--page-size、--cursor；
        export 支持 --format markdown|json|csv 与 --privacy basic|raw（默认 basic）。
        """;
}

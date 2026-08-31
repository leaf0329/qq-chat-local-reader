using System.Windows;
using QQChatLocalReader.Application.CommandLine;
using QQChatLocalReader.Application.Mcp;

namespace QQChatLocalReader.App;

public partial class App : System.Windows.Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (e.Args.Length == 0)
        {
            new MainWindow().Show();
            return;
        }

        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        try
        {
            Environment.ExitCode = e.Args[0].Equals("mcp", StringComparison.OrdinalIgnoreCase)
                ? await RunMcpAsync().ConfigureAwait(true)
                : await CommandLineRunner.RunAsync(e.Args, Console.Out, Console.Error).ConfigureAwait(true);
        }
        finally
        {
            Shutdown(Environment.ExitCode);
        }
    }

    private static async Task<int> RunMcpAsync()
    {
        try
        {
            await McpServerRunner.RunAsync().ConfigureAwait(false);
            return 0;
        }
        catch (Exception exception)
        {
            await Console.Error.WriteLineAsync($"MCP 服务错误：{exception.Message}").ConfigureAwait(false);
            return 2;
        }
    }
}

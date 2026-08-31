using System.Windows;
using QQChatLocalReader.Application.CommandLine;
using QQChatLocalReader.Application.Mcp;
using QQChatLocalReader.Infrastructure.Security;

namespace QQChatLocalReader.App;

public partial class App : System.Windows.Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        base.OnStartup(e);
        if (e.Args.Length == 0)
        {
            var window = new MainWindow();
            MainWindow = window;
            window.Closed += (_, _) => Shutdown();
            window.Show();
            return;
        }

        try
        {
            Environment.ExitCode = e.Args[0].Equals("mcp", StringComparison.OrdinalIgnoreCase)
                ? await RunMcpAsync(e.Args).ConfigureAwait(true)
                : await CommandLineRunner.RunAsync(e.Args, Console.Out, Console.Error).ConfigureAwait(true);
        }
        finally
        {
            Shutdown(Environment.ExitCode);
        }
    }

    private static async Task<int> RunMcpAsync(string[] args)
    {
        try
        {
            var store = McpAuthorizationProfileStore.OpenDefault();
            McpAuthorizationProfile? profile = null;
            var profileIndex = Array.IndexOf(args, "--profile");
            if (profileIndex >= 0)
            {
                if (profileIndex + 1 >= args.Length || !Guid.TryParse(args[profileIndex + 1], out var profileId))
                    throw new ArgumentException("MCP 授权配置 ID 无效。");
                profile = store.Read(profileId);
            }

            await McpServerRunner.RunAsync(new McpSyncRequestAuthorizer(store, profile)).ConfigureAwait(false);
            return 0;
        }
        catch (Exception exception)
        {
            await Console.Error.WriteLineAsync($"MCP 服务错误：{exception.Message}").ConfigureAwait(false);
            return 2;
        }
    }
}

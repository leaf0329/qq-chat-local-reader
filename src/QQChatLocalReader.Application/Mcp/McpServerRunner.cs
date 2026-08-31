using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using QQChatLocalReader.Application.Sync;

namespace QQChatLocalReader.Application.Mcp;

public static class McpServerRunner
{
    public static async Task RunAsync(
        ISyncRequestAuthorizer? authorizer = null,
        CancellationToken cancellationToken = default)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);
        builder.Services.AddSingleton(_ => ApplicationRuntime.OpenDefault(authorizer));
        builder.Services
            .AddMcpServer()
            .WithStreamServerTransport(Console.OpenStandardInput(), Console.OpenStandardOutput())
            .WithTools<QqReaderMcpTools>();
        await builder.Build().RunAsync(cancellationToken).ConfigureAwait(false);
    }
}

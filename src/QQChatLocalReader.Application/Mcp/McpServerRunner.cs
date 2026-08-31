using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace QQChatLocalReader.Application.Mcp;

public static class McpServerRunner
{
    public static async Task RunAsync(CancellationToken cancellationToken = default)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);
        builder.Services.AddSingleton(_ => ApplicationRuntime.OpenDefault());
        builder.Services
            .AddMcpServer()
            .WithStreamServerTransport(Console.OpenStandardInput(), Console.OpenStandardOutput())
            .WithTools<QqReaderMcpTools>();
        await builder.Build().RunAsync(cancellationToken).ConfigureAwait(false);
    }
}

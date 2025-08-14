using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MasterMcpServer.Tools;
using MasterMcpServer.Services;

var builder = Host.CreateApplicationBuilder(args);

// Configure logging
builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);

// Add HttpClient
builder.Services.AddHttpClient();

// Add our services
builder.Services.AddSingleton<IServerProcessManager, ServerProcessManager>();
builder.Services.AddSingleton<IMcpConfigManager, McpConfigManager>();
builder.Services.AddSingleton<IServerCodeGenerator, ServerCodeGenerator>();

// Configure MCP Server with tools
builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithTools<MasterServerTools>();

// Build and run
await builder.Build().RunAsync();
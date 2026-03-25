using FlaUI.Mcp.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.Server;

var port = ParsePort(args);

if (port.HasValue)
{
    // HTTP mode — used inside Windows Sandbox or other remote environments
    var builder = WebApplication.CreateBuilder(args);
    RegisterCoreServices(builder.Services);
    builder.Services
        .AddMcpServer(options =>
        {
            options.ServerInfo = new() { Name = "windows-automation", Version = "0.1.0" };
        })
        .WithHttpTransport()
        .WithToolsFromAssembly();

    var app = builder.Build();
    app.MapMcp();
    Console.Error.WriteLine($"FlaUI-MCP starting in HTTP mode on port {port.Value}");
    app.Run($"http://0.0.0.0:{port.Value}");
}
else
{
    // Stdio mode — default, backward-compatible with existing mcp.json configs
    var builder = Host.CreateApplicationBuilder(args);
    builder.Logging.AddConsole(consoleLogOptions =>
    {
        consoleLogOptions.LogToStandardErrorThreshold = LogLevel.Trace;
    });
    RegisterCoreServices(builder.Services);
    builder.Services
        .AddMcpServer(options =>
        {
            options.ServerInfo = new() { Name = "windows-automation", Version = "0.1.0" };
        })
        .WithStdioServerTransport()
        .WithToolsFromAssembly();

    await builder.Build().RunAsync();
}

// --- Helper functions ---

static int? ParsePort(string[] args)
{
    for (int i = 0; i < args.Length - 1; i++)
    {
        if (args[i] == "--port" && int.TryParse(args[i + 1], out var port))
            return port;
    }
    return null;
}

static void RegisterCoreServices(IServiceCollection services)
{
    services.AddSingleton<SessionManager>();
    services.AddSingleton<ElementRegistry>();
}

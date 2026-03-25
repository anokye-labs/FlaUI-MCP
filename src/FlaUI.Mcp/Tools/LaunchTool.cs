using FlaUI.Mcp.Core;
using ModelContextProtocol.Server;
using System.ComponentModel;

namespace FlaUI.Mcp.Tools;

[McpServerToolType]
public class LaunchTool
{
    private readonly SessionManager _sessionManager;

    public LaunchTool(SessionManager sessionManager)
    {
        _sessionManager = sessionManager;
    }

    [McpServerTool(Name = "windows_launch"), Description(
        "Launch a Windows application. Returns a window handle for use with other tools.")]
    public string Execute(
        [Description("Path to executable or UWP app ID (e.g., 'calc.exe', 'notepad.exe', 'C:\\\\Program Files\\\\MyApp\\\\app.exe')")] string app,
        [Description("Optional command line arguments")] string[]? args = null)
    {
        if (string.IsNullOrEmpty(app))
            throw new InvalidOperationException("Missing required argument: app");

        var (handle, window) = _sessionManager.LaunchApp(app, args);
        return $"Launched {app}\nWindow handle: {handle}\nTitle: {window.Title}";
    }
}

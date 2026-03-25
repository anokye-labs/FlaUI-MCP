using FlaUI.Mcp.Core;
using ModelContextProtocol.Server;
using System.ComponentModel;

namespace FlaUI.Mcp.Tools;

[McpServerToolType]
public class ListWindowsTool
{
    private readonly SessionManager _sessionManager;

    public ListWindowsTool(SessionManager sessionManager) { _sessionManager = sessionManager; }

    [McpServerTool(Name = "windows_list_windows"), Description(
        "List all open windows with their handles, titles, and process names. " +
        "Use this to find windows to interact with.")]
    public string Execute()
    {
        var windows = _sessionManager.ListWindows();
        if (windows.Count == 0) return "No windows found";

        var lines = windows.Select(w => $"- {w.handle}: \"{w.title}\" ({w.processName ?? "unknown"})");
        return string.Join("\n", lines);
    }
}

[McpServerToolType]
public class FocusWindowTool
{
    private readonly SessionManager _sessionManager;

    public FocusWindowTool(SessionManager sessionManager) { _sessionManager = sessionManager; }

    [McpServerTool(Name = "windows_focus"), Description("Bring a window to the foreground and give it focus.")]
    public string Execute(
        [Description("Window handle from windows_list_windows or windows_launch")] string? handle = null,
        [Description("Window title (alternative to handle). Finds first window containing this text.")] string? title = null)
    {
        if (!string.IsNullOrEmpty(handle))
        {
            _sessionManager.FocusWindow(handle);
            return $"Focused window {handle}";
        }
        else if (!string.IsNullOrEmpty(title))
        {
            var (windowHandle, window) = _sessionManager.AttachToWindow(title);
            window.Focus();
            return $"Focused window \"{window.Title}\" (handle: {windowHandle})";
        }
        else
        {
            throw new InvalidOperationException("Either 'handle' or 'title' is required");
        }
    }
}

[McpServerToolType]
public class CloseWindowTool
{
    private readonly SessionManager _sessionManager;

    public CloseWindowTool(SessionManager sessionManager) { _sessionManager = sessionManager; }

    [McpServerTool(Name = "windows_close"), Description("Close a window.")]
    public string Execute(
        [Description("Window handle to close")] string handle)
    {
        _sessionManager.CloseWindow(handle);
        return $"Closed window {handle}";
    }
}

using FlaUI.Core.AutomationElements;
using FlaUI.Mcp.Core;
using ModelContextProtocol.Server;
using System.ComponentModel;

namespace FlaUI.Mcp.Tools;

[McpServerToolType]
public class SnapshotTool
{
    private readonly SessionManager _sessionManager;
    private readonly ElementRegistry _elementRegistry;
    private readonly SnapshotBuilder _snapshotBuilder;

    public SnapshotTool(SessionManager sessionManager, ElementRegistry elementRegistry)
    {
        _sessionManager = sessionManager;
        _elementRegistry = elementRegistry;
        _snapshotBuilder = new SnapshotBuilder(elementRegistry);
    }

    [McpServerTool(Name = "windows_snapshot"), Description(
        "Capture accessibility snapshot of a window. Returns a structured tree with element refs " +
        "that can be used with windows_click, windows_type, etc. This is the primary tool for " +
        "understanding window contents - use it before interacting with elements.")]
    public string Execute(
        [Description("Window handle from windows_launch or windows_list_windows. If omitted, uses the focused window.")] string? handle = null)
    {
        Window? window = null;

        if (!string.IsNullOrEmpty(handle))
        {
            window = _sessionManager.GetWindow(handle);
            if (window == null)
                throw new InvalidOperationException($"Window not found: {handle}");
        }
        else
        {
            // Delegate to shared walk-up utility on SessionManager
            window = _sessionManager.GetWindowForFocusedElement();

            if (window == null)
                throw new InvalidOperationException(
                    "No window specified and no focused window found. " +
                    "Use windows_list_windows to see available windows.");

            // Register this window
            handle = _sessionManager.RegisterWindow(window);
        }

        return _snapshotBuilder.BuildSnapshot(handle!, window);
    }
}

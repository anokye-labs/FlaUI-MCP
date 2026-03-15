using System.Drawing.Imaging;
using System.Text.Json;
using FlaUI.Core.Capturing;
using PlaywrightWindows.Mcp.Core;

namespace PlaywrightWindows.Mcp.Tools;

/// <summary>
/// Take a screenshot using HWND-based capture (no stale UIA dependency).
/// </summary>
public class ScreenshotTool : ToolBase
{
    private readonly SessionManager _sessionManager;
    private readonly ElementRegistry _elementRegistry;

    public ScreenshotTool(SessionManager sessionManager, ElementRegistry elementRegistry)
    {
        _sessionManager = sessionManager;
        _elementRegistry = elementRegistry;
    }

    public override string Name => "windows_screenshot";

    public override string Description => 
        "Take a screenshot of a window or specific element. Returns the image as base64-encoded PNG.";

    public override object InputSchema => new
    {
        type = "object",
        properties = new
        {
            handle = new
            {
                type = "string",
                description = "Window handle. If omitted, captures the foreground window."
            },
            @ref = new
            {
                type = "string",
                description = "Element ref to capture. If omitted, captures the whole window."
            },
            fullScreen = new
            {
                type = "boolean",
                description = "Capture the entire screen (default: false)"
            }
        }
    };

    public override Task<McpToolResult> ExecuteAsync(JsonElement? arguments)
    {
        var handle = GetStringArgument(arguments, "handle");
        var refId = GetStringArgument(arguments, "ref");
        var fullScreen = GetBoolArgument(arguments, "fullScreen", false);

        try
        {
            System.Drawing.Bitmap? bitmap = null;

            if (fullScreen)
            {
                bitmap = SessionManager.CaptureScreen();
            }
            else if (!string.IsNullOrEmpty(refId))
            {
                // Element capture still uses UIA (no HWND alternative for sub-elements)
                var element = _elementRegistry.GetElement(refId);
                if (element == null)
                {
                    return Task.FromResult(ErrorResult($"Element not found: {refId}"));
                }
                var capture = Capture.Element(element);
                using var stream = new MemoryStream();
                capture.Bitmap.Save(stream, ImageFormat.Png);
                return Task.FromResult(ImageResult(stream.ToArray(), "image/png"));
            }
            else if (!string.IsNullOrEmpty(handle))
            {
                // HWND-based capture — no stale UIA proxy
                bitmap = _sessionManager.CaptureWindowByHwnd(handle);
                if (bitmap == null)
                {
                    return Task.FromResult(ErrorResult($"Window not found or cannot capture: {handle}"));
                }
            }
            else
            {
                // Foreground window — try focused element walk-up, fall back to screen
                try
                {
                    var focusedElement = _sessionManager.Automation.FocusedElement();
                    if (focusedElement != null)
                    {
                        var current = focusedElement;
                        while (current != null && current.Properties.ControlType.ValueOrDefault != FlaUI.Core.Definitions.ControlType.Window)
                        {
                            current = current.Parent;
                        }
                        if (current != null)
                        {
                            var capture = Capture.Element(current);
                            using var stream = new MemoryStream();
                            capture.Bitmap.Save(stream, ImageFormat.Png);
                            return Task.FromResult(ImageResult(stream.ToArray(), "image/png"));
                        }
                    }
                }
                catch
                {
                    // UIA walk failed — fall back to full screen
                }

                bitmap = SessionManager.CaptureScreen();
            }

            using var ms = new MemoryStream();
            bitmap.Save(ms, ImageFormat.Png);
            var imageData = ms.ToArray();
            bitmap.Dispose();

            return Task.FromResult(ImageResult(imageData, "image/png"));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ErrorResult($"Failed to capture screenshot: {ex.Message}"));
        }
    }
}

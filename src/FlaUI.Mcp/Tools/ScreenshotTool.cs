using FlaUI.Core.Capturing;
using FlaUI.Mcp.Core;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.ComponentModel;

namespace FlaUI.Mcp.Tools;

[McpServerToolType]
public class ScreenshotTool
{
    private readonly SessionManager _sessionManager;
    private readonly ElementRegistry _elementRegistry;

    public ScreenshotTool(SessionManager sessionManager, ElementRegistry elementRegistry)
    {
        _sessionManager = sessionManager;
        _elementRegistry = elementRegistry;
    }

    [McpServerTool(Name = "windows_screenshot"), Description(
        "Take a screenshot of a window or specific element. Returns the image as base64-encoded PNG.")]
    public IList<ContentBlock> Execute(
        [Description("Capture the entire screen (default: false)")] bool fullScreen = false,
        [Description("Element ref to capture. If omitted, captures the whole window.")] string? @ref = null,
        [Description("Window handle. If omitted, captures the foreground window.")] string? handle = null)
    {
        CaptureImage capture;

        if (fullScreen)
        {
            capture = Capture.Screen();
        }
        else if (!string.IsNullOrEmpty(@ref))
        {
            var element = _elementRegistry.GetElement(@ref);
            if (element == null)
                throw new InvalidOperationException($"Element not found: {@ref}");
#pragma warning disable CS0618
            capture = Capture.Element(element);
#pragma warning restore CS0618
        }
        else if (!string.IsNullOrEmpty(handle))
        {
            var window = _sessionManager.GetWindow(handle);
            if (window == null)
                throw new InvalidOperationException($"Window not found: {handle}");
#pragma warning disable CS0618
            capture = Capture.Element(window);
#pragma warning restore CS0618
        }
        else
        {
            // Delegate to shared walk-up utility on SessionManager
            var window = _sessionManager.GetWindowForFocusedElement();
            if (window == null)
                throw new InvalidOperationException("No focused window found");

#pragma warning disable CS0618
            capture = Capture.Element(window);
#pragma warning restore CS0618
        }

        using var stream = new MemoryStream();
        capture.Bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
        var imageBytes = stream.ToArray();

        return new List<ContentBlock>
        {
            ImageContentBlock.FromBytes(imageBytes, "image/png")
        };
    }
}

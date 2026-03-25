using FlaUI.Core.Input;
using FlaUI.Mcp.Core;
using ModelContextProtocol.Server;
using System.ComponentModel;

namespace FlaUI.Mcp.Tools;

[McpServerToolType]
public class ClickTool
{
    private readonly ElementRegistry _elementRegistry;

    public ClickTool(ElementRegistry elementRegistry)
    {
        _elementRegistry = elementRegistry;
    }

    [McpServerTool(Name = "windows_click"), Description(
        "Click an element by its ref (from windows_snapshot). Prefers Invoke pattern for reliability, " +
        "falls back to mouse click if needed.")]
    public string Execute(
        [Description("Element ref from windows_snapshot (e.g., 'w1e5')")] string @ref,
        [Description("Mouse button to click (default: left)")] string button = "left",
        [Description("Whether to double-click (default: false)")] bool doubleClick = false)
    {
        var element = _elementRegistry.GetElement(@ref);
        if (element == null)
            throw new InvalidOperationException($"Element not found: {@ref}. Run windows_snapshot to refresh element refs.");

        var elementName = element.Properties.Name.ValueOrDefault ?? @ref;

        // Try Invoke pattern first (most reliable for buttons)
        if (button == "left" && !doubleClick && element.Patterns.Invoke.IsSupported)
        {
            element.Patterns.Invoke.Pattern.Invoke();
            return $"Invoked {elementName}";
        }

        // Try Toggle pattern for checkboxes
        if (button == "left" && !doubleClick && element.Patterns.Toggle.IsSupported)
        {
            element.Patterns.Toggle.Pattern.Toggle();
            var newState = element.Patterns.Toggle.Pattern.ToggleState.ValueOrDefault;
            return $"Toggled {elementName} to {newState}";
        }

        // Try SelectionItem pattern for list items
        if (button == "left" && !doubleClick && element.Patterns.SelectionItem.IsSupported)
        {
            element.Patterns.SelectionItem.Pattern.Select();
            return $"Selected {elementName}";
        }

        // Fall back to mouse click
        var clickPoint = element.GetClickablePoint();
        var mouseButton = button switch
        {
            "right" => MouseButton.Right,
            "middle" => MouseButton.Middle,
            _ => MouseButton.Left
        };

        if (doubleClick)
        {
            Mouse.DoubleClick(clickPoint, mouseButton);
            return $"Double-clicked {elementName}";
        }
        else
        {
            Mouse.Click(clickPoint, mouseButton);
            return $"Clicked {elementName}";
        }
    }
}

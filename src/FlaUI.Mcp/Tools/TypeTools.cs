using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using FlaUI.Mcp.Core;
using ModelContextProtocol.Server;
using System.ComponentModel;

namespace FlaUI.Mcp.Tools;

[McpServerToolType]
public class TypeTool
{
    private readonly ElementRegistry _elementRegistry;

    public TypeTool(ElementRegistry elementRegistry)
    {
        _elementRegistry = elementRegistry;
    }

    [McpServerTool(Name = "windows_type"), Description(
        "Type text into an element. The element will be focused first. " +
        "Use this for typing without clearing existing content. Use windows_fill to replace content.")]
    public string Execute(
        [Description("Text to type")] string text,
        [Description("Element ref from windows_snapshot (e.g., 'w1e5'). If omitted, types to currently focused element.")] string? @ref = null,
        [Description("Press Enter after typing (default: false)")] bool submit = false)
    {
        // Focus element if ref provided
        if (!string.IsNullOrEmpty(@ref))
        {
            var element = _elementRegistry.GetElement(@ref);
            if (element == null)
                throw new InvalidOperationException(
                    $"Element not found: {@ref}. Run windows_snapshot to refresh element refs.");

            element.Focus();
            Thread.Sleep(50); // Small delay to ensure focus
        }

        // Type the text
        Keyboard.Type(text);

        if (submit)
        {
            Keyboard.Press(VirtualKeyShort.ENTER);
        }

        var target = string.IsNullOrEmpty(@ref) ? "focused element" : @ref;
        var action = submit ? "Typed and submitted" : "Typed";
        return $"{action} \"{text}\" into {target}";
    }
}

[McpServerToolType]
public class FillTool
{
    private readonly ElementRegistry _elementRegistry;

    public FillTool(ElementRegistry elementRegistry)
    {
        _elementRegistry = elementRegistry;
    }

    [McpServerTool(Name = "windows_fill"), Description(
        "Clear and fill a text field with new value. Prefers Value pattern for reliability.")]
    public string Execute(
        [Description("Element ref from windows_snapshot (e.g., 'w1e5')")] string @ref,
        [Description("Value to fill")] string value)
    {
        var element = _elementRegistry.GetElement(@ref);
        if (element == null)
            throw new InvalidOperationException(
                $"Element not found: {@ref}. Run windows_snapshot to refresh element refs.");

        var elementName = element.Properties.Name.ValueOrDefault ?? @ref;

        // Try Value pattern first
        if (element.Patterns.Value.IsSupported)
        {
            var valuePattern = element.Patterns.Value.Pattern;
            if (!valuePattern.IsReadOnly.ValueOrDefault)
            {
                valuePattern.SetValue(value);
                return $"Filled {elementName} with \"{value}\"";
            }
        }

        // Fall back to focus + select all + type
        element.Focus();
        Thread.Sleep(50);
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_A);
        Thread.Sleep(50);
        Keyboard.Type(value);

        return $"Filled {elementName} with \"{value}\"";
    }
}

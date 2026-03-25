using FlaUI.Mcp.Core;
using ModelContextProtocol.Server;
using System.ComponentModel;

namespace FlaUI.Mcp.Tools;

[McpServerToolType]
public class GetTextTool
{
    private readonly ElementRegistry _elementRegistry;

    public GetTextTool(ElementRegistry elementRegistry)
    {
        _elementRegistry = elementRegistry;
    }

    [McpServerTool(Name = "windows_get_text"), Description(
        "Get the text content of an element. Returns the element's Name property, " +
        "or for text inputs, the current value.")]
    public string Execute(
        [Description("Element ref from windows_snapshot (e.g., 'w1e5')")] string @ref)
    {
        var element = _elementRegistry.GetElement(@ref);
        if (element == null)
            throw new InvalidOperationException(
                $"Element not found: {@ref}. Run windows_snapshot to refresh element refs.");

        string? text = null;

        // Try Value pattern first (for text inputs)
        if (element.Patterns.Value.IsSupported)
        {
            text = element.Patterns.Value.Pattern.Value.ValueOrDefault;
        }

        // Fall back to Name property
        if (string.IsNullOrEmpty(text))
        {
            text = element.Properties.Name.ValueOrDefault;
        }

        // Try Text pattern
        if (string.IsNullOrEmpty(text) && element.Patterns.Text.IsSupported)
        {
            text = element.Patterns.Text.Pattern.DocumentRange.GetText(-1);
        }

        return text ?? "";
    }
}

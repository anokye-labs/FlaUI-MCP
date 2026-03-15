using System.Text;
using System.Text.Json;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using PlaywrightWindows.Mcp.Core;

namespace PlaywrightWindows.Mcp.Tools;

/// <summary>
/// Dump the UIA automation tree for a window as structured text.
/// Re-queries the tree fresh on every call — no staleness issues.
/// Returns element names, AutomationIds, control types, enabled/visible states, 
/// and bounding rectangles.
/// </summary>
public class TreeDumpTool : ToolBase
{
    private readonly SessionManager _sessionManager;

    public TreeDumpTool(SessionManager sessionManager)
    {
        _sessionManager = sessionManager;
    }

    public override string Name => "windows_tree_dump";

    public override string Description =>
        "Dump the full UIA accessibility tree for a window as structured text. " +
        "Returns element names, AutomationIds, control types, enabled states, " +
        "and bounding rectangles. Always re-queries fresh — no stale references. " +
        "More reliable than screenshots for understanding window structure.";

    public override object InputSchema => new
    {
        type = "object",
        properties = new
        {
            handle = new
            {
                type = "string",
                description = "Window handle from windows_launch or windows_list_windows."
            },
            maxDepth = new
            {
                type = "integer",
                description = "Maximum tree depth to traverse (default: 10). Use smaller values for large windows."
            }
        },
        required = new[] { "handle" }
    };

    public override Task<McpToolResult> ExecuteAsync(JsonElement? arguments)
    {
        var handle = GetStringArgument(arguments, "handle");
        var maxDepth = GetArgument<int?>(arguments, "maxDepth") ?? 10;

        if (string.IsNullOrEmpty(handle))
        {
            return Task.FromResult(ErrorResult("Missing required argument: handle"));
        }

        try
        {
            var window = _sessionManager.GetWindow(handle);
            if (window == null)
            {
                return Task.FromResult(ErrorResult($"Window not found: {handle}"));
            }

            var sb = new StringBuilder();
            sb.AppendLine($"=== UIA Tree Dump: \"{window.Title}\" ===");
            sb.AppendLine();
            DumpElement(sb, window, 0, maxDepth);

            return Task.FromResult(TextResult(sb.ToString()));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ErrorResult($"Failed to dump tree: {ex.Message}"));
        }
    }

    private static void DumpElement(StringBuilder sb, AutomationElement element, int depth, int maxDepth)
    {
        if (depth > maxDepth) return;

        var indent = new string(' ', depth * 2);

        // Control type
        var controlType = "?";
        try { controlType = element.Properties.ControlType.ValueOrDefault.ToString(); } catch { }

        // Name
        var name = "";
        try { name = element.Properties.Name.ValueOrDefault ?? ""; } catch { }

        // AutomationId
        var automationId = "";
        try { automationId = element.Properties.AutomationId.ValueOrDefault ?? ""; } catch { }

        // Enabled
        var enabled = true;
        try { enabled = element.Properties.IsEnabled.ValueOrDefault; } catch { }

        // Bounding rectangle
        var bounds = "";
        try
        {
            var rect = element.Properties.BoundingRectangle.ValueOrDefault;
            if (!rect.IsEmpty)
                bounds = $"[{rect.X},{rect.Y} {rect.Width}x{rect.Height}]";
        }
        catch { }

        // Build line
        sb.Append(indent);
        sb.Append(controlType);

        if (!string.IsNullOrEmpty(automationId))
            sb.Append($" #{automationId}");

        if (!string.IsNullOrEmpty(name))
        {
            var displayName = name.Length > 60 ? name[..57] + "..." : name;
            sb.Append($" \"{displayName}\"");
        }

        if (!enabled)
            sb.Append(" [DISABLED]");

        if (!string.IsNullOrEmpty(bounds))
            sb.Append($" {bounds}");

        sb.AppendLine();

        // Recurse children
        try
        {
            var children = element.FindAllChildren();
            foreach (var child in children)
            {
                DumpElement(sb, child, depth + 1, maxDepth);
            }
        }
        catch { /* Element may have become stale during traversal */ }
    }
}

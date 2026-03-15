using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using PlaywrightWindows.Mcp.Core;

namespace PlaywrightWindows.Mcp.Tools;

/// <summary>
/// Dump the UIA automation tree for a window as structured text.
/// Re-queries the tree fresh on every call — no staleness issues.
/// Crash-safe: catches COMException/SEHException per node and continues.
/// NavigationView-aware: limits depth inside NavigationView to avoid
/// WinUI 3 native crashes in automation peer code.
/// </summary>
public class TreeDumpTool : ToolBase
{
    private readonly SessionManager _sessionManager;

    // Control types that crash WinUI 3 when UIA deeply enumerates their subtree
    private static readonly HashSet<string> ShallowOnlyControlTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Pane" // NavigationView renders as Pane in UIA; its item subtree triggers crashes
    };

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
                description = "Maximum tree depth to traverse (default: 3). Use smaller values for large windows."
            }
        },
        required = new[] { "handle" }
    };

    public override Task<McpToolResult> ExecuteAsync(JsonElement? arguments)
    {
        var handle = GetStringArgument(arguments, "handle");
        var maxDepth = GetArgument<int?>(arguments, "maxDepth") ?? 3;

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
            var errors = new List<string>();
            sb.AppendLine($"=== UIA Tree Dump: \"{window.Title}\" ===");
            sb.AppendLine();
            DumpElement(sb, errors, window, 0, maxDepth, "root");

            if (errors.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine($"--- {errors.Count} node(s) skipped due to UIA errors ---");
                foreach (var err in errors)
                    sb.AppendLine($"  {err}");
            }

            return Task.FromResult(TextResult(sb.ToString()));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ErrorResult($"Failed to dump tree: {ex.Message}"));
        }
    }

    private static void DumpElement(StringBuilder sb, List<string> errors,
        AutomationElement element, int depth, int maxDepth, string path)
    {
        if (depth > maxDepth) return;

        var indent = new string(' ', depth * 2);

        // Read properties with per-property crash safety
        var controlType = SafeGet(() => element.Properties.ControlType.ValueOrDefault.ToString(), "?");
        var name = SafeGet(() => element.Properties.Name.ValueOrDefault ?? "", "");
        var automationId = SafeGet(() => element.Properties.AutomationId.ValueOrDefault ?? "", "");
        var enabled = SafeGet(() => element.Properties.IsEnabled.ValueOrDefault, true);
        var className = SafeGet(() => element.Properties.ClassName.ValueOrDefault ?? "", "");

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

        // NavigationView workaround: limit depth inside NavigationView subtrees
        // to avoid WinUI 3 native crash in automation peer code.
        bool isNavigationView = className.Contains("NavigationView", StringComparison.OrdinalIgnoreCase);
        if (isNavigationView)
        {
            sb.Append(" [NavigationView: children only]");
        }

        sb.AppendLine();

        // Determine effective max depth for children
        int childMaxDepth = isNavigationView ? depth + 1 : maxDepth;

        // Recurse children with crash safety per child
        try
        {
            var children = element.FindAllChildren();
            foreach (var child in children)
            {
                try
                {
                    var childPath = $"{path}/{controlType}";
                    DumpElement(sb, errors, child, depth + 1, childMaxDepth, childPath);
                }
                catch (COMException ex)
                {
                    errors.Add($"COMException at {path}: HRESULT=0x{ex.HResult:X8}");
                }
                catch (SEHException ex)
                {
                    errors.Add($"SEHException at {path}: HRESULT=0x{ex.HResult:X8}");
                }
                catch (Exception ex)
                {
                    errors.Add($"{ex.GetType().Name} at {path}: {ex.Message}");
                }
            }
        }
        catch (COMException ex)
        {
            errors.Add($"COMException enumerating children at {path}: HRESULT=0x{ex.HResult:X8}");
        }
        catch (SEHException ex)
        {
            errors.Add($"SEHException enumerating children at {path}: HRESULT=0x{ex.HResult:X8}");
        }
        catch { /* Element may have become stale during traversal */ }
    }

    private static T SafeGet<T>(Func<T> getter, T fallback)
    {
        try { return getter(); }
        catch { return fallback; }
    }
}

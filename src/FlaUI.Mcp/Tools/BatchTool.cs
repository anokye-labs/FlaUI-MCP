using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using FlaUI.Mcp.Core;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text.Json;

namespace FlaUI.Mcp.Tools;

[McpServerToolType]
public class BatchTool
{
    private readonly SessionManager _sessionManager;
    private readonly ElementRegistry _elementRegistry;
    private readonly SnapshotBuilder _snapshotBuilder;

    public BatchTool(SessionManager sessionManager, ElementRegistry elementRegistry)
    {
        _sessionManager = sessionManager;
        _elementRegistry = elementRegistry;
        _snapshotBuilder = new SnapshotBuilder(elementRegistry);
    }

    [McpServerTool(Name = "windows_batch"), Description(
        "Execute multiple actions in a single call. Much faster than individual calls. " +
        "Supports click, type, fill, wait, and snapshot actions. Returns results for each action.")]
    public string Execute(
        [Description("List of actions to execute in order (click/type/fill/wait/snapshot)")] JsonElement actions,
        [Description("Stop executing if an action fails (default: true)")] bool stopOnError = true)
    {
        if (actions.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("actions must be a JSON array");

        var results = new List<string>();
        var actionList = actions.EnumerateArray().ToList();

        foreach (var (actionObj, index) in actionList.Select((a, i) => (a, i)))
        {
            try
            {
                var actionType = actionObj.GetProperty("action").GetString();
                var result = actionType switch
                {
                    "click" => ExecuteClick(actionObj),
                    "type" => ExecuteType(actionObj),
                    "fill" => ExecuteFill(actionObj),
                    "wait" => ExecuteWait(actionObj),
                    "snapshot" => ExecuteSnapshot(actionObj),
                    _ => $"Unknown action: {actionType}"
                };
                results.Add($"{index + 1}. {actionType}: {result}");
            }
            catch (Exception ex)
            {
                results.Add($"{index + 1}. ERROR: {ex.Message}");
                if (stopOnError)
                {
                    results.Add($"Stopped at action {index + 1} due to error");
                    break;
                }
            }
        }

        return string.Join("\n", results);
    }

    private string ExecuteClick(JsonElement action)
    {
        var refId = action.TryGetProperty("ref", out var refProp) ? refProp.GetString() : null;
        if (string.IsNullOrEmpty(refId)) return "Missing ref";

        var element = _elementRegistry.GetElement(refId);
        if (element == null) return $"Element not found: {refId}";

        var elementName = element.Properties.Name.ValueOrDefault ?? refId;

        if (element.Patterns.Invoke.IsSupported)
        {
            element.Patterns.Invoke.Pattern.Invoke();
            return $"Invoked {elementName}";
        }

        if (element.Patterns.Toggle.IsSupported)
        {
            element.Patterns.Toggle.Pattern.Toggle();
            return $"Toggled {elementName}";
        }

        var clickPoint = element.GetClickablePoint();
        Mouse.Click(clickPoint);
        return $"Clicked {elementName}";
    }

    private string ExecuteType(JsonElement action)
    {
        var text = action.TryGetProperty("text", out var textProp) ? textProp.GetString() : null;
        if (string.IsNullOrEmpty(text)) return "Missing text";

        var refId = action.TryGetProperty("ref", out var refProp) ? refProp.GetString() : null;
        if (!string.IsNullOrEmpty(refId))
        {
            var element = _elementRegistry.GetElement(refId);
            if (element == null) return $"Element not found: {refId}";
            element.Focus();
            Thread.Sleep(30);
        }

        Keyboard.Type(text);
        return $"Typed \"{text}\"";
    }

    private string ExecuteFill(JsonElement action)
    {
        var refId = action.TryGetProperty("ref", out var refProp) ? refProp.GetString() : null;
        var value = action.TryGetProperty("value", out var valProp) ? valProp.GetString() : null;

        if (string.IsNullOrEmpty(refId) || value == null) return "Missing ref or value";

        var element = _elementRegistry.GetElement(refId);
        if (element == null) return $"Element not found: {refId}";

        if (element.Patterns.Value.IsSupported)
        {
            element.Patterns.Value.Pattern.SetValue(value);
            return $"Filled with \"{value}\"";
        }

        element.Focus();
        Thread.Sleep(30);
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_A);
        Thread.Sleep(30);
        Keyboard.Type(value);
        return $"Filled with \"{value}\"";
    }

    private string ExecuteWait(JsonElement action)
    {
        var ms = action.TryGetProperty("ms", out var msProp) ? msProp.GetInt32() : 100;
        Thread.Sleep(ms);
        return $"Waited {ms}ms";
    }

    private string ExecuteSnapshot(JsonElement action)
    {
        var handle = action.TryGetProperty("handle", out var handleProp) ? handleProp.GetString() : null;

        Window? window = null;
        if (!string.IsNullOrEmpty(handle))
        {
            window = _sessionManager.GetWindow(handle);
            if (window == null) return $"Window not found: {handle}";
        }
        else
        {
            var focusedElement = _sessionManager.Automation.FocusedElement();
            if (focusedElement != null)
            {
                var current = focusedElement;
                while (current != null)
                {
                    if (current.Properties.ControlType.ValueOrDefault == FlaUI.Core.Definitions.ControlType.Window)
                    {
                        window = current.AsWindow();
                        handle = _sessionManager.RegisterWindow(window);
                        break;
                    }
                    current = current.Parent;
                }
            }
        }

        if (window == null) return "No window found";
        var snapshot = _snapshotBuilder.BuildSnapshot(handle!, window);
        return $"\n{snapshot}";
    }
}

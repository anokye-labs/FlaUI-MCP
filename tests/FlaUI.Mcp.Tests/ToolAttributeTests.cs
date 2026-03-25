using System.Reflection;
using ModelContextProtocol.Server;
using FlaUI.Mcp.Tools;

namespace FlaUI.Mcp.Tests;

/// <summary>
/// Verifies that all tool classes use the SDK [McpServerToolType]/[McpServerTool] attribute
/// pattern instead of the hand-rolled ToolBase/ITool pattern.
/// </summary>
public class ToolAttributeTests
{
    [Theory]
    [InlineData(typeof(ClickTool), "windows_click")]
    [InlineData(typeof(LaunchTool), "windows_launch")]
    [InlineData(typeof(GetTextTool), "windows_get_text")]
    [InlineData(typeof(TypeTool), "windows_type")]
    [InlineData(typeof(FillTool), "windows_fill")]
    [InlineData(typeof(ListWindowsTool), "windows_list_windows")]
    [InlineData(typeof(FocusWindowTool), "windows_focus")]
    [InlineData(typeof(CloseWindowTool), "windows_close")]
    [InlineData(typeof(SnapshotTool), "windows_snapshot")]
    [InlineData(typeof(ScreenshotTool), "windows_screenshot")]
    [InlineData(typeof(BatchTool), "windows_batch")]
    public void Tool_HasMcpServerToolTypeAttribute(Type toolType, string _)
    {
        var attribute = toolType.GetCustomAttribute<McpServerToolTypeAttribute>();
        Assert.NotNull(attribute);
    }

    [Theory]
    [InlineData(typeof(ClickTool), "windows_click")]
    [InlineData(typeof(LaunchTool), "windows_launch")]
    [InlineData(typeof(GetTextTool), "windows_get_text")]
    [InlineData(typeof(TypeTool), "windows_type")]
    [InlineData(typeof(FillTool), "windows_fill")]
    [InlineData(typeof(ListWindowsTool), "windows_list_windows")]
    [InlineData(typeof(FocusWindowTool), "windows_focus")]
    [InlineData(typeof(CloseWindowTool), "windows_close")]
    [InlineData(typeof(SnapshotTool), "windows_snapshot")]
    [InlineData(typeof(ScreenshotTool), "windows_screenshot")]
    [InlineData(typeof(BatchTool), "windows_batch")]
    public void Tool_HasMcpServerToolAttribute_WithCorrectName(Type toolType, string expectedName)
    {
        var methods = toolType.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        var toolMethod = methods.FirstOrDefault(m => m.GetCustomAttribute<McpServerToolAttribute>() != null);
        Assert.NotNull(toolMethod);
        var attr = toolMethod!.GetCustomAttribute<McpServerToolAttribute>()!;
        Assert.Equal(expectedName, attr.Name);
    }

    [Theory]
    [InlineData(typeof(ClickTool))]
    [InlineData(typeof(LaunchTool))]
    [InlineData(typeof(GetTextTool))]
    [InlineData(typeof(TypeTool))]
    [InlineData(typeof(FillTool))]
    [InlineData(typeof(ListWindowsTool))]
    [InlineData(typeof(FocusWindowTool))]
    [InlineData(typeof(CloseWindowTool))]
    [InlineData(typeof(SnapshotTool))]
    [InlineData(typeof(ScreenshotTool))]
    [InlineData(typeof(BatchTool))]
    public void Tool_DoesNotInheritFromToolBase(Type toolType)
    {
        // Tool classes must NOT inherit from the old hand-rolled ToolBase
        Assert.Equal(typeof(object), toolType.BaseType);
    }

    [Fact]
    public void AllElevenToolsArePresent()
    {
        var toolAssembly = typeof(ClickTool).Assembly;
        var toolTypes = toolAssembly.GetTypes()
            .Where(t => t.Namespace == "FlaUI.Mcp.Tools"
                     && t.GetCustomAttribute<McpServerToolTypeAttribute>() != null)
            .ToList();

        var expectedToolNames = new[]
        {
            "windows_click", "windows_launch", "windows_snapshot",
            "windows_type", "windows_fill", "windows_get_text",
            "windows_screenshot", "windows_list_windows", "windows_focus",
            "windows_close", "windows_batch"
        };

        var foundNames = toolTypes
            .SelectMany(t => t.GetMethods(
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Select(m => m.GetCustomAttribute<McpServerToolAttribute>()?.Name)
            .Where(n => n != null)
            .ToHashSet();

        foreach (var expectedName in expectedToolNames)
        {
            Assert.Contains(expectedName, foundNames);
        }
    }
}

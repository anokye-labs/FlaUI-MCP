using System.Reflection;
using FlaUI.Mcp.Core;

namespace FlaUI.Mcp.Tests;

/// <summary>
/// Verifies quality fixes: DRY refactoring and behavioral consistency in BatchTool.
/// </summary>
public class QualityFixTests
{
    // Issue 3: "Walk up to find focused window" is duplicated across SnapshotTool,
    // ScreenshotTool, and BatchTool. Extract to SessionManager.GetWindowForFocusedElement().
    [Fact]
    public void SessionManager_HasGetWindowForFocusedElementMethod()
    {
        var method = typeof(SessionManager).GetMethod(
            "GetWindowForFocusedElement",
            BindingFlags.Public | BindingFlags.Instance);

        Assert.NotNull(method);
    }

    [Fact]
    public void SessionManager_GetWindowForFocusedElement_ReturnsWindowType()
    {
        // Verify the return type is Window (nullable reference type)
        var method = typeof(SessionManager).GetMethod(
            "GetWindowForFocusedElement",
            BindingFlags.Public | BindingFlags.Instance);

        Assert.NotNull(method);
        Assert.Equal(typeof(FlaUI.Core.AutomationElements.Window), method!.ReturnType);
    }
}

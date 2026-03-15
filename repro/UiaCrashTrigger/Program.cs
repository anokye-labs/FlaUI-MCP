using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;

if (args.Length == 0)
{
    Console.WriteLine("Usage: UiaCrashTrigger <target-pid>");
    return 1;
}

var targetPid = int.Parse(args[0]);
Console.WriteLine($"[TRIGGER] Target PID: {targetPid}");
Console.WriteLine("[TRIGGER] Waiting 2 seconds for window to settle...");
Thread.Sleep(2000);

using var automation = new UIA3Automation();
var desktop = automation.GetDesktop();

Console.WriteLine("[TRIGGER] Searching for target window...");
var window = desktop.FindFirstDescendant(cf => cf.ByProcessId(targetPid));
if (window == null)
{
    Console.WriteLine("[TRIGGER] ERROR: Could not find window for target PID.");
    return 1;
}

Console.WriteLine($"[TRIGGER] Found: \"{window.Name}\". Walking with TreeWalker (like FlaUInspect)...");

try
{
    var walker = automation.TreeWalkerFactory.GetRawViewWalker();
    for (int attempt = 1; attempt <= 10; attempt++)
    {
        Console.WriteLine($"[TRIGGER] TreeWalker pass {attempt}/10...");
        WalkWithTreeWalker(walker, window, 0);
        Thread.Sleep(200);
    }
    Console.WriteLine("[TRIGGER] All walks completed. Target did NOT crash.");
}
catch (Exception ex)
{
    Console.WriteLine($"[TRIGGER] Walk failed: {ex.GetType().Name}: {ex.Message}");
    Console.WriteLine("[TRIGGER] Target process likely crashed.");
}

return 0;

static void WalkWithTreeWalker(ITreeWalker walker, AutomationElement el, int depth)
{
    if (depth > 15) return;
    try
    {
        _ = el.ControlType;
        _ = el.Name;
        _ = el.AutomationId;
        _ = el.BoundingRectangle;
        
        // Use TreeWalker navigation (like FlaUInspect does)
        var child = walker.GetFirstChild(el);
        while (child != null)
        {
            WalkWithTreeWalker(walker, child, depth + 1);
            child = walker.GetNextSibling(child);
        }
    }
    catch { }
}

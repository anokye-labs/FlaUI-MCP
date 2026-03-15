using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;

if (args.Length == 0)
{
    Console.WriteLine("Usage: UiaCrashTrigger <target-pid> [--dump]");
    return 1;
}

var targetPid = int.Parse(args[0]);
var dumpMode = args.Length > 1 && args[1] == "--dump";

Console.WriteLine($"[TRIGGER] Target PID: {targetPid}, Mode: {(dumpMode ? "DUMP" : "WALK")}");
Thread.Sleep(2000);

using var automation = new UIA3Automation();
var desktop = automation.GetDesktop();
var window = desktop.FindFirstDescendant(cf => cf.ByProcessId(targetPid));
if (window == null)
{
    Console.WriteLine("[TRIGGER] ERROR: Could not find window.");
    return 1;
}

Console.WriteLine($"[TRIGGER] Found: \"{window.Name}\"");
var walker = automation.TreeWalkerFactory.GetRawViewWalker();

if (dumpMode)
{
    DumpTree(walker, window, 0);
}
else
{
    try
    {
        for (int attempt = 1; attempt <= 10; attempt++)
        {
            Console.WriteLine($"[TRIGGER] TreeWalker pass {attempt}/10...");
            WalkTree(walker, window, 0);
            Thread.Sleep(200);
        }
        Console.WriteLine("[TRIGGER] All walks completed. Target did NOT crash.");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[TRIGGER] Walk failed: {ex.GetType().Name}: {ex.Message}");
    }
}

return 0;

static void DumpTree(ITreeWalker walker, AutomationElement el, int depth)
{
    if (depth > 8) { Console.WriteLine($"{new string(' ', depth*2)}..."); return; }
    try
    {
        var ct = el.ControlType;
        var name = "";
        var aid = "";
        var cls = "";
        try { name = el.Name ?? ""; } catch { }
        try { aid = el.AutomationId ?? ""; } catch { }
        try { cls = el.ClassName ?? ""; } catch { }
        Console.WriteLine($"{new string(' ', depth*2)}[{ct}] \"{name}\" id=\"{aid}\" class=\"{cls}\"");
        var child = walker.GetFirstChild(el);
        while (child != null)
        {
            DumpTree(walker, child, depth + 1);
            child = walker.GetNextSibling(child);
        }
    }
    catch (Exception ex) { Console.WriteLine($"{new string(' ', depth*2)}[ERROR] {ex.Message}"); }
}

static void WalkTree(ITreeWalker walker, AutomationElement el, int depth)
{
    if (depth > 15) return;
    try
    {
        _ = el.ControlType; _ = el.Name; _ = el.AutomationId; _ = el.BoundingRectangle;
        var child = walker.GetFirstChild(el);
        while (child != null)
        {
            WalkTree(walker, child, depth + 1);
            child = walker.GetNextSibling(child);
        }
    }
    catch { }
}

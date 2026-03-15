using Interop.UIAutomationClient;
using System.Runtime.InteropServices;

if (args.Length == 0)
{
    Console.WriteLine("Usage: UiaCrashTrigger <target-pid> [--dump]");
    return 1;
}

var targetPid = int.Parse(args[0]);
var dumpMode = args.Length > 1 && args[1] == "--dump";

Console.WriteLine($"[TRIGGER] Target PID: {targetPid}, Mode: {(dumpMode ? "DUMP" : "WALK")}");
Thread.Sleep(2000);

var uia = new CUIAutomation8();
var walker = uia.RawViewWalker;
var root = uia.GetRootElement();

// Find window by walking desktop children and matching PID
Console.WriteLine("[TRIGGER] Searching for target window...");
IUIAutomationElement? window = null;
var child = walker.GetFirstChildElement(root);
while (child != null)
{
    try
    {
        if (child.CurrentProcessId == targetPid)
        {
            window = child;
            break;
        }
    }
    catch { }
    child = walker.GetNextSiblingElement(child);
}

if (window == null)
{
    Console.WriteLine("[TRIGGER] ERROR: Could not find window.");
    return 1;
}

Console.WriteLine($"[TRIGGER] Found: \"{window.CurrentName}\"");

if (dumpMode)
{
    DumpTree(walker, window, 0);
}
else
{
    try
    {
        for (int attempt = 1; attempt <= 20; attempt++)
        {
            Console.WriteLine($"[TRIGGER] Raw COM walk {attempt}/20...");
            WalkTree(walker, window, 0);
            Thread.Sleep(100);
        }
        Console.WriteLine("[TRIGGER] All walks completed. Target did NOT crash.");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[TRIGGER] Failed: {ex.GetType().Name}: {ex.Message}");
    }
}

return 0;

static void DumpTree(IUIAutomationTreeWalker walker, IUIAutomationElement el, int depth)
{
    if (depth > 2) { Console.WriteLine($"{new string(' ', depth * 2)}..."); return; }
    try
    {
        var name = ""; try { name = el.CurrentName ?? ""; } catch { }
        var aid = ""; try { aid = el.CurrentAutomationId ?? ""; } catch { }
        var cls = ""; try { cls = el.CurrentClassName ?? ""; } catch { }
        var ct = 0; try { ct = el.CurrentControlType; } catch { }
        Console.WriteLine($"{new string(' ', depth * 2)}[{ct}] \"{name}\" id=\"{aid}\" class=\"{cls}\"");

        var child = walker.GetFirstChildElement(el);
        while (child != null)
        {
            DumpTree(walker, child, depth + 1);
            child = walker.GetNextSiblingElement(child);
        }
    }
    catch { Console.WriteLine($"{new string(' ', depth * 2)}[ERROR]"); }
}

static void WalkTree(IUIAutomationTreeWalker walker, IUIAutomationElement el, int depth)
{
    if (depth > 2) return;
    try
    {
        _ = el.CurrentName;
        _ = el.CurrentControlType;
        var child = walker.GetFirstChildElement(el);
        while (child != null)
        {
            WalkTree(walker, child, depth + 1);
            child = walker.GetNextSiblingElement(child);
        }
    }
    catch { }
}


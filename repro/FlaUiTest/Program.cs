using FlaUI.UIA3;
using FlaUI.Core.AutomationElements;

var pid = int.Parse(args[0]);
using var automation = new UIA3Automation();
var desktop = automation.GetDesktop();
var window = desktop.FindFirstDescendant(cf => cf.ByProcessId(pid));
Console.WriteLine("Found: " + window?.Name);

void Walk(AutomationElement el, int depth) {
    if (depth > 10) return;
    try {
        Console.WriteLine(new string(' ', depth*2) + "[" + el.ControlType + "] " + el.Name);
        foreach (var child in el.FindAllChildren())
            Walk(child, depth + 1);
    } catch (Exception ex) { Console.WriteLine(new string(' ', depth*2) + "ERROR: " + ex.Message); }
}

Walk(window, 0);
Console.WriteLine("DONE - app still alive");

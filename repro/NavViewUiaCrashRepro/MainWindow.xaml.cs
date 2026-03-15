using System;
using System.Threading.Tasks;
using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Window = Microsoft.UI.Xaml.Window;

namespace NavViewUiaCrashRepro;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        this.InitializeComponent();
        this.Title = "NavView UIA Crash Repro";
    }

    private async void TriggerUiaEnumeration_Click(object sender, RoutedEventArgs e)
    {
        StatusText.Text = "Walking UIA tree...";
        var pid = Environment.ProcessId;

        await Task.Run(() =>
        {
            try
            {
                using var automation = new UIA3Automation();
                var desktop = automation.GetDesktop();
                var window = desktop.FindFirstDescendant(cf => cf.ByProcessId(pid));
                if (window == null)
                {
                    Console.WriteLine("[UIA] Could not find own window.");
                    return;
                }

                Console.WriteLine("[UIA] Found window. Walking tree...");
                WalkTree(window, 0);
                Console.WriteLine("[UIA] Walk completed without crash.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[UIA] Walk threw: {ex.GetType().Name}: {ex.Message}");
            }
        });

        StatusText.Text = "Walk completed (if you see this, no crash occurred)";
    }

    private static void WalkTree(AutomationElement el, int depth)
    {
        if (depth > 10) return;
        try
        {
            var controlType = el.ControlType;
            var name = el.Name ?? "(null)";
            var automationId = el.AutomationId ?? "(null)";
            Console.WriteLine($"{new string(' ', depth * 2)}[{controlType}] {name} / {automationId}");

            foreach (var child in el.FindAllChildren())
                WalkTree(child, depth + 1);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"{new string(' ', depth * 2)}[ERROR] {ex.GetType().Name}: {ex.Message}");
        }
    }
}

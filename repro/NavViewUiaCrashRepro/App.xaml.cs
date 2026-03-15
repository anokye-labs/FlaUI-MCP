using System;
using Microsoft.UI.Xaml;

namespace NavViewUiaCrashRepro;

public partial class App : Application
{
    public App()
    {
        this.InitializeComponent();

        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            Console.WriteLine($"UNHANDLED: {e.ExceptionObject}");

        this.UnhandledException += (s, e) =>
        {
            e.Handled = true;
            Console.WriteLine($"WINUI UNHANDLED: {e.Exception}");
        };
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new MainWindow();
        _window.Activate();
    }

    private Window? _window;
}

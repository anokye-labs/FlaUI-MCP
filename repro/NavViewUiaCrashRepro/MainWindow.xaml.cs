using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Window = Microsoft.UI.Xaml.Window;

namespace NavViewUiaCrashRepro;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        this.InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
    }

    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.IsSettingsSelected)
        {
            ContentTitle.Text = "Settings";
            ContentSubtitle.Text = "Configure providers, models, and preferences.";
            return;
        }

        if (args.SelectedItem is NavigationViewItem item)
        {
            var tag = item.Tag?.ToString();
            if (tag == "newchat")
            {
                ContentTitle.Text = "New Chat";
                ContentSubtitle.Text = "Start a conversation with an AI agent.";
            }
            else
            {
                ContentTitle.Text = item.Content?.ToString() ?? "";
                ContentSubtitle.Text = $"Chat session: {tag}";
            }
        }
    }
}

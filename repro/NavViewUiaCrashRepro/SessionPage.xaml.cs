using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace NavViewUiaCrashRepro;

public sealed partial class SessionPage : Page
{
    public SessionPage()
    {
        this.InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        if (e.Parameter is string title)
            SessionTitle.Text = title;
    }
}

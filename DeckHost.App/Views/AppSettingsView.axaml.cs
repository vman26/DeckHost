using Avalonia.Controls;
using Avalonia.Interactivity;

namespace DeckHost.App.Views;

public partial class AppSettingsView : UserControl
{
    public AppSettingsView()
    {
        InitializeComponent();
    }

    private void OpenSupport_OnClick(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is MainWindow main)
        {
            main.OpenSupport();
        }
        else
        {
            Pages.Open(this, "Help and support", new SupportView { DataContext = new ViewModels.SupportViewModel() });
        }
    }

}

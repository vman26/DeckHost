using Avalonia.Controls;
using Avalonia.Interactivity;
using DeckHost.App.ViewModels;

namespace DeckHost.App.Views;

/// <summary>"New in DeckHost &lt;version&gt;": the changelog's notes for the version, once after an update.</summary>
public partial class WhatsNewView : UserControl
{
    public WhatsNewView()
    {
        InitializeComponent();
    }

    private void Close_OnClick(object? sender, RoutedEventArgs e)
    {
        (DataContext as WhatsNewViewModel)?.CloseCommand.Execute(null);
        Pages.Close(this);
    }
}

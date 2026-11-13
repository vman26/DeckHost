using Avalonia.Controls;
using Avalonia.Interactivity;
using DeckHost.App.ViewModels;

namespace DeckHost.App.Views;

/// <summary>An available update: the changelog since the running version, install, skip or later.</summary>
public partial class UpdateView : UserControl
{
    public UpdateView()
    {
        InitializeComponent();
    }

    private void Later_OnClick(object? sender, RoutedEventArgs e)
    {
        (DataContext as UpdateViewModel)?.LaterCommand.Execute(null);
        Pages.Close(this);
    }
}

using Avalonia.Controls;
using Avalonia.Interactivity;
using DeckHost.App.ViewModels;

namespace DeckHost.App.Views;

/// <summary>An available update: the changelog since the running version, install, skip or later.</summary>
public partial class UpdateWindow : Window
{
    private static UpdateWindow? _open;

    public UpdateWindow()
    {
        InitializeComponent();
    }

    /// <summary>Shows the window (one at a time), over <paramref name="owner"/> when it's visible.</summary>
    public static void ShowFor(Window? owner, UpdateViewModel updates)
    {
        if (_open is { IsVisible: true })
        {
            _open.Activate();
            return;
        }

        _open = new UpdateWindow { DataContext = updates };
        _open.Closed += (_, _) => _open = null;
        if (owner is { IsVisible: true })
        {
            _open.Show(owner);
        }
        else
        {
            _open.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            _open.Show();
        }
    }

    private void Later_OnClick(object? sender, RoutedEventArgs e)
    {
        (DataContext as UpdateViewModel)?.LaterCommand.Execute(null);
        Close();
    }
}

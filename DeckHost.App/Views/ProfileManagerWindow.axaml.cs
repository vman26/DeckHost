using Avalonia.Controls;
using DeckHost.App.ViewModels;
using System;

namespace DeckHost.App.Views;

public partial class ProfileManagerWindow : Window
{
    public ProfileManagerWindow()
    {
        InitializeComponent();
    }

    protected override void OnClosed(EventArgs e)
    {
        (DataContext as ProfileManagerViewModel)?.Detach();
        base.OnClosed(e);
    }
}

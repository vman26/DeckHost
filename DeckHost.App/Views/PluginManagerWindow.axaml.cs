using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using DeckHost.App.ViewModels;
using System;

namespace DeckHost.App.Views;

public partial class PluginManagerWindow : Window
{
    public PluginManagerWindow()
    {
        InitializeComponent();
    }

    protected override void OnClosed(EventArgs e)
    {
        (DataContext as PluginManagerViewModel)?.Detach();
        base.OnClosed(e);
    }

    private async void InstallPlugin_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not PluginManagerViewModel vm)
        {
            return;
        }

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Install a DeckHost plugin package",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("DeckHost plugin package") { Patterns = ["*.zip", "*.dhplugin"] },
            ],
        });

        if (files.Count > 0 && files[0].TryGetLocalPath() is { } path)
        {
            vm.Install(path);
        }
    }
}

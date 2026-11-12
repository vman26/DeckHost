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

    private async void Tabs_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        // The catalogs are only fetched once the Store tab is opened.
        if (sender is TabControl { SelectedIndex: 1 } && DataContext is PluginManagerViewModel vm)
        {
            await vm.Store.EnsureLoadedAsync();
        }
    }

    private void DeviceSettings_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: DeviceInfoViewModel device }
            && DataContext is PluginManagerViewModel vm
            && vm.CreateDeviceSettings(device) is { } settings)
        {
            new DeviceSettingsWindow { DataContext = settings }.Show(this);
        }
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
                new FilePickerFileType("Plugin packages (DeckHost, Stream Deck)") { Patterns = ["*.zip", "*.dhplugin", "*.streamDeckPlugin"] },
            ],
        });

        if (files.Count > 0 && files[0].TryGetLocalPath() is { } path)
        {
            await vm.InstallAsync(path);
        }
    }
}

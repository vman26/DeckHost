using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using DeckHost.App.ViewModels;
using System;

namespace DeckHost.App.Views;

public partial class PluginManagerView : UserControl
{
    public PluginManagerView()
    {
        InitializeComponent();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        (DataContext as PluginManagerViewModel)?.Detach();
        base.OnDetachedFromVisualTree(e);
    }

    private async void Tabs_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        // The catalogs are only fetched once the Store tab is opened.
        if (sender is TabControl { SelectedIndex: 1 } && DataContext is PluginManagerViewModel vm)
        {
            await vm.Store.EnsureLoadedAsync();
        }
    }

    private void Documentation_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: PluginEntryViewModel entry } &&
            CommunityToolkit.Mvvm.DependencyInjection.Ioc.Default.GetService<PluginDocsService>()?.Create(entry.Id) is { } docs)
        {
            Pages.Open(this, entry.Name, new PluginDocsView { DataContext = docs });
        }
    }

    private void Devices_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is PluginManagerViewModel vm)
        {
            Pages.Open(this, "Devices", new DeviceManagerView { DataContext = vm.CreateDeviceManager() });
        }
    }

    private async void InstallPlugin_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not PluginManagerViewModel vm)
        {
            return;
        }

        var files = await TopLevel.GetTopLevel(this)!.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
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

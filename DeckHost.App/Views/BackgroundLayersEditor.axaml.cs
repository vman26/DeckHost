using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using DeckHost.App.ViewModels;

namespace DeckHost.App.Views;

/// <summary>The Background Studio's layer list (see <see cref="BackgroundLayersViewModel"/>).</summary>
public partial class BackgroundLayersEditor : UserControl
{
    public BackgroundLayersEditor() => InitializeComponent();

    private void LayerUp_OnClick(object? sender, RoutedEventArgs e) => With(sender, static (vm, row) => vm.Move(row, -1));

    private void LayerDown_OnClick(object? sender, RoutedEventArgs e) => With(sender, static (vm, row) => vm.Move(row, 1));

    private void LayerRemove_OnClick(object? sender, RoutedEventArgs e) => With(sender, static (vm, row) => vm.Remove(row));

    private async void LayerBrowse_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: BackgroundLayerRowViewModel row } || TopLevel.GetTopLevel(this) is not { } top)
        {
            return;
        }

        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select a background image",
            AllowMultiple = false,
            FileTypeFilter = [FilePickerFileTypes.ImageAll],
        });
        if (files.Count > 0 && files[0].TryGetLocalPath() is { } path)
        {
            row.ImagePath = path;
        }
    }

    private void With(object? sender, Action<BackgroundLayersViewModel, BackgroundLayerRowViewModel> action)
    {
        if (sender is Control { DataContext: BackgroundLayerRowViewModel row } && DataContext is BackgroundLayersViewModel vm)
        {
            action(vm, row);
        }
    }
}

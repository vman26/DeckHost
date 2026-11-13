using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using DeckHost.App.ViewModels;
using System;

namespace DeckHost.App.Views;

public partial class ProfileManagerView : UserControl
{
    public ProfileManagerView()
    {
        InitializeComponent();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        (DataContext as ProfileManagerViewModel)?.Detach();
        base.OnDetachedFromVisualTree(e);
    }

    private async void BrowseBackground_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ProfileManagerViewModel vm)
        {
            return;
        }

        var files = await TopLevel.GetTopLevel(this)!.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select a profile background",
            AllowMultiple = false,
            FileTypeFilter = [FilePickerFileTypes.ImageAll],
        });

        if (files.Count > 0 && files[0].TryGetLocalPath() is { } path)
        {
            vm.EditingBackgroundImage = path;
        }
    }

    private async void BrowseDefaultBackground_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ProfileManagerViewModel vm)
        {
            return;
        }

        var files = await TopLevel.GetTopLevel(this)!.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select the default background for all profiles",
            AllowMultiple = false,
            FileTypeFilter = [FilePickerFileTypes.ImageAll],
        });

        if (files.Count > 0 && files[0].TryGetLocalPath() is { } path)
        {
            vm.DefaultBackgroundImage = path;
        }
    }
}

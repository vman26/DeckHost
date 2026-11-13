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

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        (DataContext as ProfileManagerViewModel)?.RefreshBackups();
    }

    private async void ImportProfile_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ProfileManagerViewModel vm)
        {
            return;
        }

        var files = await TopLevel.GetTopLevel(this)!.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Import a profile",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Profiles (DeckHost, Stream Deck)") { Patterns = ["*.deckhostprofile", "*.streamDeckProfile"] },
            ],
        });
        if (files.Count > 0 && files[0].TryGetLocalPath() is { } path)
        {
            vm.Import(path);
        }
    }

    private async void ExportProfile_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ProfileManagerViewModel { EditingProfile: { } profile } vm)
        {
            return;
        }

        var file = await TopLevel.GetTopLevel(this)!.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export the profile",
            SuggestedFileName = profile.Name + Core.Profiles.ProfileBundle.Extension,
            DefaultExtension = Core.Profiles.ProfileBundle.Extension,
            FileTypeChoices = [new FilePickerFileType("DeckHost profile") { Patterns = ["*.deckhostprofile"] }],
        });
        if (file?.TryGetLocalPath() is { } path)
        {
            vm.Export(path);
        }
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

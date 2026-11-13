using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using DeckHost.App.ViewModels;

namespace DeckHost.App.Views;

public partial class DeviceSettingsView : UserControl
{
    public DeviceSettingsView()
    {
        InitializeComponent();
    }

    private async void BrowseFile_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: FilePathSettingField field })
        {
            return;
        }

        var files = await TopLevel.GetTopLevel(this)!.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = $"Select {field.Label.ToLowerInvariant()}",
            AllowMultiple = false,
            FileTypeFilter = [FilePickerFileTypes.ImageAll, FilePickerFileTypes.All],
        });

        if (files.Count > 0 && files[0].TryGetLocalPath() is { } path)
        {
            field.Value = path;
        }
    }

    private void Close_OnClick(object? sender, RoutedEventArgs e) => Pages.Close(this);
}

using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using DeckHost.App.ViewModels;

namespace DeckHost.App.Views;

public partial class ControlCustomizationWindow : Window
{
    public ControlCustomizationWindow()
    {
        InitializeComponent();
    }

    private async void BrowseImage_OnClick(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select control image",
            AllowMultiple = false,
            FileTypeFilter = [FilePickerFileTypes.ImageAll],
        });

        if (DataContext is ControlCustomizationViewModel vm && files.Count > 0)
        {
            vm.ImagePath = files[0].TryGetLocalPath() ?? files[0].Name;
        }
    }

    private void Save_OnClick(object? sender, RoutedEventArgs e)
    {
        Close(true);
    }

    private void Cancel_OnClick(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }
}

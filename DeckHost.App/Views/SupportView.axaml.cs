using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using DeckHost.App.ViewModels;

namespace DeckHost.App.Views;

public partial class SupportView : UserControl
{
    public SupportView()
    {
        InitializeComponent();
    }

    private async void SaveBundle_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not SupportViewModel vm || TopLevel.GetTopLevel(this) is not { } top)
        {
            return;
        }

        var file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save support bundle",
            SuggestedFileName = vm.DefaultBundleName,
            DefaultExtension = "zip",
            FileTypeChoices = [new FilePickerFileType("Zip file") { Patterns = ["*.zip"] }],
        });
        if (file?.TryGetLocalPath() is { } path)
        {
            vm.SaveBundle(path);
        }
    }
}

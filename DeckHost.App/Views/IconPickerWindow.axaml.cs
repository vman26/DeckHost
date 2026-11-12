using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using DeckHost.App.ViewModels;
using System.Linq;

namespace DeckHost.App.Views;

/// <summary>Picks an icon; closes with the <see cref="IconChoice"/>, or null when cancelled.</summary>
public partial class IconPickerWindow : Window
{
    public IconPickerWindow()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is IconPickerViewModel viewModel)
            {
                viewModel.Picked += (_, choice) => Close(choice);
            }
        };
    }

    private void Cancel_OnClick(object? sender, RoutedEventArgs e) => Close(null);

    private void Results_OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is IconPickerViewModel { HasSelection: true } viewModel)
        {
            var command = viewModel.SuggestPicture ? viewModel.UseAsPictureCommand : viewModel.UseAsIconCommand;
            command.Execute(null);
        }
    }

    private async void InstallPack_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not IconPickerViewModel viewModel)
        {
            return;
        }

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Install an icon pack",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Icon packs") { Patterns = ["*.streamDeckIconPack", "*.zip"] },
                new FilePickerFileType("All files") { Patterns = ["*"] },
            ],
        });

        if (files.FirstOrDefault()?.TryGetLocalPath() is { } path)
        {
            await viewModel.InstallAsync(path);
        }
    }
}

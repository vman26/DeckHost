using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using DeckHost.App.Platform;
using DeckHost.App.ViewModels;

namespace DeckHost.App.Views;

public partial class ControlCustomizationWindow : Window
{
    private HotkeySettingField? _recordingField;

    public ControlCustomizationWindow()
    {
        InitializeComponent();

        // Tunnel so Tab, Enter or Esc reach the recorder before focus navigation or dialog keys.
        AddHandler(KeyDownEvent, OnRecorderKeyDown, RoutingStrategies.Tunnel);
        AddHandler(KeyUpEvent, OnRecorderKeyUp, RoutingStrategies.Tunnel);

        if (!WebView2Host.IsSupported)
        {
            // WebView2 is Windows-only; elsewhere the plugin's page opens in the default browser.
            SettingsBrowser.IsVisible = false;
            SettingsBrowserError.Text = "Plugin settings pages open in your web browser on this system. Use Open in browser above; changes there are saved straight to the plugin.";
            SettingsBrowserError.Foreground = Avalonia.Media.Brushes.LightSteelBlue;
            SettingsBrowserError.IsVisible = true;
        }

        SettingsBrowser.Failed += message => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            SettingsBrowser.IsVisible = false;
            SettingsBrowserError.Text = $"The built-in browser couldn't start ({message}). Use Open in browser instead.";
            SettingsBrowserError.IsVisible = true;
        });

        DataContextChanged += (_, _) =>
        {
            if (DataContext is ControlCustomizationViewModel { ShowSettingsPage: true } && WebView2Host.IsSupported)
            {
                // Room for the plugin's settings page next to the editor.
                Width = 1120;
            }
        };
    }

    private void HotkeyRecord_OnIsCheckedChanged(object? sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton { DataContext: HotkeySettingField field } button)
        {
            return;
        }

        if (button.IsChecked == true)
        {
            if (_recordingField is not null && !ReferenceEquals(_recordingField, field))
            {
                _recordingField.IsRecording = false;
            }

            _recordingField = field;
            button.Focus();
        }
        else if (ReferenceEquals(_recordingField, field))
        {
            _recordingField = null;
        }
    }

    private void OnRecorderKeyDown(object? sender, KeyEventArgs e)
    {
        if (_recordingField is not { } field)
        {
            return;
        }

        e.Handled = true;

        if (e.Key == Key.Escape && e.KeyModifiers == KeyModifiers.None)
        {
            StopRecording();
            return;
        }

        if (HotkeyRecorder.IsModifier(e.Key))
        {
            field.RecordingPreview = HotkeyRecorder.ModifierPrefix(e.KeyModifiers) + "…";
            return;
        }

        // Windows sends Print Screen only on key-up; that is handled there.
        if (e.Key == Key.PrintScreen)
        {
            return;
        }

        Complete(field, e.Key, e.KeyModifiers);
    }

    private void OnRecorderKeyUp(object? sender, KeyEventArgs e)
    {
        if (_recordingField is not { } field)
        {
            return;
        }

        e.Handled = true;
        if (e.Key == Key.PrintScreen)
        {
            Complete(field, e.Key, e.KeyModifiers);
        }
        else if (HotkeyRecorder.IsModifier(e.Key))
        {
            var prefix = HotkeyRecorder.ModifierPrefix(e.KeyModifiers);
            field.RecordingPreview = prefix.Length == 0 ? string.Empty : prefix + "…";
        }
    }

    private void Complete(HotkeySettingField field, Key key, KeyModifiers modifiers)
    {
        if (HotkeyRecorder.Describe(key, modifiers) is { } hotkey)
        {
            field.Value = hotkey;
            StopRecording();
        }
        else
        {
            field.RecordingPreview = $"{key} can't be used; try another key.";
        }
    }

    private void StopRecording()
    {
        if (_recordingField is { } field)
        {
            _recordingField = null;
            field.IsRecording = false;
        }
    }

    private async void ChooseIcon_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ControlCustomizationViewModel viewModel ||
            CommunityToolkit.Mvvm.DependencyInjection.Ioc.Default.GetService(typeof(DeckHost.Core.Icons.IconLibrary)) is not DeckHost.Core.Icons.IconLibrary library)
        {
            return;
        }

        var iconify = CommunityToolkit.Mvvm.DependencyInjection.Ioc.Default.GetService(typeof(DeckHost.Core.Icons.IconifyClient)) as DeckHost.Core.Icons.IconifyClient;
        var picker = new IconPickerWindow { DataContext = new IconPickerViewModel(library, iconify) };
        if (await picker.ShowDialog<IconChoice?>(this) is { } choice)
        {
            if (choice.AsPicture)
            {
                viewModel.ImagePath = choice.Path;
            }
            else
            {
                viewModel.IconPath = choice.Path;
            }
        }
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

    private async void BrowseSettingFile_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: FilePathSettingField field })
        {
            return;
        }

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = $"Select {field.Label.ToLowerInvariant()}",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Programs and shortcuts") { Patterns = ["*.exe", "*.lnk", "*.bat", "*.cmd", "*.ps1", "*.url"] },
                FilePickerFileTypes.All,
            ],
        });

        if (files.Count > 0 && files[0].TryGetLocalPath() is { } path)
        {
            field.Value = path;
        }
    }

    private void FontBox_OnGotFocus(object? sender, RoutedEventArgs e) => FontBox.IsDropDownOpen = true;

    /// <summary>Opens the whole font list (scroll), while typing in the box still filters it.</summary>
    private void FontList_OnClick(object? sender, RoutedEventArgs e)
    {
        if (FontBox.IsDropDownOpen)
        {
            FontBox.IsDropDownOpen = false;
            return;
        }

        FontBox.Focus();
        FontBox.IsDropDownOpen = true;
    }

    private void Save_OnClick(object? sender, RoutedEventArgs e) => Close(true);

    private void Cancel_OnClick(object? sender, RoutedEventArgs e) => Close(false);
}

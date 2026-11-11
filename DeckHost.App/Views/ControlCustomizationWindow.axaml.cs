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

    private void Save_OnClick(object? sender, RoutedEventArgs e) => Close(true);

    private void Cancel_OnClick(object? sender, RoutedEventArgs e) => Close(false);
}

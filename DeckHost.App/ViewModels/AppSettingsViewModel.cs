using System;
using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.Input;
using DeckHost.App.Platform;

namespace DeckHost.App.ViewModels;

/// <summary>The DeckHost settings window: start-up and tray behavior. Changes apply right away.</summary>
public sealed partial class AppSettingsViewModel : ViewModelBase
{
    private bool _startWithWindows;
    private bool _startMinimized;
    private bool _closeToTray;
    private string _status = string.Empty;

    public AppSettingsViewModel()
    {
        var settings = AppSettings.Load();
        _startMinimized = settings.StartMinimized;
        _closeToTray = settings.CloseToTray;
        _startWithWindows = AppSettings.IsStartWithWindowsEnabled();
    }

    /// <summary>Raised after a change, so the app can update its tray and close behavior.</summary>
    public static event Action<AppSettingsData>? Changed;

    public bool CanStartWithWindows => AppSettings.CanStartWithWindows;

    public bool StartWithWindows
    {
        get => _startWithWindows;
        set
        {
            if (!SetProperty(ref _startWithWindows, value))
            {
                return;
            }

            try
            {
                AppSettings.SetStartWithWindows(value);
                Status = value ? "DeckHost starts when you sign in to Windows." : "DeckHost no longer starts with Windows.";
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
            {
                Status = $"Could not change the Windows start-up entry: {ex.Message}";
            }
        }
    }

    public bool StartMinimized
    {
        get => _startMinimized;
        set
        {
            if (SetProperty(ref _startMinimized, value))
            {
                Save();
            }
        }
    }

    public bool CloseToTray
    {
        get => _closeToTray;
        set
        {
            if (SetProperty(ref _closeToTray, value))
            {
                Save();
            }
        }
    }

    public string Status
    {
        get => _status;
        private set
        {
            if (SetProperty(ref _status, value))
            {
                OnPropertyChanged(nameof(HasStatus));
            }
        }
    }

    public bool HasStatus => Status.Length > 0;

    public string DataDirectory { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DeckHost");

    public string LogDirectory => Logging.LogDirectory;

    [RelayCommand]
    private void OpenDataFolder() => Open(DataDirectory);

    [RelayCommand]
    private void OpenLogFolder() => Open(LogDirectory);

    private static void Open(string directory)
    {
        Directory.CreateDirectory(directory);
        Process.Start(new ProcessStartInfo(directory) { UseShellExecute = true })?.Dispose();
    }

    private void Save()
    {
        var settings = new AppSettingsData(StartMinimized, CloseToTray);
        try
        {
            AppSettings.Save(settings);
            Status = "Saved.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Status = $"Could not save the settings: {ex.Message}";
        }

        Changed?.Invoke(settings);
    }
}

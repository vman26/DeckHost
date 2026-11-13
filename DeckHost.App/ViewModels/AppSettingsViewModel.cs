using System;
using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.Input;
using DeckHost.App.Platform;

namespace DeckHost.App.ViewModels;

/// <summary>The DeckHost settings window: start-up and tray behavior. Changes apply right away.</summary>
public sealed partial class AppSettingsViewModel : ViewModelBase
{
    private bool _startAtLogin;
    private bool _startMinimized;
    private bool _closeToTray;
    private bool _showDebugPanel;
    private string _status = string.Empty;

    public AppSettingsViewModel(UpdateViewModel? updates = null)
    {
        Updates = updates;
        var settings = AppSettings.Load();
        _startMinimized = settings.StartMinimized;
        _closeToTray = settings.KeepsRunningInTray;
        _startAtLogin = AppSettings.IsStartAtLoginEnabled();
        _showDebugPanel = settings.ShowDebugPanel;
    }

    /// <summary>Update checks and the available update (null where there is no update service, e.g. in tests).</summary>
    public UpdateViewModel? Updates { get; }

    public bool HasUpdates => Updates is not null;

    /// <summary>Raised after a change, so the app can update its tray and close behavior.</summary>
    public static event Action<AppSettingsData>? Changed;

    public bool CanStartAtLogin => AppSettings.CanStartAtLogin;

    public bool StartAtLogin
    {
        get => _startAtLogin;
        set
        {
            if (!SetProperty(ref _startAtLogin, value))
            {
                return;
            }

            try
            {
                AppSettings.SetStartAtLogin(value);
                Status = value ? "DeckHost starts when you sign in." : "DeckHost no longer starts when you sign in.";
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
            {
                Status = $"Could not change the start-up entry: {ex.Message}";
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

    /// <summary>The debug panel next to the editor: the selected control's capabilities, the runtime connection and log.</summary>
    public bool ShowDebugPanel
    {
        get => _showDebugPanel;
        set
        {
            if (SetProperty(ref _showDebugPanel, value))
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

    public string VersionText { get; } = $"DeckHost {AppInfo.Version} on {System.Runtime.InteropServices.RuntimeInformation.OSDescription}";

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
        // The update fields are the update view model's; keep what's stored.
        var settings = AppSettings.Load() with { StartMinimized = StartMinimized, CloseToTray = CloseToTray, ShowDebugPanel = ShowDebugPanel };
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

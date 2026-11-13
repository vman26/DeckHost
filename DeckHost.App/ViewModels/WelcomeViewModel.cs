using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.Input;
using DeckHost.App.Platform;
using DeckHost.Core.Interfaces;

namespace DeckHost.App.ViewModels;

/// <summary>
/// The welcome tour, shown on the first start (and from the menu): the devices DeckHost found and
/// how to connect one, a starter layout, the main preferences, and how the editor works.
/// </summary>
public sealed partial class WelcomeViewModel : ViewModelBase
{
    public const int StepCount = 4;

    private readonly MainViewModel? _main;
    private readonly IDeviceHostService? _host;
    private readonly UpdateViewModel? _updates;
    private int _step;
    private bool _starterApplied;
    private string _starterStatus = string.Empty;
    private bool _startAtLogin;
    private bool _offerCrashReports;

    public WelcomeViewModel(MainViewModel? main = null, IDeviceHostService? host = null, UpdateViewModel? updates = null)
    {
        _main = main;
        _host = host;
        _updates = updates;
        _startAtLogin = AppSettings.CanStartAtLogin && AppSettings.IsStartAtLoginEnabled();
        _offerCrashReports = AppSettings.Load().OfferCrashReports;
        RefreshDevices();
    }

    /// <summary>Raised when the tour is finished or skipped (the page closes).</summary>
    public event EventHandler? Finished;

    public int Step
    {
        get => _step;
        private set
        {
            if (SetProperty(ref _step, Math.Clamp(value, 0, StepCount - 1)))
            {
                foreach (var name in new[] { nameof(IsDevicesStep), nameof(IsStartStep), nameof(IsPreferencesStep), nameof(IsTipsStep), nameof(CanGoBack), nameof(NextLabel), nameof(StepText) })
                {
                    OnPropertyChanged(name);
                }
            }
        }
    }

    public bool IsDevicesStep => Step == 0;

    public bool IsStartStep => Step == 1;

    public bool IsPreferencesStep => Step == 2;

    public bool IsTipsStep => Step == 3;

    public bool CanGoBack => Step > 0;

    public string NextLabel => Step == StepCount - 1 ? "Start using DeckHost" : "Next";

    public string StepText => $"Step {Step + 1} of {StepCount}";

    // ---------------------------------------------------------------- devices

    public IReadOnlyList<WelcomeDevice> Devices { get; private set; } = [];

    public bool HasConnectedDevice => Devices.Any(static device => device.IsConnected && !device.IsVirtual);

    public bool ShowNoDeviceHelp => !HasConnectedDevice;

    /// <summary>What to do when the device isn't found, for this system.</summary>
    public string DeviceHelp => OperatingSystem.IsLinux()
        ? "On Linux, run ./install.sh from the download once: it adds the device rule that lets DeckHost open the device without root. Then unplug it and plug it in again."
        : OperatingSystem.IsMacOS()
            ? "Plug the device in directly (not through a monitor's hub, if it doesn't show up). Close the Elgato Stream Deck app first: only one program can drive a Stream Deck."
            : "Plug the device in directly. Close the Elgato Stream Deck app or the MiraBox app first: only one program can drive the device at a time.";

    [RelayCommand]
    private void RefreshDevices()
    {
        Devices = [.. (_host?.Devices ?? []).Select(static device => new WelcomeDevice(
            device.Name,
            device.IsConnected,
            device.Id.StartsWith("web.", StringComparison.Ordinal)))];
        OnPropertyChanged(nameof(Devices));
        OnPropertyChanged(nameof(HasConnectedDevice));
        OnPropertyChanged(nameof(ShowNoDeviceHelp));
    }

    // ---------------------------------------------------------------- start

    public bool CanApplyStarter => _main is not null && !_starterApplied;

    public string StarterStatus
    {
        get => _starterStatus;
        private set => SetProperty(ref _starterStatus, value);
    }

    /// <summary>Puts everyday actions on the edited profile's empty controls.</summary>
    [RelayCommand]
    private void ApplyStarter()
    {
        if (_main is null || _starterApplied)
        {
            return;
        }

        var filled = _main.ApplyStarterLayout();
        _starterApplied = true;
        StarterStatus = filled == 0
            ? "Every control already has an action, so nothing changed."
            : $"Done: {filled} controls now have an action (media keys, volume, clock, system info, lock). Change any of them later.";
        OnPropertyChanged(nameof(CanApplyStarter));
    }

    // ---------------------------------------------------------------- preferences

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
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or System.IO.IOException)
            {
                StarterStatus = $"Could not change the start-up entry: {ex.Message}";
            }
        }
    }

    public bool HasUpdates => _updates is not null;

    public bool CheckForUpdates
    {
        get => _updates?.CheckAutomatically ?? AppSettings.Load().CheckForUpdates;
        set
        {
            if (_updates is not null)
            {
                _updates.CheckAutomatically = value;
            }
            else
            {
                AppSettings.Save(AppSettings.Load() with { CheckForUpdates = value });
            }

            OnPropertyChanged();
        }
    }

    public bool OfferCrashReports
    {
        get => _offerCrashReports;
        set
        {
            if (SetProperty(ref _offerCrashReports, value))
            {
                AppSettings.Save(AppSettings.Load() with { OfferCrashReports = value });
            }
        }
    }

    // ---------------------------------------------------------------- navigation

    [RelayCommand]
    private void Next()
    {
        if (Step == StepCount - 1)
        {
            Finish();
            return;
        }

        if (Step == 0)
        {
            RefreshDevices();
        }

        Step++;
    }

    [RelayCommand]
    private void Back() => Step--;

    [RelayCommand]
    private void Finish()
    {
        AppSettings.Save(AppSettings.Load() with { WelcomeShown = true });
        Finished?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Whether the tour should open by itself (the first start).</summary>
    public static bool ShouldShow() => !AppSettings.Load().WelcomeShown;
}

/// <summary>A device on the welcome page.</summary>
public sealed record WelcomeDevice(string Name, bool IsConnected, bool IsVirtual)
{
    public string State => IsVirtual ? (IsConnected ? "on" : "off: turn it on in Device settings") : IsConnected ? "connected" : "not connected";

    public string Dot => IsConnected ? "●" : "○";

    public string DotColor => IsConnected ? "#22C55E" : "#64748B";
}

using System;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using DeckHost.Contracts.Models;
using DeckHost.Core.Firmware;

namespace DeckHost.App.ViewModels;

/// <summary>
/// The Firmware card in Device settings: the version the device runs and, when its plugin can install
/// firmware, checking for an update or installing a file. Installing asks once more (it can't be
/// undone from here, and the device must stay plugged in) and shows the plugin's progress.
/// </summary>
public sealed partial class FirmwareViewModel : ViewModelBase
{
    private readonly FirmwareService _service;
    private readonly DeviceDefinition _device;
    private DeviceFirmwareInfo? _info;
    private FirmwareRelease? _release;
    private FirmwareImage? _pending;
    private string _status = string.Empty;
    private double _progress;
    private bool _isBusy;
    private bool _statusIsError;

    public FirmwareViewModel(FirmwareService service, DeviceDefinition device)
    {
        _service = service;
        _device = device;
        _info = service.GetInfo(device.Id);
    }

    public string Version => _info?.Version ?? (_device.IsConnected ? "Not reported" : "Plug the device in to read it");

    public string Detail => _info?.Detail ?? string.Empty;

    public bool HasDetail => Detail.Length > 0;

    public bool CanUpdate => _info?.CanUpdate == true && _device.IsConnected;

    /// <summary>The plugin installs from a file the user picks (with these extensions).</summary>
    public bool CanInstallFile => CanUpdate && _info!.FileExtensions.Count > 0;

    public System.Collections.Generic.IReadOnlyList<string> FileExtensions => _info?.FileExtensions ?? [];

    public FirmwareRelease? Release
    {
        get => _release;
        private set
        {
            if (SetProperty(ref _release, value))
            {
                OnPropertyChanged(nameof(HasRelease));
                OnPropertyChanged(nameof(ReleaseText));
            }
        }
    }

    public bool HasRelease => _release is not null;

    public string ReleaseText => _release is { } release ? $"Version {release.Version} is available.{(string.IsNullOrWhiteSpace(release.Notes) ? string.Empty : " " + release.Notes)}" : string.Empty;

    /// <summary>An install waits for the second click.</summary>
    public bool IsConfirming => _pending is not null || (_confirmRelease && _release is not null);

    private bool _confirmRelease;

    public string ConfirmText => $"Install {(_pending is { } image ? image.Version ?? System.IO.Path.GetFileName(image.Path) : _release?.Version)} on {_device.Name}? Keep it plugged in and DeckHost open until it's done; the device won't respond meanwhile.";

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(IsIdle));
            }
        }
    }

    public bool IsIdle => !_isBusy;

    public double Progress
    {
        get => _progress;
        private set => SetProperty(ref _progress, value);
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

    public bool HasStatus => _status.Length > 0;

    public bool StatusIsError
    {
        get => _statusIsError;
        private set => SetProperty(ref _statusIsError, value);
    }

    [RelayCommand]
    private async Task CheckAsync()
    {
        IsBusy = true;
        StatusIsError = false;
        Status = "Checking…";
        try
        {
            Release = await _service.CheckAsync(_device.Id, CancellationToken.None);
            Status = Release is null ? $"{_device.Name} has the newest firmware its plugin knows of." : string.Empty;
        }
        catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or TaskCanceledException or System.IO.IOException)
        {
            StatusIsError = true;
            Status = $"Couldn't check: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>The first click on Install (the available release).</summary>
    [RelayCommand]
    private void InstallRelease()
    {
        _pending = null;
        _confirmRelease = true;
        RaiseConfirm();
    }

    /// <summary>A file the user picked: asks before installing it.</summary>
    public void ChooseFile(string path)
    {
        _confirmRelease = false;
        _pending = new FirmwareImage(path);
        RaiseConfirm();
    }

    [RelayCommand]
    private void CancelInstall()
    {
        _pending = null;
        _confirmRelease = false;
        RaiseConfirm();
    }

    /// <summary>The second click: downloads (and checks) the release if needed, then installs.</summary>
    [RelayCommand]
    public async Task ConfirmInstallAsync()
    {
        var file = _pending;
        var release = _confirmRelease ? _release : null;
        _pending = null;
        _confirmRelease = false;
        RaiseConfirm();
        if (file is null && release is null)
        {
            return;
        }

        IsBusy = true;
        StatusIsError = false;
        Progress = 0;
        var progress = new Progress<FirmwareProgress>(step =>
        {
            Progress = Math.Clamp(step.Fraction, 0, 1);
            Status = step.Step;
        });
        try
        {
            var image = file ?? await _service.DownloadAsync(release!, progress);
            var result = await Task.Run(() => _service.InstallAsync(_device.Id, image, progress));
            StatusIsError = !result.Success;
            Status = result.Message;
            if (result.Success)
            {
                Progress = 1;
                Release = null;
                _info = _service.GetInfo(_device.Id) ?? _info;
                OnPropertyChanged(nameof(Version));
            }
        }
        catch (FirmwareException ex)
        {
            StatusIsError = true;
            Status = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void RaiseConfirm()
    {
        OnPropertyChanged(nameof(IsConfirming));
        OnPropertyChanged(nameof(ConfirmText));
    }
}

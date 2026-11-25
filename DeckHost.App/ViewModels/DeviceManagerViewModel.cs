using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using DeckHost.Core.Devices;
using DeckHost.Core.Interfaces;

namespace DeckHost.App.ViewModels;

/// <summary>
/// The Devices page: the devices in DeckHost's list (plugged in or not), each with what it is, what it can do, its firmware,
/// its settings and Remove; the devices that were found and wait to be added; and the ones that were removed or refused.
/// Adding new devices by themselves can be turned off here.
/// </summary>
public sealed partial class DeviceManagerViewModel : ViewModelBase, IDisposable
{
    private readonly IDeviceHostService _host;
    private readonly DeviceSettingsService? _deviceSettings;
    private bool _autoAdd;

    /// <param name="deviceSettings">Opens a device's settings and reads its firmware; null in tests (no Settings… buttons).</param>
    public DeviceManagerViewModel(IDeviceHostService host, DeviceSettingsService? deviceSettings = null)
    {
        _host = host;
        _deviceSettings = deviceSettings;
        _autoAdd = host.AutoAddDevices;
        Resume();
    }

    private bool _listening;

    /// <summary>
    /// Follows the host again and rereads the list: the page is shown again after a page on top of it (a device's
    /// settings) closed, which took it out of the window and so <see cref="Dispose"/>d it.
    /// </summary>
    public void Resume()
    {
        if (!_listening)
        {
            _listening = true;
            _host.RosterChanged += OnRosterChanged;
            _host.DevicesChanged += OnDevicesChanged;
        }

        Refresh();
    }

    /// <summary>The devices in the list.</summary>
    public ObservableCollection<DeviceRowViewModel> Listed { get; } = [];

    /// <summary>Found, waiting for Add or Keep ignoring.</summary>
    public ObservableCollection<DeviceRowViewModel> Discovered { get; } = [];

    /// <summary>Removed or refused; offered again when plugged in again.</summary>
    public ObservableCollection<DeviceRowViewModel> Ignored { get; } = [];

    public bool HasDiscovered => Discovered.Count > 0;

    public bool HasIgnored => Ignored.Count > 0;

    /// <summary>Add a device by itself when it is plugged in (otherwise it waits under Found).</summary>
    public bool AutoAdd
    {
        get => _autoAdd;
        set
        {
            if (SetProperty(ref _autoAdd, value))
            {
                _host.SetAutoAddDevices(value);
            }
        }
    }

    /// <summary>Reads the list from the host again.</summary>
    public void Refresh()
    {
        var entries = _host.DeviceRoster();
        var listedCount = entries.Count(static entry => entry.Listing == DeviceListing.Listed);
        _cards = _host.Devices.ToDictionary(static device => device.Id, CreateCard, StringComparer.OrdinalIgnoreCase);
        Fill(Listed, entries.Where(static entry => entry.Listing == DeviceListing.Listed), listedCount);
        Fill(Discovered, entries.Where(static entry => entry.Listing == DeviceListing.Discovered), listedCount);
        Fill(Ignored, entries.Where(static entry => entry.Listing == DeviceListing.Ignored), listedCount);
        OnPropertyChanged(nameof(HasDiscovered));
        OnPropertyChanged(nameof(HasIgnored));
    }

    private void Fill(ObservableCollection<DeviceRowViewModel> rows, System.Collections.Generic.IEnumerable<DeviceRosterEntry> entries, int listedCount)
    {
        var wanted = entries.Select(entry => new DeviceRowViewModel(entry, this, canRemove: listedCount > 1, _cards.GetValueOrDefault(entry.Id))).ToList();
        if (rows.Select(static row => row.Entry).SequenceEqual(wanted.Select(static row => row.Entry)) &&
            rows.Select(static row => row.CanRemove).SequenceEqual(wanted.Select(static row => row.CanRemove)) &&
            rows.Select(static row => row.Card?.Device).SequenceEqual(wanted.Select(static row => row.Card?.Device)) &&
            rows.Select(static row => row.Card?.FirmwareText).SequenceEqual(wanted.Select(static row => row.Card?.FirmwareText)))
        {
            return;
        }

        rows.Clear();
        foreach (var row in wanted)
        {
            rows.Add(row);
        }
    }

    private System.Collections.Generic.Dictionary<string, DeviceInfoViewModel> _cards = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>What a listed device is and can do (the host only knows the listed ones).</summary>
    private DeviceInfoViewModel CreateCard(Contracts.Models.DeviceDefinition device) => new(
        device,
        _host.ConnectionStatuses.FirstOrDefault(status => status.DeviceId == device.Id)?.Message,
        _deviceSettings?.HasSettings(device.Id) == true,
        _deviceSettings?.HasFirmware(device.Id) == true,
        _deviceSettings?.FirmwareInfo(device.Id));

    /// <summary>The device's settings page (brightness, lights, idle screen, pairing, firmware, …), when it has one.</summary>
    public DeviceSettingsViewModel? CreateDeviceSettings(DeviceRowViewModel row) =>
        row.Card is { HasSettings: true } card ? _deviceSettings?.Create(card.Device) : null;

    internal void Remove(DeviceRowViewModel row) => _host.RemoveDevice(row.Id);

    internal void Add(DeviceRowViewModel row) => _host.AddDevice(row.Id);

    internal void KeepIgnoring(DeviceRowViewModel row) => _host.IgnoreDevice(row.Id);

    private void OnRosterChanged() => Dispatcher.UIThread.Post(Refresh);

    // Connected/unplugged, a firmware version read, a renamed Web Deck: the cards follow.
    private void OnDevicesChanged() => Dispatcher.UIThread.Post(Refresh);

    public void Dispose()
    {
        if (!_listening)
        {
            return;
        }

        _listening = false;
        _host.RosterChanged -= OnRosterChanged;
        _host.DevicesChanged -= OnDevicesChanged;
    }
}

/// <summary>One device on the Devices page.</summary>
public sealed partial class DeviceRowViewModel(DeviceRosterEntry entry, DeviceManagerViewModel owner, bool canRemove, DeviceInfoViewModel? card = null) : ViewModelBase
{
    public DeviceRosterEntry Entry { get; } = entry;

    /// <summary>What the device is and can do, its firmware and whether it has settings; null for a device that isn't listed.</summary>
    public DeviceInfoViewModel? Card { get; } = card;

    public bool HasCard => Card is not null;

    public bool HasSettings => Card?.HasSettings == true;

    /// <summary>"Connected" / "Not connected".</summary>
    public string State => Entry.IsConnected ? "Connected" : "Not connected";

    /// <summary>The device plugin that connects it ("MiraBox StreamDock", "Web Deck").</summary>
    public string PluginName => Entry.PluginName;

    public string Id => Entry.Id;

    public string Name => Entry.Name;

    public string Detail => $"{Entry.PluginName} · {(Entry.IsConnected ? "plugged in" : "not plugged in")}";

    public bool IsConnected => Entry.IsConnected;

    /// <summary>DeckHost keeps at least one device in the list, so the last one can't be removed.</summary>
    public bool CanRemove { get; } = canRemove;

    public string RemoveTip => CanRemove
        ? "Take it out of the list. Its profiles are kept for when it is added again."
        : "DeckHost always keeps one device in the list.";

    [RelayCommand]
    private void Remove() => owner.Remove(this);

    [RelayCommand]
    private void Add() => owner.Add(this);

    [RelayCommand]
    private void KeepIgnoring() => owner.KeepIgnoring(this);
}

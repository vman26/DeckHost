using System;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using DeckHost.Core.Devices;
using DeckHost.Core.Interfaces;

namespace DeckHost.App.ViewModels;

/// <summary>
/// The Devices page: the devices in DeckHost's list (plugged in or not) that can be removed, the devices that were found and
/// wait to be added, and the ones that were removed or refused. Adding new devices by themselves can be turned off here.
/// </summary>
public sealed partial class DeviceManagerViewModel : ViewModelBase, IDisposable
{
    private readonly IDeviceHostService _host;
    private bool _autoAdd;

    public DeviceManagerViewModel(IDeviceHostService host)
    {
        _host = host;
        _autoAdd = host.AutoAddDevices;
        _host.RosterChanged += OnRosterChanged;
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
        Fill(Listed, entries.Where(static entry => entry.Listing == DeviceListing.Listed), listedCount);
        Fill(Discovered, entries.Where(static entry => entry.Listing == DeviceListing.Discovered), listedCount);
        Fill(Ignored, entries.Where(static entry => entry.Listing == DeviceListing.Ignored), listedCount);
        OnPropertyChanged(nameof(HasDiscovered));
        OnPropertyChanged(nameof(HasIgnored));
    }

    private void Fill(ObservableCollection<DeviceRowViewModel> rows, System.Collections.Generic.IEnumerable<DeviceRosterEntry> entries, int listedCount)
    {
        var wanted = entries.Select(entry => new DeviceRowViewModel(entry, this, canRemove: listedCount > 1)).ToList();
        if (rows.Select(static row => row.Entry).SequenceEqual(wanted.Select(static row => row.Entry)) &&
            rows.Select(static row => row.CanRemove).SequenceEqual(wanted.Select(static row => row.CanRemove)))
        {
            return;
        }

        rows.Clear();
        foreach (var row in wanted)
        {
            rows.Add(row);
        }
    }

    internal void Remove(DeviceRowViewModel row) => _host.RemoveDevice(row.Id);

    internal void Add(DeviceRowViewModel row) => _host.AddDevice(row.Id);

    internal void KeepIgnoring(DeviceRowViewModel row) => _host.IgnoreDevice(row.Id);

    private void OnRosterChanged() => Dispatcher.UIThread.Post(Refresh);

    public void Dispose() => _host.RosterChanged -= OnRosterChanged;
}

/// <summary>One device on the Devices page.</summary>
public sealed partial class DeviceRowViewModel(DeviceRosterEntry entry, DeviceManagerViewModel owner, bool canRemove) : ViewModelBase
{
    public DeviceRosterEntry Entry { get; } = entry;

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

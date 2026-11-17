using System.Text.Json;

namespace DeckHost.Core.Devices;

/// <summary>Where a device the plugins report stands with the user.</summary>
public enum DeviceListing
{
    /// <summary>In the device list: it can be edited, drawn on and used.</summary>
    Listed,

    /// <summary>Plugged in but not added yet: waits for Add or Keep ignoring.</summary>
    Discovered,

    /// <summary>Removed, or refused: stays out of the list until it is plugged in again (then it is Discovered).</summary>
    Ignored,
}

/// <summary>A device as the Devices page shows it.</summary>
public sealed record DeviceRosterEntry(string Id, string Name, string PluginName, bool IsConnected, DeviceListing Listing);

/// <summary>
/// Which of the devices the plugins report are in DeckHost's device list. A device nobody decided about is added
/// by itself while <see cref="AutoAdd"/> is on (the default), else it waits as <see cref="DeviceListing.Discovered"/>.
/// A removed device is <see cref="DeviceListing.Ignored"/>: it stays out until it is unplugged and plugged in again,
/// when it is offered again as discovered. The decisions are kept in a small JSON file.
/// </summary>
public sealed class DeviceRoster
{
    private readonly string? _file;
    private readonly HashSet<string> _listed = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _ignored = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _discovered = new(StringComparer.OrdinalIgnoreCase);

    // Ignored while plugged in: it only counts as "plugged in again" after it has been seen unplugged.
    private readonly HashSet<string> _ignoredWhilePluggedIn = new(StringComparer.OrdinalIgnoreCase);
    private bool _autoAdd = true;

    /// <param name="file">Where the decisions are kept; null keeps them in memory only.</param>
    public DeviceRoster(string? file = null)
    {
        _file = file;
        Load();
    }

    /// <summary>Add devices by themselves when they are plugged in (on by default).</summary>
    public bool AutoAdd
    {
        get => _autoAdd;
        set
        {
            if (_autoAdd != value)
            {
                _autoAdd = value;
                Save();
            }
        }
    }

    /// <summary>Where the device stands now; <paramref name="isConnected"/> is whether it is plugged in at the moment.</summary>
    public DeviceListing Decide(string deviceId, bool isConnected)
    {
        if (_ignored.Contains(deviceId))
        {
            if (!isConnected)
            {
                _ignoredWhilePluggedIn.Remove(deviceId);
                return DeviceListing.Ignored;
            }

            if (_ignoredWhilePluggedIn.Contains(deviceId))
            {
                return DeviceListing.Ignored;
            }

            // Plugged in again after being removed: offer it again.
            _ignored.Remove(deviceId);
            _discovered.Add(deviceId);
            Save();
            return DeviceListing.Discovered;
        }

        if (_listed.Contains(deviceId))
        {
            return DeviceListing.Listed;
        }

        if (_discovered.Contains(deviceId))
        {
            return DeviceListing.Discovered;
        }

        // Not seen before. An unplugged one comes from an earlier run (its profiles exist): it stays in the list.
        if (_autoAdd || !isConnected)
        {
            _listed.Add(deviceId);
            Save();
            return DeviceListing.Listed;
        }

        _discovered.Add(deviceId);
        return DeviceListing.Discovered;
    }

    /// <summary>Puts a discovered or ignored device in the list.</summary>
    public void Add(string deviceId)
    {
        _discovered.Remove(deviceId);
        _ignored.Remove(deviceId);
        _ignoredWhilePluggedIn.Remove(deviceId);
        _listed.Add(deviceId);
        Save();
    }

    /// <summary>Takes a listed device out of the list. A plugged-in one is offered again after it is unplugged and plugged in.</summary>
    public void Remove(string deviceId, bool isConnected) => Ignore(deviceId, isConnected);

    /// <summary>Refuses a discovered device (Keep ignoring); like <see cref="Remove"/> it is offered again when plugged in again.</summary>
    public void Ignore(string deviceId, bool isConnected)
    {
        _listed.Remove(deviceId);
        _discovered.Remove(deviceId);
        _ignored.Add(deviceId);
        if (isConnected)
        {
            _ignoredWhilePluggedIn.Add(deviceId);
        }
        else
        {
            _ignoredWhilePluggedIn.Remove(deviceId);
        }

        Save();
    }

    private sealed record Stored(bool AutoAdd, string[] Listed, string[] Ignored);

    private void Load()
    {
        if (_file is null || !File.Exists(_file))
        {
            return;
        }

        try
        {
            var stored = JsonSerializer.Deserialize<Stored>(File.ReadAllText(_file));
            if (stored is null)
            {
                return;
            }

            _autoAdd = stored.AutoAdd;
            _listed.UnionWith(stored.Listed ?? []);
            _ignored.UnionWith(stored.Ignored ?? []);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // An unreadable file: start over (everything plugged in is added again).
        }
    }

    private void Save()
    {
        if (_file is null)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
            File.WriteAllText(_file, JsonSerializer.Serialize(new Stored(_autoAdd, [.. _listed.Order()], [.. _ignored.Order()])));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Not saved: the choices are still kept until DeckHost closes.
        }
    }
}

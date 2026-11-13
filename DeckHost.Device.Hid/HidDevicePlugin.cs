using System.Collections.Concurrent;
using DeckHost.Contracts.Enums;
using DeckHost.Contracts.Interfaces;
using DeckHost.Contracts.Models;
using HidSharp;

namespace DeckHost.Device.Hid;

/// <summary>
/// Generic USB HID devices the user described in the device creator (<see cref="HidDeviceDescription"/>
/// files in <c>devices/hid</c>). Each description is a device in DeckHost, listed while unplugged;
/// when its hardware is plugged in it is opened and its reports become key presses and knob turns.
/// These devices only send input: they have no screens or lights DeckHost could drive.
/// </summary>
public sealed class HidDevicePlugin : IDevicePlugin, IDeviceOutput
{
    private static readonly TimeSpan LongPressDelay = TimeSpan.FromMilliseconds(500);

    private readonly Lock _sync = new();
    private readonly Dictionary<string, Session> _sessions = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<(string DeviceId, string ControlId), Timer> _longPressTimers = new();
    private IReadOnlyCollection<DeviceDefinition> _devices = [];
    private IDevicePluginContext? _context;
    private bool _disposed;

    public HidDevicePlugin(string? directory = null)
    {
        Directory = directory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DeckHost", "devices", "hid");
        LoadDescriptions();
    }

    public event EventHandler? DevicesChanged;

    public string Id => "deckhost.device.hid";

    public string Name => "USB devices (generic HID)";

    /// <summary>Where the descriptions are kept.</summary>
    public string Directory { get; }

    public IReadOnlyCollection<DeviceDefinition> Devices => Volatile.Read(ref _devices);

    public IDeviceOutput Output => this;

    /// <summary>The descriptions, for the device creator.</summary>
    public IReadOnlyList<HidDeviceDescription> Descriptions
    {
        get
        {
            lock (_sync)
            {
                return [.. _sessions.Values.Select(static session => session.Description)];
            }
        }
    }

    public Task InitializeAsync(IDevicePluginContext context, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _context = context;
        DeviceList.Local.Changed += OnDeviceListChanged;
        Refresh();
        return Task.CompletedTask;
    }

    /// <summary>Saves a description (new or changed) and opens its device when it's plugged in.</summary>
    public void Save(HidDeviceDescription description)
    {
        System.IO.Directory.CreateDirectory(Directory);
        File.WriteAllText(PathOf(description.Id), description.ToJson());
        Reload();
    }

    /// <summary>Removes a description; its device is no longer listed.</summary>
    public void Delete(string id)
    {
        var path = PathOf(id);
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        Reload();
    }

    /// <summary>Reads the descriptions again (closing and reopening devices whose description changed).</summary>
    public void Reload()
    {
        LoadDescriptions();
        Refresh();
        DevicesChanged?.Invoke(this, EventArgs.Empty);
    }

    public bool TryPushImage(string deviceId, string controlId, ReadOnlySpan<byte> rgba, int width, int height, out string? error)
    {
        error = null;
        return true;
    }

    public bool TrySetLight(string deviceId, string controlId, byte red, byte green, byte blue, out string? error)
    {
        error = null;
        return true;
    }

    public void Clear(string deviceId)
    {
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        DeviceList.Local.Changed -= OnDeviceListChanged;
        lock (_sync)
        {
            foreach (var session in _sessions.Values)
            {
                session.Close();
            }
        }

        foreach (var timer in _longPressTimers.Values)
        {
            timer.Dispose();
        }
    }

    private string PathOf(string id) => Path.Combine(Directory, HidDeviceDescription.MakeId(id) + ".json");

    private void LoadDescriptions()
    {
        var found = new List<HidDeviceDescription>();
        try
        {
            if (System.IO.Directory.Exists(Directory))
            {
                foreach (var file in System.IO.Directory.EnumerateFiles(Directory, "*.json"))
                {
                    if (HidDeviceDescription.FromJson(File.ReadAllText(file)) is { } description)
                    {
                        found.Add(description);
                    }
                    else
                    {
                        _context?.Log("Warning", $"{Path.GetFileName(file)} is not a valid USB device description.");
                    }
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _context?.Log("Warning", $"Could not read the USB device descriptions: {ex.Message}");
        }

        lock (_sync)
        {
            foreach (var session in _sessions.Values.ToList())
            {
                var replacement = found.FirstOrDefault(description => description.DeviceId == session.DeviceId);
                if (replacement is null || replacement.ToJson() != session.Description.ToJson())
                {
                    session.Close();
                    _sessions.Remove(session.DeviceId);
                }
            }

            foreach (var description in found)
            {
                if (!_sessions.ContainsKey(description.DeviceId))
                {
                    _sessions[description.DeviceId] = new Session(description);
                }
            }

            Volatile.Write(ref _devices, BuildDefinitions());
        }
    }

    private void OnDeviceListChanged(object? sender, DeviceListChangedEventArgs e)
    {
        if (!_disposed)
        {
            ThreadPool.QueueUserWorkItem(_ => Refresh());
        }
    }

    private void Refresh()
    {
        if (_context is null || _disposed)
        {
            return;
        }

        HidDevice[] hardware;
        try
        {
            hardware = [.. DeviceList.Local.GetHidDevices()];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return;
        }

        var changes = new List<(string DeviceId, string State, string Message)>();
        bool changed = false;
        lock (_sync)
        {
            foreach (var session in _sessions.Values)
            {
                var description = session.Description;
                var match = hardware.FirstOrDefault(device => device.VendorID == description.VendorId && device.ProductID == description.ProductId && Matches(device, description));
                if (match is null)
                {
                    if (session.Stream is not null)
                    {
                        session.Close();
                        changed = true;
                        changes.Add((session.DeviceId, "Disconnected", $"{description.Name} was unplugged."));
                    }

                    continue;
                }

                if (session.Stream is not null)
                {
                    continue;
                }

                if (!match.TryOpen(out var stream))
                {
                    changes.Add((session.DeviceId, "Error", $"Could not open {description.Name}: another program has it, the system keeps it (keyboards and mice on Windows), or on Linux the device rule is missing."));
                    continue;
                }

                stream.ReadTimeout = Timeout.Infinite;
                session.Open(stream);
                changed = true;
                changes.Add((session.DeviceId, "Connected", description.Name));
                new Thread(() => ReadLoop(session, stream)) { IsBackground = true, Name = $"HID {description.Name}" }.Start();
            }

            if (changed)
            {
                Volatile.Write(ref _devices, BuildDefinitions());
            }
        }

        foreach (var (deviceId, state, message) in changes)
        {
            _context?.ReportConnectionStatus(deviceId, state, message);
        }

        if (changed)
        {
            DevicesChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Whether this interface is the one the description names (by usage page) when it names one.</summary>
    private static bool Matches(HidDevice device, HidDeviceDescription description)
    {
        if (description.UsagePage is null)
        {
            return true;
        }

        var (page, usage) = HidSharpDeviceSource.TopUsage(device);
        return page == description.UsagePage && (description.Usage is null || usage == description.Usage);
    }

    private void ReadLoop(Session session, HidStream stream)
    {
        var parser = new HidInputParser(session.Description);
        var buffer = new byte[Math.Max(64, stream.Device.GetMaxInputReportLength())];
        try
        {
            while (!_disposed && ReferenceEquals(session.Stream, stream))
            {
                var length = stream.Read(buffer, 0, buffer.Length);
                if (length <= 0)
                {
                    continue;
                }

                foreach (var input in parser.Parse(buffer.AsSpan(0, length)))
                {
                    if (input.Event is InputCapabilities.Press or InputCapabilities.Release &&
                        session.Description.Controls.FirstOrDefault(control => control.Id == input.ControlId)?.Kind == HidControlKind.Key)
                    {
                        TrackLongPress(session.DeviceId, input.ControlId, input.Event);
                    }

                    _context?.ReportInputEvent(session.DeviceId, input.ControlId, input.Event, input.Value);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or TimeoutException or InvalidOperationException)
        {
            if (!_disposed && ReferenceEquals(session.Stream, stream))
            {
                lock (_sync)
                {
                    session.Close();
                    Volatile.Write(ref _devices, BuildDefinitions());
                }

                _context?.ReportConnectionStatus(session.DeviceId, "Disconnected", $"{session.Description.Name} stopped: {ex.Message}");
                DevicesChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    private void TrackLongPress(string deviceId, string controlId, InputCapabilities inputEvent)
    {
        var key = (deviceId, controlId);
        if (_longPressTimers.TryRemove(key, out var previous))
        {
            previous.Dispose();
        }

        if (inputEvent != InputCapabilities.Press)
        {
            return;
        }

        Timer? timer = null;
        timer = new Timer(_ =>
        {
            if (_longPressTimers.TryRemove(new KeyValuePair<(string, string), Timer>(key, timer!)))
            {
                timer!.Dispose();
                _context?.ReportInputEvent(deviceId, controlId, InputCapabilities.LongPress, 1);
            }
        });
        _longPressTimers[key] = timer;
        timer.Change(LongPressDelay, Timeout.InfiniteTimeSpan);
    }

    private IReadOnlyCollection<DeviceDefinition> BuildDefinitions() =>
        [.. _sessions.Values
            .OrderBy(static session => session.Description.Name, StringComparer.OrdinalIgnoreCase)
            .Select(static session => new DeviceDefinition(
                session.DeviceId,
                session.Description.Name,
                session.Description.PhysicalControls(),
                session.Stream is not null,
                $"USB {session.Description.VendorId:X4}:{session.Description.ProductId:X4}"))];

    private sealed class Session(HidDeviceDescription description)
    {
        public HidDeviceDescription Description { get; } = description;

        public string DeviceId => Description.DeviceId;

        public HidStream? Stream { get; private set; }

        public void Open(HidStream stream) => Stream = stream;

        public void Close()
        {
            var stream = Stream;
            Stream = null;
            try
            {
                stream?.Dispose();
            }
            catch (IOException)
            {
            }
        }
    }
}

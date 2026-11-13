using HidSharp;

namespace DeckHost.Device.Hid;

/// <summary>A HID interface on this computer, as the device creator lists it.</summary>
/// <param name="Path">The system's path for this interface (opens exactly this one).</param>
/// <param name="UsagePage">The interface's top-level usage page (0xFF00+ is vendor-defined, 1 generic desktop, 0x0C consumer).</param>
public sealed record HidDeviceInfo(string Path, int VendorId, int ProductId, string Name, string? Manufacturer, int? UsagePage, int? Usage, int InputReportLength)
{
    public string Ids => $"{VendorId:X4}:{ProductId:X4}";

    /// <summary>What the interface is, from its usage page.</summary>
    public string Kind => (UsagePage, Usage) switch
    {
        (1, 6) => "keyboard",
        (1, 2) => "mouse",
        (1, 4) => "joystick",
        (1, 5) => "game pad",
        (0x0C, _) => "media keys",
        (>= 0xFF00, _) => "vendor-defined",
        (null, _) => "unknown",
        _ => $"usage page 0x{UsagePage:X2}",
    };

    /// <summary>Windows keeps keyboards and mice to itself: their reports can't be read.</summary>
    public bool IsReservedOnWindows => UsagePage == 1 && Usage is 2 or 6;

    /// <summary>A device DeckHost already drives (Elgato Stream Decks).</summary>
    public bool IsKnownDeck => VendorId == 0x0fd9;
}

/// <summary>Lists HID interfaces and listens to one; the device creator's view of the hardware (tests replace it).</summary>
public interface IHidDeviceSource
{
    IReadOnlyList<HidDeviceInfo> List();

    /// <summary>
    /// Opens <paramref name="path"/> and calls <paramref name="report"/> for every input report (on a
    /// background thread) until disposed; <paramref name="failed"/> when it can't be opened or is unplugged.
    /// </summary>
    IDisposable Listen(string path, Action<byte[]> report, Action<string> failed);
}

/// <summary>The real hardware, through HidSharp.</summary>
public sealed class HidSharpDeviceSource : IHidDeviceSource
{
    public IReadOnlyList<HidDeviceInfo> List()
    {
        try
        {
            return [.. DeviceList.Local.GetHidDevices().Select(Describe).OrderBy(static info => info.Name, StringComparer.OrdinalIgnoreCase).ThenBy(static info => info.Path, StringComparer.Ordinal)];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or InvalidOperationException)
        {
            return [];
        }
    }

    public IDisposable Listen(string path, Action<byte[]> report, Action<string> failed)
    {
        var device = DeviceList.Local.GetHidDevices().FirstOrDefault(candidate => candidate.DevicePath == path);
        var listener = new Listener();
        if (device is null)
        {
            failed("The device is gone (unplugged?).");
            return listener;
        }

        if (!device.TryOpen(out var stream))
        {
            failed(OperatingSystem.IsLinux()
                ? "Can't open it: on Linux it needs a device rule (shown when you save), or another program has it."
                : "Can't open it: another program has it, or the system keeps it to itself (keyboards and mice on Windows).");
            return listener;
        }

        listener.Start(stream, report, failed);
        return listener;
    }

    internal static HidDeviceInfo Describe(HidDevice device)
    {
        var (page, usage) = TopUsage(device);
        var name = TryGet(device.GetProductName) ?? TryGet(device.GetFriendlyName) ?? $"HID device {device.VendorID:X4}:{device.ProductID:X4}";
        int length;
        try
        {
            length = device.GetMaxInputReportLength();
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or UnauthorizedAccessException)
        {
            length = 0;
        }

        return new HidDeviceInfo(device.DevicePath, device.VendorID, device.ProductID, name, TryGet(device.GetManufacturer), page, usage, length);
    }

    /// <summary>The interface's top-level usage (page, usage) from its report descriptor.</summary>
    internal static (int? Page, int? Usage) TopUsage(HidDevice device)
    {
        try
        {
            var value = device.GetReportDescriptor().DeviceItems.SelectMany(static item => item.Usages.GetAllValues()).FirstOrDefault();
            return value == 0 ? (null, null) : ((int)(value >> 16), (int)(value & 0xFFFF));
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            return (null, null);
        }
    }

    private static string? TryGet(Func<string> read)
    {
        try
        {
            var value = read();
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or UnauthorizedAccessException or InvalidOperationException)
        {
            return null;
        }
    }

    private sealed class Listener : IDisposable
    {
        private HidStream? _stream;

        public void Start(HidStream stream, Action<byte[]> report, Action<string> failed)
        {
            _stream = stream;
            stream.ReadTimeout = Timeout.Infinite;
            var thread = new Thread(() =>
            {
                var buffer = new byte[Math.Max(64, stream.Device.GetMaxInputReportLength())];
                try
                {
                    while (ReferenceEquals(_stream, stream))
                    {
                        var length = stream.Read(buffer, 0, buffer.Length);
                        if (length > 0)
                        {
                            report(buffer[..length]);
                        }
                    }
                }
                catch (Exception ex) when (ex is IOException or ObjectDisposedException or TimeoutException or InvalidOperationException)
                {
                    if (ReferenceEquals(_stream, stream))
                    {
                        failed($"The device stopped sending: {ex.Message}");
                    }
                }
            })
            { IsBackground = true, Name = "HID listen" };
            thread.Start();
        }

        public void Dispose()
        {
            var stream = Interlocked.Exchange(ref _stream, null);
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

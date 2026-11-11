namespace DeckHost.Contracts.Interfaces;

/// <summary>
/// Device output. The host calls these from a single worker thread, never the UI thread.
/// </summary>
public interface IDeviceOutput
{
    /// <summary>Pushes an RGBA8888 image sized <c>RenderWidth x RenderHeight</c> to one control.</summary>
    bool TryPushImage(string deviceId, string controlId, ReadOnlySpan<byte> rgba, int width, int height, out string? error);

    /// <summary>Sets the RGB LED tied to a control (see <c>PhysicalControl.LedIndex</c>).</summary>
    bool TrySetLight(string deviceId, string controlId, byte red, byte green, byte blue, out string? error);

    /// <summary>Blanks all images and lights on the device, for example on shutdown.</summary>
    void Clear(string deviceId);
}

namespace DeckHost.Core.Models;

/// <summary>A control's rendered image (RGBA8888, premultiplied), as shown on the device.</summary>
public sealed record ControlFrame(string DeviceId, string ControlId, int Width, int Height, byte[] Rgba);

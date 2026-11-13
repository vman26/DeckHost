namespace DeckHost.Core.Models;

/// <summary>The color a control's LED shows now (also while the device is unplugged: the editor's preview).</summary>
public sealed record LightState(string DeviceId, string ControlId, byte Red, byte Green, byte Blue);

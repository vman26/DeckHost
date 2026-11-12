namespace DeckHost.Contracts.Interfaces;

/// <summary>
/// Optional addition to <see cref="IDeviceOutput"/> for devices whose screen is larger than the
/// control images: the area between and around the controls (the "backdrop"). Without it that
/// area keeps showing whatever the device stores itself, for example its idle screen.
/// The host calls it from the same worker thread as <see cref="IDeviceOutput"/>.
/// </summary>
public interface IDeviceBackdropOutput
{
    /// <summary>
    /// Fills the screen behind the control images with one color. It stays until the next call
    /// or <see cref="IDeviceOutput.Clear"/>. The host sends every control image again afterwards.
    /// </summary>
    bool TrySetBackdrop(string deviceId, byte red, byte green, byte blue, out string? error);
}

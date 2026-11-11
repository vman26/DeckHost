namespace DeckHost.Contracts.Interfaces;

/// <summary>
/// Services the host offers to actions. Thread-safe: actions may call these from background
/// work (for example after an async request finished).
/// </summary>
public interface IActionHost
{
    /// <summary>Re-renders the device's controls (only changed controls are pushed).</summary>
    void RequestRender(string deviceId);

    /// <summary>Activates a profile of the device by id or (case-insensitive) name.</summary>
    void ActivateProfile(string deviceId, string profileIdOrName);

    /// <summary>Activates the next (+1) or previous (-1) profile of the device, wrapping around.</summary>
    void ActivateRelativeProfile(string deviceId, int offset);
}

public sealed class NullActionHost : IActionHost
{
    public static NullActionHost Instance { get; } = new();

    private NullActionHost()
    {
    }

    public void RequestRender(string deviceId)
    {
    }

    public void ActivateProfile(string deviceId, string profileIdOrName)
    {
    }

    public void ActivateRelativeProfile(string deviceId, int offset)
    {
    }
}

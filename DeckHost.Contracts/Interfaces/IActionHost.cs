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

    /// <summary>Shows the next (+1) or previous (-1) page of the active profile, wrapping around.</summary>
    void ActivateRelativePage(string deviceId, int offset)
    {
    }

    /// <summary>Shows page <paramref name="number"/> (from 1) of the active profile.</summary>
    void ActivatePage(string deviceId, int number)
    {
    }

    /// <summary>Opens a folder (a layout of the active profile) on the device.</summary>
    void OpenFolder(string deviceId, string folderId)
    {
    }

    /// <summary>Leaves the open folder: back to the layout whose key opened it.</summary>
    void CloseFolder(string deviceId)
    {
    }
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

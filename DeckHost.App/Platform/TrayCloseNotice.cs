using Avalonia.Controls;

namespace DeckHost.App.Platform;

/// <summary>What closing the main window does.</summary>
public enum WindowCloseOutcome
{
    /// <summary>The window closes and DeckHost exits.</summary>
    Close,

    /// <summary>The window only hides; DeckHost keeps running in the tray.</summary>
    HideToTray,

    /// <summary>The first close to the tray: say so (and let the user quit instead) before hiding.</summary>
    AskFirst,
}

/// <summary>
/// The first time closing the window hides DeckHost to the tray, the user is told once: a hidden
/// DeckHost looks like a closed one, and people wondered why their deck still worked (or quit it
/// from the task manager). Avalonia's tray icon has no notifications, so the window asks first.
/// </summary>
public static class TrayCloseNotice
{
    public const string Message = "DeckHost keeps running in the tray, so your deck keeps working. Quit from the tray icon, or change this in DeckHost settings.";

    public static WindowCloseOutcome Decide(WindowCloseReason reason, bool exiting, AppSettingsData settings)
    {
        if (!DeckHost.App.App.KeepsWindowOnClose(reason, exiting, settings.KeepsRunningInTray))
        {
            return WindowCloseOutcome.Close;
        }

        return settings.TrayNoticeShown ? WindowCloseOutcome.HideToTray : WindowCloseOutcome.AskFirst;
    }

    /// <summary>
    /// The settings after the user answered: the notice isn't shown again, and "Quit DeckHost"
    /// also turns closing to the tray off, so the next close quits as the user chose.
    /// </summary>
    public static AppSettingsData Answer(AppSettingsData settings, bool keepRunning) =>
        keepRunning ? settings with { TrayNoticeShown = true } : settings with { TrayNoticeShown = true, CloseToTray = false };
}

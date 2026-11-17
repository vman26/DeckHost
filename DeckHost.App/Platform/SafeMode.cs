namespace DeckHost.App.Platform;

/// <summary>
/// Safe mode: DeckHost started with only what is needed to use the deck, because the last starts ended in a crash.
/// Web views, Stream Deck / OpenAction / installed plugins and the editor's first selection stay off, so whatever
/// crashed can't run again. Set once at start (<see cref="Program"/>); the banner offers a normal restart.
/// </summary>
internal static class SafeMode
{
    /// <summary>The command-line switch that starts DeckHost in safe mode.</summary>
    public const string Argument = "--safe-mode";

    public static bool IsOn { get; private set; }

    /// <summary>Why safe mode is on, in a sentence for the banner.</summary>
    public static string Reason { get; private set; } = string.Empty;

    public static void Set(bool on, string reason)
    {
        IsOn = on;
        Reason = on ? reason : string.Empty;
    }
}

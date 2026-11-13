namespace DeckHost.Contracts;

/// <summary>
/// DeckHost's own icons as SVG path data on a 24×24 grid, filled with the even-odd rule, for
/// <see cref="Interfaces.IRenderContext.DrawIcon"/> and <see cref="RenderContextExtensions.DrawTile"/>.
/// Plugins may use them, or pass their own path data on the same grid.
/// </summary>
public static class Icons
{
    public const string PlayPause = "M3 5L12 12L3 19Z M14 5h3v14h-3Z M19 5h3v14h-3Z";
    public const string Previous = "M4 5h2.5v14H4Z M20 5L8 12L20 19Z";
    public const string Next = "M17.5 5H20v14h-2.5Z M4 5L16 12L4 19Z";
    public const string Stop = "M6 6h12v12H6Z";
    public const string Speaker = "M3 9h4l5-4v14l-5-4H3Z";
    public const string VolumeUp = Speaker + " M15 11h2.5V8.5h2V11H22v2h-2.5v2.5h-2V13H15Z";
    public const string VolumeDown = Speaker + " M15 11h7v2h-7Z";
    public const string Muted = Speaker + " M14.4 9.5L15.9 8L18.3 10.4L20.7 8L22.2 9.5L19.8 11.9L22.2 14.3L20.7 15.8L18.3 13.4L15.9 15.8L14.4 14.3L16.8 11.9Z";
    public const string Lock = "M5 11h14v10H5Z M11 14h2v4h-2Z M7.5 11V8a4.5 4.5 0 0 1 9 0v3h-2.2V8a2.3 2.3 0 0 0-4.6 0v3Z";
    public const string Apps = "M4 4h7v7H4Z M13 4h7v7h-7Z M4 13h7v7H4Z M13 13h7v7h-7Z";
    public const string OpenLink = "M4 6h7v2H6v10h10v-5h2v7H4Z M13 3h8v8h-2V6.4l-7.3 7.3-1.4-1.4L17.6 5H13Z";
    public const string Keyboard = "M2 6h20v12H2Z M4 8h16v8H4Z M6 10h2v2H6Z M9.5 10h2v2h-2Z M13 10h2v2h-2Z M16.5 10h2v2h-2Z M7 13.5h10v1.5H7Z";
    public const string Text = "M5 4h14v4h-2V6h-4v12h2.5v2h-7v-2H11V6H7v2H5Z";
    public const string SwitchProfile = "M3 6.5h12v2H3Z M14 3.5L21 7.5L14 11.5Z M9 15.5h12v2H9Z M10 12.5L3 16.5L10 20.5Z";
    public const string Stack = "M4 4h16v4H4Z M4 10h16v4H4Z M4 16h16v4H4Z";
    public const string Chip = "M7 7h10v10H7Z M9.5 9.5h5v5h-5Z M9 3h2v3H9Z M13 3h2v3h-2Z M9 18h2v3H9Z M13 18h2v3h-2Z M3 9h3v2H3Z M3 13h3v2H3Z M18 9h3v2h-3Z M18 13h3v2h-3Z";
    /// <summary>A switch, for a Multi Action Switch.</summary>
    public const string Toggle = "M7 7h10a5 5 0 0 1 0 10H7a5 5 0 0 1 0-10Z M7 9a3 3 0 1 0 0 6h10a3 3 0 1 0 0-6Z M17 10a2 2 0 1 0 0 4a2 2 0 1 0 0-4Z";

    /// <summary>A key with one, two and a long press, for Key Logic.</summary>
    public const string KeyLogic = "M4 4h16v16H4Z M6 6v12h12V6Z M8 8h2v2H8Z M11 8h2v2h-2Z M14 8h2v2h-2Z M8 13h8v2H8Z";

    /// <summary>A folder.</summary>
    public const string Folder = "M3 5h7l2 2h9v12H3Z M5 9v8h14V9Z";

    /// <summary>An arrow back (out of a folder).</summary>
    public const string Back = "M10.5 5L12 6.5L7.5 11H20v2H7.5l4.5 4.5L10.5 19L3.5 12Z";

    /// <summary>Pages with an arrow to the next one.</summary>
    public const string PageNext = "M3 4h11v16H3Z M5 6v12h7V6Z M16 8.5L19.5 12L16 15.5Z M20 6h1.5v12H20Z";

    /// <summary>Pages with an arrow to the previous one.</summary>
    public const string PagePrevious = "M10 4h11v16H10Z M12 6v12h7V6Z M8 8.5L4.5 12L8 15.5Z M2.5 6H4v12H2.5Z";

    /// <summary>A clock face with hands.</summary>
    public const string Clock = "M12 2a10 10 0 1 0 0 20a10 10 0 1 0 0-20Z M12 4.2a7.8 7.8 0 1 1 0 15.6a7.8 7.8 0 1 1 0-15.6Z M11 6.5h2v5h4v2h-6Z";

    /// <summary>A hand swiping left and right.</summary>
    public const string Swipe = "M3 12L8 7v3.5h8V7l5 5-5 5v-3.5H8V17Z";

    public const string Counter = "M11 5h2v14h-2Z M5 11h14v2H5Z";

    /// <summary>A lightning bolt (a workflow trigger).</summary>
    public const string Bolt = "M13 2 4 14h7l-1 8 9-12h-7Z";

    public const string Play = "M8 5v14l11-7Z";

    /// <summary>An hourglass.</summary>
    public const string Timer = "M6 2h12v2h-1.5v3.5L13 12l3.5 4.5V20H18v2H6v-2h1.5v-3.5L11 12 7.5 7.5V4H6Z";

    /// <summary>A decision diamond.</summary>
    public const string Branch = "M12 2 22 12 12 22 2 12Z M12 5 5 12 12 19 19 12Z";

    public const string Repeat = "M17 1l4 4-4 4V6H7a2 2 0 0 0-2 2v3H3V8a4 4 0 0 1 4-4h10Z M7 23l-4-4 4-4v3h10a2 2 0 0 0 2-2v-3h2v3a4 4 0 0 1-4 4H7Z";

    /// <summary>Brackets around an x.</summary>
    public const string Variable = "M4 4h6v2H6v12h4v2H4Z M14 4h6v16h-6v-2h4V6h-4Z M9 9h2l1 2 1-2h2l-2 3 2 3h-2l-1-2-1 2H9l2-3Z";

    /// <summary>Two nodes and a link.</summary>
    public const string Workflow = "M3 3h7v7H3Z M14 14h7v7h-7Z M10 5.5h4a3.5 3.5 0 0 1 3.5 3.5v5h-2V9A1.5 1.5 0 0 0 14 7.5h-4Z";
}

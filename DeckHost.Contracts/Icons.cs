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
    public const string Counter = "M11 5h2v14h-2Z M5 11h14v2H5Z";
}

namespace DeckHost.Contracts.Enums
{
    [Flags]
    public enum OutputCapabilities
    {
        None = 0,
        ColorImage = 1 << 0,
        Text = 1 << 1,
        SegmentLed = 1 << 2,
        FullDisplay = 1 << 3,
        All = ColorImage | Text | SegmentLed | FullDisplay,
    }
}

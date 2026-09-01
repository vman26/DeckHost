namespace DeckHost.Contracts.Enums
{
    [Flags]
    public enum InputCapabilities
    {
        None = 0,
        Press = 1 << 0,
        LongPress = 1 << 1,
        Release = 1 << 2,
        Rotate = 1 << 3,
        Touch = 1 << 4,
        Swipe = 1 << 5,
        All = Press | LongPress | Release | Rotate | Touch | Swipe,
    }
}

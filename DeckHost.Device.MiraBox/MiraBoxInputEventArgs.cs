namespace DeckHost.Device.MiraBox;

public sealed class MiraBoxInputEventArgs : EventArgs
{
    public MiraBoxInputEventArgs(MiraBoxEventType eventType, int controlId, int x, int y)
    {
        EventType = eventType;
        ControlId = controlId;
        X = x;
        Y = y;
    }

    public MiraBoxEventType EventType { get; }

    public int ControlId { get; }

    public int X { get; }

    public int Y { get; }
}

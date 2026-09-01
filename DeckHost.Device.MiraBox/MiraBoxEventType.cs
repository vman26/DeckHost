namespace DeckHost.Device.MiraBox;

public enum MiraBoxEventType
{
    Unknown = 0,
    KeyPress = 0x01,
    KeyRelease = 0x02,
    KnobLeft = 0x03,
    KnobRight = 0x04,
    KnobPress = 0x05,
    KnobRelease = 0x06,
    SwipeLeft = 0x07,
    SwipeRight = 0x08,
    ToggleUp = 0x09,
    ToggleDown = 0x0A,
    DipLeft = 0x0B,
    DipLeftEnd = 0x0C,
    DipRight = 0x0D,
    DipRightEnd = 0x0E,
    DipPress = 0x0F,
    DipRelease = 0x10,
}

namespace DeckHost.Contracts;

/// <summary>
/// Shared variable-store key conventions. Actions and the renderer talk to each other through
/// these keys, so always build them here instead of with string literals.
/// </summary>
public static class VariableKeys
{
    public const string LastEvent = "lastEvent";
    public const string LastValue = "lastValue";
    public const string LastControl = "lastControl";
    public const string LastAction = "lastAction";
    public const string LastActionAt = "lastActionAt";
    public const string ExecutionCount = "executionCount";
    public const string AppearanceText = "appearance.text";
    public const string AppearanceImage = "appearance.image";
    public const string AppearanceBackground = "appearance.background";
    public const string AppearanceIcon = "appearance.icon";
    public const string HardwareId = "hardwareId";
    public const string LastError = "lastError";
    public const string Status = "status";
    public const string Counter = "counter";

    /// <summary><c>devices.{deviceId}.</c></summary>
    public static string Device(string deviceId) => $"devices.{deviceId}.";

    /// <summary><c>devices.{deviceId}.{controlId}.</c></summary>
    public static string Control(string deviceId, string controlId) => $"devices.{deviceId}.{controlId}.";

    public static string For(string deviceId, string controlId, string suffix) => Control(deviceId, controlId) + suffix;
}

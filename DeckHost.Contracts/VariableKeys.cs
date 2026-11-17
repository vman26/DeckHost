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
    public const string AppearanceTile = "appearance.tile";
    public const string AppearanceTileStyle = "appearance.tileStyle";
    public const string HardwareId = "hardwareId";
    public const string LastError = "lastError";
    public const string Status = "status";
    public const string Counter = "counter";

    /// <summary>A Multi Action Switch's set that the next press runs (<see cref="Models.StepGroups.First"/> or <see cref="Models.StepGroups.Second"/>).</summary>
    public const string SwitchState = "switchState";

    /// <summary>Prefix of a workflow's own state under a control (e.g. which output a Toggle node takes next).</summary>
    public const string WorkflowState = "workflow";

    /// <summary>The name of the app in the foreground (where the platform reports it).</summary>
    public const string ForegroundApp = "system.foregroundApp";

    /// <summary><c>vars.{name}</c>: a variable that workflows set and check (names ignore case).</summary>
    public static string UserVariable(string name) => $"vars.{name.Trim().ToLowerInvariant()}";

    /// <summary>
    /// <c>vars.profile|{profileId}|{name}</c>: a variable that only the workflows of one profile share. The
    /// <c>|</c> keeps the parts apart (device and profile ids contain dots) and the key still starts with
    /// <c>vars.</c>, like every variable workflows set.
    /// </summary>
    public static string ProfileVariable(string profileId, string name) => $"vars.profile|{profileId}|{name.Trim().ToLowerInvariant()}";

    /// <summary><c>vars.control|{deviceId}|{controlId}|{name}</c>: a variable that belongs to one control's workflow.</summary>
    public static string ControlVariable(string deviceId, string controlId, string name) => $"vars.control|{deviceId}|{controlId}|{name.Trim().ToLowerInvariant()}";

    /// <summary><c>devices.{deviceId}.</c></summary>
    public static string Device(string deviceId) => $"devices.{deviceId}.";

    /// <summary><c>devices.{deviceId}.{controlId}.</c></summary>
    public static string Control(string deviceId, string controlId) => $"devices.{deviceId}.{controlId}.";

    public static string For(string deviceId, string controlId, string suffix) => Control(deviceId, controlId) + suffix;
}

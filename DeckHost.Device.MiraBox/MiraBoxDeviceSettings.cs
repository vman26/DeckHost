using DeckHost.Contracts.Models;
using System.Text.Json;

namespace DeckHost.Device.MiraBox;

/// <summary>
/// MiraBox device settings: device configuration (vibration, boot animation, LED behaviour; the N4
/// Pro only), key and LED brightness, and the idle screen; each model gets the ones it has
/// (<see cref="For"/>). Stored per device in <c>%AppData%/DeckHost/devices/&lt;id&gt;.json</c>
/// and applied when changed and whenever the device connects. Items left on their default are
/// not sent unless DeckHost changed them before; then the default is sent once to restore it.
/// </summary>
internal static class MiraBoxDeviceSettings
{
    public const string Vibration = "vibration";
    public const string BootVideo = "bootVideo";
    public const string LedFollowsKeyLight = "ledFollowKeyLight";
    public const string KeyLightWhenDisconnected = "keyLightOnDisconnect";
    public const string KeyBrightness = "keyBrightness";
    public const string LedBrightness = "ledBrightness";
    public const string IdleScreen = "idleScreen";
    public const string IdleImage = "idleImage";

    /// <summary>The idle screen last written to the device, so it isn't rewritten on every start.</summary>
    public const string AppliedIdleScreen = "appliedIdleScreen";

    private const byte On = 0x11;
    private const byte Off = 0xFF;
    private const byte Follow = 0x1F;

    /// <summary>0-100 %. Empty means "the device default" (100), which isn't sent.</summary>
    private static readonly NumberRange Percentage = new(0, 100) { Unit = "%", UnsetValue = DefaultBrightness };

    private static readonly ActionSettingOption[] SwitchOptions =
    [
        new("default", "Default (on)"),
        new("on", "On"),
        new("off", "Off"),
    ];

    /// <summary>The N4 Pro's settings (every one).</summary>
    public static IReadOnlyList<ActionSettingDefinition> Definitions { get; } = For(MiraBoxModels.N4Pro);

    /// <summary>The settings a model has: the configuration items only where the SDK's are known, LED brightness only with LEDs.</summary>
    public static IReadOnlyList<ActionSettingDefinition> For(MiraBoxModel model)
    {
        var definitions = new List<ActionSettingDefinition>();
        if (model.HasDeviceConfig)
        {
            definitions.Add(new(Vibration, "Vibrate when a control is used", ActionSettingKind.Choice, "default", Options: SwitchOptions));
            definitions.Add(new(BootVideo, "Play the start-up animation", ActionSettingKind.Choice, "default", Options: SwitchOptions));
            definitions.Add(new(LedFollowsKeyLight, "LEDs follow the key brightness", ActionSettingKind.Choice, "default", Options: SwitchOptions));
            definitions.Add(new(KeyLightWhenDisconnected, "Keep keys lit when DeckHost isn't running", ActionSettingKind.Choice, "default", Options: SwitchOptions));
        }

        definitions.Add(new(KeyBrightness, "Screen brightness", ActionSettingKind.Number, Placeholder: "Default") { Range = Percentage });
        if (model.LedCount > 0)
        {
            definitions.Add(new(LedBrightness, "LED brightness", ActionSettingKind.Number, Placeholder: "Default") { Range = Percentage });
        }

        definitions.Add(new(IdleScreen, "Idle / start-up screen", ActionSettingKind.Choice, "keep",
            Description: "What the device shows when DeckHost isn't running. Choose Black or an image to replace an old picture stored on the device.",
            Options:
            [
                new("keep", "Leave as it is"),
                new("black", "Black"),
                new("image", "Image file"),
            ]));
        definitions.Add(new(IdleImage, "Idle screen image", ActionSettingKind.FilePath, Placeholder: string.Create(System.Globalization.CultureInfo.InvariantCulture, $"PNG or JPG, ideally {model.ScreenWidth}×{model.ScreenHeight}")));
        return definitions;
    }

    private static string Directory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DeckHost", "devices");

    public static ActionSettings Load(string deviceId)
    {
        try
        {
            var path = Path.Combine(Directory, deviceId + ".json");
            return File.Exists(path) ? JsonSerializer.Deserialize<ActionSettings>(File.ReadAllText(path)) ?? ActionSettings.Empty : ActionSettings.Empty;
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            return ActionSettings.Empty;
        }
    }

    public static void Save(string deviceId, ActionSettings settings)
    {
        System.IO.Directory.CreateDirectory(Directory);
        File.WriteAllText(Path.Combine(Directory, deviceId + ".json"), JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
    }

    /// <summary>The applied-value markers kept in the settings file (not shown in the editor).</summary>
    public static readonly string[] Markers = [AppliedIdleScreen, AppliedConfig, AppliedKeyBrightness, AppliedLedBrightness];

    /// <summary>What the device does when nothing is set: the SDK's own defaults.</summary>
    public const int DefaultBrightness = 100;

    private const string AppliedConfig = "appliedConfig";
    private const string AppliedKeyBrightness = "appliedKeyBrightness";
    private const string AppliedLedBrightness = "appliedLedBrightness";

    /// <summary>
    /// Sends the settings to the device. Values that are set are always sent (the device forgets
    /// some of them when it loses power). A value set back to "Device default" is sent once as the
    /// default, so the device really returns to it. Returns the problems found and whether the
    /// idle screen was rewritten (which also clears the key images).
    /// </summary>
    public static (List<string> Problems, bool IdleScreenWritten) Apply(string deviceKey, MiraBoxModel model, ActionSettings settings, Action<string, string?> remember)
    {
        var problems = new List<string>();
        if (!model.HasDeviceConfig)
        {
            settings = settings.With(AppliedConfig, null);
        }

        // Configer takes all items at once, in the device's order. Unset items use the SDK default (on).
        var items = new[] { LedFollowsKeyLight, KeyLightWhenDisconnected, null, Vibration, null, BootVideo };
        var config = string.Join(",", items.Select(item => item is null ? "-" : settings.Get(item, "default")));
        var anySet = model.HasDeviceConfig && items.Any(item => item is not null && settings.Get(item, "default") != "default");
        if (anySet || !string.IsNullOrEmpty(settings.Get(AppliedConfig)))
        {
            var states = items.Select(item => item is null ? Follow : settings.Get(item, "default") switch
            {
                "off" => Off,
                _ => On,
            }).ToArray();
            if (Check(MiraBoxNative.SetDeviceConfig(deviceKey, states), "device configuration", problems))
            {
                remember(AppliedConfig, anySet ? config : null);
            }
        }

        ApplyBrightness(settings, KeyBrightness, AppliedKeyBrightness, "screen brightness", value => MiraBoxNative.SetKeyBrightness(deviceKey, (byte)value), remember, problems);
        if (model.LedCount > 0)
        {
            ApplyBrightness(settings, LedBrightness, AppliedLedBrightness, "LED brightness", value => MiraBoxNative.SetLedBrightness(deviceKey, (byte)Math.Round(value * 2.55)), remember, problems);
        }

        var idle = settings.Get(IdleScreen, "keep");
        var idleKey = idle == "image" ? $"image:{settings.Get(IdleImage)}:{SafeTimestamp(settings.Get(IdleImage))}" : idle;
        var idleWritten = false;
        if (idle != "keep" && settings.Get(AppliedIdleScreen) != idleKey)
        {
            var result = idle == "image"
                ? MiraBoxNative.SendBackgroundFile(deviceKey, settings.Get(IdleImage))
                : MiraBoxNative.SendBackground(deviceKey, new byte[model.ScreenWidth * model.ScreenHeight * 4], model.ScreenWidth, model.ScreenHeight);
            if (Check(result, "idle screen", problems))
            {
                remember(AppliedIdleScreen, idleKey);
                idleWritten = true;
            }
        }

        return (problems, idleWritten);
    }

    /// <summary>The LED brightness to use (0–255): the device setting, else full.</summary>
    public static byte LedBrightnessLevel(string deviceId) =>
        (byte)Math.Round((Load(deviceId).GetInt(LedBrightness, -1) is >= 0 and <= 100 and var percent ? percent : DefaultBrightness) * 2.55);

    private static void ApplyBrightness(ActionSettings settings, string key, string marker, string what, Func<int, int> send, Action<string, string?> remember, List<string> problems)
    {
        if (settings.GetInt(key, -1) is >= 0 and <= 100 and var value)
        {
            if (Check(send(value), what, problems))
            {
                remember(marker, value.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
        }
        else if (!string.IsNullOrEmpty(settings.Get(marker)) && Check(send(DefaultBrightness), what, problems))
        {
            // Cleared after DeckHost had set it: back to the default once.
            remember(marker, null);
        }
    }

    private static string SafeTimestamp(string path)
    {
        try
        {
            return File.Exists(path) ? File.GetLastWriteTimeUtc(path).Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture) : "missing";
        }
        catch (Exception ex) when (ex is IOException or ArgumentException)
        {
            return "missing";
        }
    }

    private static bool Check(int result, string what, List<string> problems)
    {
        if (result >= 0)
        {
            return true;
        }

        problems.Add($"{what}: {MiraBoxNative.Describe(result)}");
        return false;
    }
}

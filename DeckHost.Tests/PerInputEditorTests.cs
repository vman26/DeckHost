using DeckHost.App.SystemPlugins;
using DeckHost.App.ViewModels;
using DeckHost.Contracts.Enums;
using DeckHost.Contracts.Interfaces;
using DeckHost.Contracts.Models;
using DeckHost.Core;
using DeckHost.Core.Models;
using DeckHost.Core.State;

namespace DeckHost.Tests;

/// <summary>Per-input settings in the editor and in Switch Profile.</summary>
public class PerInputEditorTests
{
    private const string DeviceId = "dev";
    private const InputCapabilities Knob = InputCapabilities.Rotate | InputCapabilities.Press | InputCapabilities.Release;

    private static readonly ActionSettingDefinition Mode = new("mode", "Mode", ActionSettingKind.Choice, "a", Options: [new("a", "A"), new("b", "B"), new("c", "C")])
    {
        PerInput = true,
        InputDefaults = new Dictionary<string, string> { [ControlInputs.RotateLeft] = "b", [ControlInputs.RotateRight] = "c" },
    };

    private static readonly ActionSettingDefinition Name = new("name", "Name") { PerInput = true, ShowWhen = new("mode", "c") };

    private static readonly ActionSettingDefinition Shared = new("shared", "Shared", DefaultValue: "x");

    private const string KnobId = "encoder.0";
    private const string ModeKey = "mode";
    private const string SwitchProfile = "builtin.switch-profile";
    private const string GamingCall = "profile:Gaming";
    private const string PreviousCall = "relative:-1";
    private const string NextCall = "relative:1";

    private static KeyValuePair<string, string> Pair(string key, string value) => new(key, value);

    private static ControlBinding SwitchProfileOnTheKnob(params KeyValuePair<string, string>[] settings)
    {
        var reference = new ActionReference(SystemPluginSet.BuiltInPluginId, "Built-ins", SwitchProfile, "Switch Profile", InputCapabilities.None, OutputCapabilities.None,
            new ActionSettings(settings));
        return new ControlBinding(KnobId, ProfileActionKind.Single, reference, new ControlAppearance(), []);
    }

    private static void Send(ActionRouter router, DeviceProfile profile, RecordingHost host, InputCapabilities type, int value) =>
        router.Dispatch(profile, new DeviceInputEvent(DeviceId, KnobId, type, value), host);

    [Test]
    public void Dispatch_SwitchProfileOnAKnob_DoesWhatEachInputIsSetTo()
    {
        // Arrange
        using var store = new VariableStore();
        var host = new RecordingHost();
        var router = new ActionRouter(store, SystemPluginSet.Create());
        var profile = new DeviceProfile("profile", "Profile", DeviceId,
        [
            SwitchProfileOnTheKnob(Pair("mode@press", "specific"), Pair("profile@press", "Gaming"), Pair("mode@rotate-right", "none")),
        ]);

        // Act
        Send(router, profile, host, InputCapabilities.Press, 1);
        Send(router, profile, host, InputCapabilities.Release, 0);
        Send(router, profile, host, InputCapabilities.Rotate, -1); // default: previous
        Send(router, profile, host, InputCapabilities.Rotate, 1); // set to do nothing

        // Assert
        Assert.That(host.Calls, Is.EqualTo(new[] { GamingCall, PreviousCall }));
    }

    [Test]
    public void Dispatch_SwitchProfileSavedBeforePerInputSettings_KeepsWorking()
    {
        // Arrange (press used the mode, turning stepped by direction)
        using var store = new VariableStore();
        var host = new RecordingHost();
        var router = new ActionRouter(store, SystemPluginSet.Create());
        var profile = new DeviceProfile("profile", "Profile", DeviceId, [SwitchProfileOnTheKnob(Pair(ModeKey, "previous"))]);

        // Act
        Send(router, profile, host, InputCapabilities.Press, 1);
        Send(router, profile, host, InputCapabilities.Rotate, 1);
        Send(router, profile, host, InputCapabilities.Rotate, -1);

        // Assert
        Assert.That(host.Calls, Is.EqualTo(new[] { PreviousCall, NextCall, PreviousCall }));
    }

    private static SettingsEditorViewModel KnobEditor() =>
        new([Shared, Mode, Name], new ActionSettings(new Dictionary<string, string> { ["mode@rotate-left"] = "c", ["name@rotate-left"] = "Left" }), Knob);

    [Test]
    public void Constructor_AKnob_ShowsAGroupOfFieldsPerInput()
    {
        // Act
        var editor = KnobEditor();

        // Assert
        Assert.That(editor.Fields.Select(field => field.Definition.Key), Is.EqualTo(new[] { "shared", "mode@press", "name@press", "mode@rotate-left", "name@rotate-left", "mode@rotate-right", "name@rotate-right" }));
        Assert.That(editor.Fields.Select(field => field.SectionTitle), Is.EqualTo(new[] { null, "When pressed", null, "When turned left", null, "When turned right", null }));
        Assert.That(editor.Fields.Select(field => field.Value), Is.EqualTo(new[] { "x", "a", "", "c", "Left", "c", "" }));
    }

    [Test]
    public void IsVisible_TheNameField_OnlyShowsWhereTheModeAsksForIt()
    {
        // Act
        var editor = KnobEditor();

        // Assert
        Assert.That(editor.Fields.Select(field => field.IsVisible), Is.EqualTo(new[] { true, true, false, true, true, true, true }));
    }

    [Test]
    public void IsVisible_TheNameField_FollowsTheMode()
    {
        // Arrange
        var editor = KnobEditor();

        // Act
        editor.Fields[1].Value = "c";
        editor.Fields[3].Value = "a";

        // Assert
        Assert.That(editor.Fields[2].IsVisible, Is.True);
        Assert.That(editor.Fields[4].IsVisible, Is.False);
    }

    [Test]
    public void ToSettings_AfterEditingTheGroups_StoresAValuePerInput()
    {
        // Arrange
        var editor = KnobEditor();
        editor.Fields[1].Value = "c";
        editor.Fields[3].Value = "a";

        // Act
        var saved = editor.ToSettings();

        // Assert
        Assert.That(saved.ForInput([Mode], ControlInputs.Press).Get(ModeKey), Is.EqualTo("c"));
        Assert.That(saved.ForInput([Mode], ControlInputs.RotateLeft).Get(ModeKey), Is.EqualTo("a"));
        Assert.That(saved.ForInput([Mode], ControlInputs.RotateRight).Get(ModeKey), Is.EqualTo("c"));
    }

    [Test]
    public void ToSettings_OpeningTheEditorWithoutChanges_StoresNoDefaults()
    {
        // Arrange
        var editor = new SettingsEditorViewModel([Shared, Mode], ActionSettings.Empty, InputCapabilities.Press);

        // Act
        var saved = editor.ToSettings();

        // Assert
        Assert.That(editor.Fields.Select(static field => field.Value), Is.EqualTo(new[] { "x", "a" }));
        Assert.That(saved, Is.Empty);
    }

    [Test]
    public void ToSettings_AChangedValue_IsStored()
    {
        // Arrange
        var editor = new SettingsEditorViewModel([Shared, Mode], ActionSettings.Empty, InputCapabilities.Press);
        editor.Fields[1].Value = "b";

        // Act
        var saved = editor.ToSettings();

        // Assert
        Assert.That(saved.Select(static pair => (pair.Key, pair.Value)), Is.EqualTo(new[] { (ModeKey, "b") }));
    }

    [Test]
    public void ToSettings_AStoredValueThatIsTheDefault_StaysStored()
    {
        // Arrange (kept explicitly: a plugin update that changes its default doesn't change what the user chose)
        var editor = new SettingsEditorViewModel([Shared, Mode], new ActionSettings(new Dictionary<string, string> { [ModeKey] = "a" }), InputCapabilities.Press);

        // Act
        var saved = editor.ToSettings();

        // Assert
        Assert.That(saved.Select(static pair => (pair.Key, pair.Value)), Is.EqualTo(new[] { (ModeKey, "a") }));
    }

    private static ControlBinding BindingWithMode(string mode) => new(
        "key.0",
        ProfileActionKind.Single,
        new ActionReference("p", "P", "a", "A", InputCapabilities.Press, OutputCapabilities.None, new ActionSettings(new Dictionary<string, string> { [ModeKey] = mode })),
        new ControlAppearance(),
        new List<MultiActionStep>());

    [Test]
    public void SameBinding_TwoBindingsBuiltTheSameWay_AreTheSameEvenWithSeparateStepLists()
    {
        // Act
        var same = MainViewModel.SameBinding(BindingWithMode("a"), BindingWithMode("a"));

        // Assert
        Assert.That(ReferenceEquals(BindingWithMode("a").Steps, BindingWithMode("a").Steps), Is.False);
        Assert.That(same, Is.True);
    }

    [Test]
    public void SameBinding_BindingsThatDifferInASetting_AreNotTheSame()
    {
        // Act
        var same = MainViewModel.SameBinding(BindingWithMode("a"), BindingWithMode("b"));

        // Assert
        Assert.That(same, Is.False);
    }

    [Test]
    public void SameBinding_ABindingAndNothing_AreNotTheSame()
    {
        // Act
        var same = MainViewModel.SameBinding(BindingWithMode("a"), null);

        // Assert
        Assert.That(same, Is.False);
    }

    [Test]
    public void Constructor_AControlWithOneInput_KeepsOneFieldPerSettingWithoutGroups()
    {
        // Act
        var editor = new SettingsEditorViewModel([Mode, Name], ActionSettings.Empty, InputCapabilities.Press);

        // Assert
        Assert.That(editor.Fields.Select(field => field.Definition.Key), Is.EqualTo(new[] { ModeKey, "name" }));
        Expect.All(editor.Fields, field => Assert.That(field.SectionTitle, Is.Null));
        Assert.That(editor.Fields[1].IsVisible, Is.False);
    }

    private sealed class RecordingHost : IActionHost
    {
        public List<string> Calls { get; } = [];

        public void RequestRender(string deviceId)
        {
        }

        public void ActivateProfile(string deviceId, string profileIdOrName) => Calls.Add($"profile:{profileIdOrName}");

        public void ActivateRelativeProfile(string deviceId, int offset) => Calls.Add($"relative:{offset}");
    }
}

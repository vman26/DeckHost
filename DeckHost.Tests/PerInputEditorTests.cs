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

    [Test]
    public void Switch_Profile_on_a_knob_does_what_each_input_is_set_to()
    {
        using var store = new VariableStore();
        var host = new RecordingHost();
        var router = new ActionRouter(store, SystemPluginSet.Create());
        var reference = new ActionReference(SystemPluginSet.BuiltInPluginId, "Built-ins", "builtin.switch-profile", "Switch Profile", InputCapabilities.None, OutputCapabilities.None,
            new ActionSettings(new Dictionary<string, string>
            {
                ["mode@press"] = "specific",
                ["profile@press"] = "Gaming",
                ["mode@rotate-right"] = "none",
            }));
        var profile = new DeviceProfile("profile", "Profile", DeviceId, [new ControlBinding("encoder.0", ProfileActionKind.Single, reference, new ControlAppearance(), [])]);

        router.Dispatch(profile, new DeviceInputEvent(DeviceId, "encoder.0", InputCapabilities.Press, 1), host);
        router.Dispatch(profile, new DeviceInputEvent(DeviceId, "encoder.0", InputCapabilities.Release, 0), host);
        router.Dispatch(profile, new DeviceInputEvent(DeviceId, "encoder.0", InputCapabilities.Rotate, -1), host); // default: previous
        router.Dispatch(profile, new DeviceInputEvent(DeviceId, "encoder.0", InputCapabilities.Rotate, 1), host); // set to do nothing

        Assert.That(host.Calls, Is.EqualTo(new[] { "profile:Gaming", "relative:-1" }));
    }

    [Test]
    public void Switch_Profile_bindings_from_before_keep_working()
    {
        // Saved before per-input settings: press used the mode, turning stepped by direction.
        using var store = new VariableStore();
        var host = new RecordingHost();
        var router = new ActionRouter(store, SystemPluginSet.Create());
        var reference = new ActionReference(SystemPluginSet.BuiltInPluginId, "Built-ins", "builtin.switch-profile", "Switch Profile", InputCapabilities.None, OutputCapabilities.None,
            new ActionSettings(new Dictionary<string, string> { ["mode"] = "previous" }));
        var profile = new DeviceProfile("profile", "Profile", DeviceId, [new ControlBinding("encoder.0", ProfileActionKind.Single, reference, new ControlAppearance(), [])]);

        router.Dispatch(profile, new DeviceInputEvent(DeviceId, "encoder.0", InputCapabilities.Press, 1), host);
        router.Dispatch(profile, new DeviceInputEvent(DeviceId, "encoder.0", InputCapabilities.Rotate, 1), host);
        router.Dispatch(profile, new DeviceInputEvent(DeviceId, "encoder.0", InputCapabilities.Rotate, -1), host);

        Assert.That(host.Calls, Is.EqualTo(new[] { "relative:-1", "relative:1", "relative:-1" }));
    }

    [Test]
    public void The_editor_shows_a_group_per_input_on_a_knob()
    {
        var editor = new SettingsEditorViewModel([Shared, Mode, Name], new ActionSettings(new Dictionary<string, string> { ["mode@rotate-left"] = "c", ["name@rotate-left"] = "Left" }), Knob);

        Assert.That(editor.Fields.Select(field => field.Definition.Key), Is.EqualTo(new[] { "shared", "mode@press", "name@press", "mode@rotate-left", "name@rotate-left", "mode@rotate-right", "name@rotate-right" }));
        Assert.That(editor.Fields.Select(field => field.SectionTitle), Is.EqualTo(new[] { null, "When pressed", null, "When turned left", null, "When turned right", null }));
        Assert.That(editor.Fields.Select(field => field.Value), Is.EqualTo(new[] { "x", "a", "", "c", "Left", "c", "" }));

        // The name only shows where the mode asks for it, and follows the mode.
        Assert.That(editor.Fields.Select(field => field.IsVisible), Is.EqualTo(new[] { true, true, false, true, true, true, true }));
        editor.Fields[1].Value = "c";
        Assert.That(editor.Fields[2].IsVisible, Is.True);
        editor.Fields[3].Value = "a";
        Assert.That(editor.Fields[4].IsVisible, Is.False);

        var saved = editor.ToSettings();
        Assert.That(saved.ForInput([Mode], ControlInputs.Press).Get("mode"), Is.EqualTo("c"));
        Assert.That(saved.ForInput([Mode], ControlInputs.RotateLeft).Get("mode"), Is.EqualTo("a"));
        Assert.That(saved.ForInput([Mode], ControlInputs.RotateRight).Get("mode"), Is.EqualTo("c"));
    }

    [Test]
    public void Opening_the_editor_stores_no_defaults_that_were_never_set()
    {
        var editor = new SettingsEditorViewModel([Shared, Mode], ActionSettings.Empty, InputCapabilities.Press);

        Assert.That(editor.Fields.Select(static field => field.Value), Is.EqualTo(new[] { "x", "a" }));
        Assert.That(editor.ToSettings(), Is.Empty);

        editor.Fields[1].Value = "b";
        Assert.That(editor.ToSettings().Select(static pair => (pair.Key, pair.Value)), Is.EqualTo(new[] { ("mode", "b") }));
    }

    [Test]
    public void A_stored_value_stays_stored_even_when_it_is_the_default()
    {
        // Kept explicitly: a plugin update that changes its default doesn't change what the user chose.
        var editor = new SettingsEditorViewModel([Shared, Mode], new ActionSettings(new Dictionary<string, string> { ["mode"] = "a" }), InputCapabilities.Press);

        Assert.That(editor.ToSettings().Select(static pair => (pair.Key, pair.Value)), Is.EqualTo(new[] { ("mode", "a") }));
    }

    [Test]
    public void A_binding_rebuilt_by_the_editor_without_changes_is_the_same_binding()
    {
        static ControlBinding Make(string mode) => new(
            "key.0",
            ProfileActionKind.Single,
            new ActionReference("p", "P", "a", "A", InputCapabilities.Press, OutputCapabilities.None, new ActionSettings(new Dictionary<string, string> { ["mode"] = mode })),
            new ControlAppearance(),
            new List<MultiActionStep>());

        Assert.That(ReferenceEquals(Make("a").Steps, Make("a").Steps), Is.False);
        Assert.That(MainViewModel.SameBinding(Make("a"), Make("a")), Is.True);
        Assert.That(MainViewModel.SameBinding(Make("a"), Make("b")), Is.False);
        Assert.That(MainViewModel.SameBinding(Make("a"), null), Is.False);
    }

    [Test]
    public void The_editor_keeps_one_field_on_a_control_with_one_input()
    {
        var editor = new SettingsEditorViewModel([Mode, Name], ActionSettings.Empty, InputCapabilities.Press);

        Assert.That(editor.Fields.Select(field => field.Definition.Key), Is.EqualTo(new[] { "mode", "name" }));
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

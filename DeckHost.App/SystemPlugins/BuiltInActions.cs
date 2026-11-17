using DeckHost.Contracts;
using DeckHost.Contracts.Enums;
using DeckHost.Contracts.Interfaces;
using DeckHost.Contracts.Models;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace DeckHost.App.SystemPlugins;

/// <summary>Runs its steps in order; the steps are edited in the Customize window.</summary>
internal sealed class MultiActionAction() : DeckActionBase(
    "builtin.multi-action",
    "Multi Action",
    InputCapabilities.None,
    OutputCapabilities.None,
    "Runs several actions in order. Add steps in Customize, or drop actions onto the control.")
{
    public override bool CanBeMultiActionStep => false;

    protected override string? Icon(IRenderContext context) => Icons.Stack;

    protected override (uint From, uint To) TileColors => Palette.Multi;
}

/// <summary>
/// Two sets of steps; each press runs the next set (a toggle: on, then off). Shows which set the
/// next press runs: its own title per set, else 1 or 2.
/// </summary>
internal sealed class MultiActionSwitchAction() : DeckActionBase(
    "builtin.multi-action-switch",
    "Multi Action Switch",
    InputCapabilities.None,
    OutputCapabilities.None,
    "Two sets of actions: the first press runs the first set, the next press the second, and so on. For toggles such as mute and unmute.")
{
    private const string FirstTitleKey = "title1";
    private const string SecondTitleKey = "title2";

    public override bool CanBeMultiActionStep => false;

    protected override IReadOnlyList<ActionSettingDefinition> OwnSettings { get; } =
    [
        new(FirstTitleKey, "Title before the first press", ActionSettingKind.Text, Placeholder: "1"),
        new(SecondTitleKey, "Title before the second press", ActionSettingKind.Text, Placeholder: "2"),
    ];

    protected override string Title(IRenderContext context)
    {
        var next = context.Variables.TryGetValue<string>(VariableKeys.For(context.DeviceId, context.ControlId, VariableKeys.SwitchState), out var state) && state == StepGroups.Second
            ? (SecondTitleKey, "2")
            : (FirstTitleKey, "1");
        return context.Settings.Get(next.Item1) is { Length: > 0 } title ? title : next.Item2;
    }

    protected override string? Subtitle(IRenderContext context) => null;

    protected override string? Icon(IRenderContext context) => Icons.Toggle;

    protected override (uint From, uint To) TileColors => Palette.Multi;

    protected override string? Caption(IRenderContext context) => Title(context);
}

/// <summary>Different steps for a press, a double press and holding the key down.</summary>
internal sealed class KeyLogicAction() : DeckActionBase(
    "builtin.key-logic",
    "Key Logic",
    InputCapabilities.None,
    OutputCapabilities.None,
    "Up to three things on one key: one for a press, one for a double press and one for holding it down.")
{
    public override bool CanBeMultiActionStep => false;

    protected override string? Icon(IRenderContext context) => Icons.KeyLogic;

    protected override (uint From, uint To) TileColors => Palette.Multi;
}

/// <summary>A visual workflow: nodes linked on a canvas (triggers, actions, waits, conditions, loops).</summary>
internal sealed class WorkflowAction() : DeckActionBase(
    ActionId,
    "Workflow",
    InputCapabilities.None,
    OutputCapabilities.None,
    "Build what the control does from linked nodes: what starts it (press, tap, double press, hold, turn), actions, waits, If (a variable, the app in front, the time), toggles, repeats and variables.")
{
    public const string ActionId = "builtin.workflow";

    public override bool CanBeMultiActionStep => false;

    protected override string? Icon(IRenderContext context) => Icons.Workflow;

    protected override (uint From, uint To) TileColors => Palette.Multi;
}

/// <summary>Shows the value of a workflow variable (set by a Set variable node).</summary>
internal sealed class ShowVariableAction() : DeckActionBase(
    "builtin.show-variable",
    "Show Variable",
    InputCapabilities.None,
    OutputCapabilities.Text,
    "Shows a workflow variable's value; workflows set it with a Set variable node.")
{
    private const string NameKey = "name";
    private const string EmptyKey = "empty";

    protected override string? PaletteIcon => Icons.Variable;

    protected override IReadOnlyList<ActionSettingDefinition> OwnSettings { get; } =
    [
        new(NameKey, "Variable", ActionSettingKind.Variable, Placeholder: "e.g. mode"),
        new(EmptyKey, "Shown while it's empty", Placeholder: "–"),
    ];

    public override bool CanBeMultiActionStep => false;

    // Workflows on other controls change it, so look again now and then.
    public override TimeSpan? RefreshInterval => TimeSpan.FromMilliseconds(500);

    public override void Render(IRenderContext context)
    {
        var name = context.Settings.Get(NameKey);
        var value = name.Length > 0 && context.Variables.TryGetValue<string>(VariableKeys.UserVariable(name), out var stored) && !string.IsNullOrEmpty(stored)
            ? stored
            : context.Settings.Get(EmptyKey, "–");
        context.DrawReading(ResolveDisplayText(context, name.Length > 0 ? name : Name), value, null, SecondaryTextColor, ResolveIcon(context, Icons.Variable));
    }
}

/// <summary>
/// Used internally when a control only shows text or an image. Not in the action list: setting
/// the appearance of an empty control creates it automatically.
/// </summary>
internal sealed class DisplayContentAction() : DeckActionBase(
    "builtin.display-content",
    "Display Content",
    InputCapabilities.None,
    OutputCapabilities.None,
    "Shows the control's text and image.")
{
    public override bool ShowInPalette => false;

    public override bool CanBeMultiActionStep => false;

    public override bool ShowsAppearanceOnly => true;

    protected override string Title(IRenderContext context) => string.Empty;

    protected override string? Subtitle(IRenderContext context) => null;
}

internal sealed class SwitchProfileAction() : DeckActionBase(
    "builtin.switch-profile",
    "Switch Profile",
    InputCapabilities.None,
    OutputCapabilities.None,
    "Switches the device to another profile. On a knob each input can do something else: for example pressed a specific profile, turned left the previous one, turned right the next.")
{
    private const string ModeKey = "mode";
    private const string ProfileKey = "profile";
    private const string Next = "next";
    private const string Previous = "previous";
    private const string Specific = "specific";
    private const string Nothing = "none";

    protected override IReadOnlyList<ActionSettingDefinition> OwnSettings { get; } =
    [
        new(ModeKey, "Switch to", ActionSettingKind.Choice, Next, Options:
        [
            new(Next, "Next profile"),
            new(Previous, "Previous profile"),
            new(Specific, "A specific profile"),
            new(Nothing, "Do nothing"),
        ])
        {
            // Each input its own choice; turning and swiping step through the profiles by direction
            // unless set otherwise, and holding a key does nothing extra.
            PerInput = true,
            InputDefaults = new Dictionary<string, string>
            {
                [ControlInputs.LongPress] = Nothing,
                [ControlInputs.RotateLeft] = Previous,
                [ControlInputs.RotateRight] = Next,
                [ControlInputs.SwipeLeft] = Previous,
                [ControlInputs.SwipeRight] = Next,
            },
        },
        new(ProfileKey, "Profile name", Description: "The profile's name as shown in the profile list.")
        {
            PerInput = true,
            ShowWhen = new(ModeKey, Specific),
        },
    ];

    protected override void Run(ActionContext context)
    {
        // The host has already picked this input's choice (see ActionSettingDefinition.PerInput).
        switch (context.Settings.Get(ModeKey, Next))
        {
            case Nothing:
                break;
            case Previous:
                context.Host.ActivateRelativeProfile(context.DeviceId, -1);
                break;
            case Specific:
                var profile = context.Settings.Get(ProfileKey);
                if (!string.IsNullOrWhiteSpace(profile))
                {
                    context.Host.ActivateProfile(context.DeviceId, profile);
                }

                break;
            default:
                context.Host.ActivateRelativeProfile(context.DeviceId, 1);
                break;
        }
    }

    protected override string Title(IRenderContext context)
    {
        // What a press does; on a knob without a press choice, that it switches profiles.
        var settings = context.Settings.ForInput(Settings, ControlInputs.Press);
        return settings.Get(ModeKey, Next) switch
        {
            Previous => "Prev Profile",
            Specific => settings.Get(ProfileKey, "Profile"),
            Nothing => "Profiles",
            _ => "Next Profile",
        };
    }

    protected override string? Subtitle(IRenderContext context) => null;

    protected override string? Icon(IRenderContext context) => Icons.SwitchProfile;

    protected override (uint From, uint To) TileColors => Palette.Profiles;

    protected override string? Caption(IRenderContext context) => Title(context);
}

internal sealed class ClockAction() : DeckActionBase(
    "builtin.clock",
    "Clock",
    InputCapabilities.None,
    OutputCapabilities.Text,
    "Shows the current time and date.")
{
    protected override string? PaletteIcon => Icons.Clock;

    private const string FormatKey = "format";

    protected override IReadOnlyList<ActionSettingDefinition> OwnSettings { get; } =
    [
        new(FormatKey, "Format", ActionSettingKind.Choice, "HH:mm", Options:
        [
            new("HH:mm", "24-hour (14:05)"),
            new("HH:mm:ss", "24-hour with seconds"),
            new("h:mm tt", "12-hour (2:05 PM)"),
            new("h:mm:ss tt", "12-hour with seconds"),
        ]),
    ];

    public override bool CanBeMultiActionStep => false;

    public override TimeSpan? RefreshInterval => TimeSpan.FromSeconds(1);

    public override void Render(IRenderContext context)
    {
        var now = DateTime.Now;
        if (context.IsCompact)
        {
            DrawCompact(context, now.ToString(context.Settings.Get(FormatKey, "HH:mm"), CultureInfo.CurrentCulture));
            return;
        }

        DrawTileBackground(context, Palette.Clock);
        var label = ResolveDisplayText(context, now.ToString("ddd d MMM", CultureInfo.CurrentCulture));
        context.DrawCenteredText(now.ToString(context.Settings.Get(FormatKey, "HH:mm"), CultureInfo.CurrentCulture), context.Width / 2d, context.Height * 0.44d, Math.Max(14f, context.Height * 0.24f));
        context.DrawCenteredText(label, context.Width / 2d, context.Height * 0.74d, SubtitleSize(context), 0xFFC7D2FE);
    }
}

internal sealed class CounterAction() : DeckActionBase(
    "builtin.counter",
    "Counter",
    InputCapabilities.None,
    OutputCapabilities.Text,
    "Counts presses (or knob steps). Long-press a key to reset it to zero.")
{
    protected override string? PaletteIcon => Icons.Counter;

    private const string StepKey = "step";

    protected override IReadOnlyList<ActionSettingDefinition> OwnSettings { get; } =
    [
        new(StepKey, "Step", ActionSettingKind.Number, "1", Description: "Added per press or knob step.") { Range = new(1, 100) },
    ];

    public override bool CanBeMultiActionStep => false;

    protected override void Run(ActionContext context)
    {
        var key = VariableKeys.For(context.DeviceId, context.ControlId, VariableKeys.Counter);
        context.Variables.TryGetValue<int>(key, out var count);
        var step = context.Settings.GetInt(StepKey, 1);

        count = context.EventType switch
        {
            InputCapabilities.LongPress => 0,
            InputCapabilities.Rotate or InputCapabilities.Swipe => count + (Math.Sign(context.Value) * step),
            _ => count + step,
        };

        context.Variables.Publish(key, count);
    }

    public override void Render(IRenderContext context)
    {
        context.Variables.TryGetValue<int>(VariableKeys.For(context.DeviceId, context.ControlId, VariableKeys.Counter), out var count);
        context.DrawReading(ResolveDisplayText(context, Name), count.ToString(CultureInfo.CurrentCulture), null, SecondaryTextColor, ResolveIcon(context, Icons.Counter));
    }
}

/// <summary>Shows the logical control id and the SDK/hardware id, to check the key mapping.</summary>
internal sealed class KeyIdDiagnosticAction() : DeckActionBase(
    "deckhost.diagnostic.key-id",
    "Key ID (Diagnostic)",
    InputCapabilities.None,
    OutputCapabilities.Text,
    "Shows which control and SDK id this is, to check the key layout.")
{
    protected override string? PaletteIcon => Icons.Chip;

    public override bool CanBeMultiActionStep => false;

    public override void Render(IRenderContext context)
    {
        context.Variables.TryGetValue<string>(VariableKeys.For(context.DeviceId, context.ControlId, VariableKeys.HardwareId), out var hardwareId);
        if (context.IsCompact)
        {
            DrawCompact(context, $"{context.ControlId} · SDK {hardwareId ?? "?"}");
            return;
        }

        var size = Math.Max(11f, context.Height * 0.16f);
        context.DrawCenteredText(context.ControlId, context.Width / 2d, context.Height * 0.38d, size);
        context.DrawCenteredText($"SDK {hardwareId ?? "?"}", context.Width / 2d, context.Height * 0.66d, size, 0xFFFDE68A);
    }
}

using DeckHost.Contracts;
using DeckHost.Contracts.Enums;
using DeckHost.Contracts.Interfaces;
using DeckHost.Contracts.Models;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace DeckHost.App.SystemPlugins;

/// <summary>
/// Shared behaviour for the system actions: records each activation, runs the action, and
/// draws a title (the binding's custom text, else the action's own title) plus a status line.
/// Runs on the host's worker thread; slow work goes through <see cref="RunInBackground"/>.
/// </summary>
internal abstract class DeckActionBase(
    string id,
    string name,
    InputCapabilities requiredInputs,
    OutputCapabilities requiredOutputs,
    string? description = null) : IDeckAction
{
    protected const uint SecondaryTextColor = 0xFF93C5FD;
    protected const uint ErrorTextColor = 0xFFFCA5A5;

    public const string TriggerKey = "trigger";

    private static readonly ActionSettingDefinition TriggerSetting = new(
        TriggerKey,
        "Run when",
        ActionSettingKind.Choice,
        "press",
        "Which input on this control runs the action.",
        [
            new("press", "Pressed") { Requires = InputCapabilities.Press },
            new("release", "Released") { Requires = InputCapabilities.Release },
            new("longpress", "Held down (long press)") { Requires = InputCapabilities.LongPress },
            new("rotate-left", "Turned left") { Requires = InputCapabilities.Rotate },
            new("rotate-right", "Turned right") { Requires = InputCapabilities.Rotate },
            new("swipe-left", "Swiped left") { Requires = InputCapabilities.Swipe },
            new("swipe-right", "Swiped right") { Requires = InputCapabilities.Swipe },
        ]);

    public string Id { get; } = id;

    public string Name { get; } = name;

    /// <summary>One-shot actions accept every input and filter by their "Run when" setting instead.</summary>
    public InputCapabilities RequiredInputs => UsesTrigger ? InputCapabilities.None : requiredInputs;

    public OutputCapabilities RequiredOutputs { get; } = requiredOutputs;

    public string? Description { get; } = description;

    public IReadOnlyList<ActionSettingDefinition> Settings => UsesTrigger ? [TriggerSetting, .. OwnSettings] : OwnSettings;

    protected virtual IReadOnlyList<ActionSettingDefinition> OwnSettings => [];

    /// <summary>Actions that fire once per press get a configurable trigger (press, release, long press, ...).</summary>
    private bool UsesTrigger => requiredInputs == InputCapabilities.Press;

    public virtual bool ShowInPalette => true;

    public virtual bool CanBeMultiActionStep => true;

    public virtual TimeSpan? RefreshInterval => null;

    public virtual bool ShowsAppearanceOnly => false;

    /// <summary>Most actions fire on press; a release only ends the press.</summary>
    protected virtual bool RunOnRelease => false;

    public void Execute(ActionContext context)
    {
        if (UsesTrigger)
        {
            if (!MatchesTrigger(context))
            {
                return;
            }
        }
        else if (context.EventType == InputCapabilities.Release && !RunOnRelease)
        {
            return;
        }

        RecordActivation(context);
        Run(context);
    }

    public virtual void Render(IRenderContext context)
    {
        if (ResolveIcon(context, Icon(context)) is { } icon)
        {
            RenderTile(context, icon);
            return;
        }

        // The label follows the user's text style (color, font, alignment, wrapping) from Customize.
        var title = ResolveDisplayText(context, Title(context));
        var variables = context.Variables;
        var hasError = variables.TryGetValue<string>(VariableKeys.For(context.DeviceId, context.ControlId, VariableKeys.LastError), out var error) &&
                       !string.IsNullOrWhiteSpace(error);

        if (context.IsCompact)
        {
            // A strip under another control's action: one line, the error instead of the title.
            DrawCompact(context, hasError ? error! : CompactText(context, title), hasError ? ErrorTextColor : 0xFFFFFFFF);
            return;
        }

        if (!string.IsNullOrWhiteSpace(title))
        {
            context.DrawLabel(title, TextVerticalAlignment.Middle, TitleSize(context));
        }

        if (hasError)
        {
            context.DrawCenteredText(error!, context.Width / 2d, context.Height * 0.76d, SubtitleSize(context), ErrorTextColor);
            return;
        }

        var subtitle = Subtitle(context);
        if (!string.IsNullOrWhiteSpace(subtitle))
        {
            context.DrawCenteredText(subtitle, context.Width / 2d, context.Height * 0.76d, SubtitleSize(context), SecondaryTextColor);
        }
    }

    protected virtual void Run(ActionContext context)
    {
    }

    /// <summary>
    /// The action's icon (<see cref="Icons"/>). With one, the action is drawn as an icon on a
    /// colored tile (<see cref="TileColors"/>) instead of as text, like the keys of a Stream Deck.
    /// </summary>
    protected virtual string? Icon(IRenderContext context) => null;

    /// <summary>
    /// The icon the user picked for this control (Customize → Icon), else <paramref name="fallback"/>
    /// (the action's own). Actions that draw readouts pass their icon through this too.
    /// </summary>
    protected static string? ResolveIcon(IRenderContext context, string? fallback) =>
        context.Variables.TryGetValue<string>(VariableKeys.For(context.DeviceId, context.ControlId, VariableKeys.AppearanceIcon), out var custom) &&
        !string.IsNullOrWhiteSpace(custom)
            ? custom
            : fallback;

    /// <summary>The gray tile of actions without colors of their own (Display Content).</summary>
    private static readonly (uint From, uint To) NeutralTile = (0xFF64748B, 0xFF1E293B);

    /// <summary>The tile's gradient, top left to bottom right.</summary>
    protected virtual (uint From, uint To) TileColors => NeutralTile;

    /// <summary>
    /// Text under the icon on a key when the user set none; null when the icon says it all (play,
    /// lock). On a touch zone and in a strip the title is shown instead.
    /// </summary>
    protected virtual string? Caption(IRenderContext context) => null;

    /// <summary>
    /// The colored tile, for actions that draw their own content on it (a clock): only on a key whose
    /// user chose the tile (Customize → Background), and without a color or picture of their own.
    /// Not on a touch zone, which stays one surface.
    /// </summary>
    protected static void DrawTileBackground(IRenderContext context, (uint From, uint To) colors)
    {
        if (context.IsCompact || context.Width > context.Height * 1.3 || !WantsTile(context))
        {
            return;
        }

        foreach (var suffix in new[] { VariableKeys.AppearanceBackground, VariableKeys.AppearanceImage })
        {
            if (context.Variables.TryGetValue<string>(VariableKeys.For(context.DeviceId, context.ControlId, suffix), out var value) && !string.IsNullOrWhiteSpace(value))
            {
                return;
            }
        }

        FillTile(context, colors);
    }

    /// <summary>The tile on a key: the user's colors and corners (Customize → Background → the action's tile) over the action's own.</summary>
    private static void FillTile(IRenderContext context, (uint From, uint To) actionColors)
    {
        var style = context.Variables.TryGetValue<TileStyle>(VariableKeys.For(context.DeviceId, context.ControlId, VariableKeys.AppearanceTileStyle), out var tile) ? tile : null;
        var from = ParseColor(style?.From) ?? actionColors.From;
        var to = ParseColor(style?.To) ?? (ParseColor(style?.From) is { } single ? single : actionColors.To);
        var corner = Math.Clamp(style?.CornerPercent ?? TileStyle.DefaultCornerPercent, 0, 50) / 100d;
        var inset = Math.Max(2d, context.Width * 0.035d);
        var size = context.Width - (2 * inset);
        context.FillGradientRect(inset, inset, size, context.Height - (2 * inset), size * corner, from, to);
    }

    private static uint? ParseColor(string? text)
    {
        var hex = text?.Trim().TrimStart('#');
        return hex is { Length: 6 or 8 } &&
               uint.TryParse(hex, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out var value)
            ? hex.Length == 6 ? 0xFF000000 | value : value
            : null;
    }

    private void RenderTile(IRenderContext context, string icon)
    {
        var variables = context.Variables;
        string? Appearance(string suffix) =>
            variables.TryGetValue<string>(VariableKeys.For(context.DeviceId, context.ControlId, suffix), out var value) && !string.IsNullOrWhiteSpace(value)
                ? value
                : null;

        var customText = Appearance(VariableKeys.AppearanceText);
        var errorKey = VariableKeys.For(context.DeviceId, context.ControlId, VariableKeys.LastError);
        var error = variables.TryGetValue<string>(errorKey, out var lastError) && !string.IsNullOrWhiteSpace(lastError) ? lastError : null;

        if (Appearance(VariableKeys.AppearanceImage) is not null && !context.IsCompact)
        {
            // The user's own picture is the icon: only their text (or the error) goes on top.
            if (error is not null)
            {
                context.DrawCenteredText(error, context.Width / 2d, context.Height * 0.84d, SubtitleSize(context), ErrorTextColor);
            }
            else if (customText is not null)
            {
                context.DrawLabel(customText, TextVerticalAlignment.Bottom, SubtitleSize(context));
            }

            return;
        }

        var wide = context.IsCompact || context.Width > context.Height * 1.3;
        var caption = error ?? customText ?? (wide ? CompactText(context, Title(context)) : Caption(context));

        // Transparent by default: no tile, and the icon in the action's color (white for gray ones,
        // which would look dim). The tile only when the user chose it; on their own color, white.
        uint from = 0, to = 0, iconColor;
        if (Appearance(VariableKeys.AppearanceBackground) is not null)
        {
            iconColor = 0xFFFFFFFF;
        }
        else if (!wide && WantsTile(context))
        {
            // The tile is drawn here with the user's colors and corners; DrawTile only adds the icon.
            FillTile(context, TileColors);
            iconColor = 0xFFFFFFFF;
        }
        else
        {
            iconColor = TileColors == NeutralTile ? 0xFFFFFFFF : TileColors.From;
        }

        // A text color of the user's colors the icon too, whatever the background.
        iconColor = context.TextColorOr(iconColor);

        if (error is not null && !wide)
        {
            context.DrawTile(icon, from, to, null, iconColor);
            context.DrawCenteredText(error, context.Width / 2d, context.Height * 0.84d, SubtitleSize(context), ErrorTextColor);
            return;
        }

        context.DrawTile(icon, from, to, caption, iconColor);
    }

    /// <summary>The user chose the action's tile as the control's background (<see cref="ControlAppearance.ShowTile"/>).</summary>
    private static bool WantsTile(IRenderContext context) =>
        context.Variables.TryGetValue<string>(VariableKeys.For(context.DeviceId, context.ControlId, VariableKeys.AppearanceTile), out var tile) && tile == "true";

    private static bool MatchesTrigger(ActionContext context) => context.Settings.Get(TriggerKey, "press") switch
    {
        "release" => context.EventType == InputCapabilities.Release,
        "longpress" => context.EventType == InputCapabilities.LongPress,
        "rotate-left" => context.EventType == InputCapabilities.Rotate && context.Value < 0,
        "rotate-right" => context.EventType == InputCapabilities.Rotate && context.Value > 0,
        "swipe-left" => context.EventType == InputCapabilities.Swipe && context.Value < 0,
        "swipe-right" => context.EventType == InputCapabilities.Swipe && context.Value > 0,
        _ => context.EventType == InputCapabilities.Press,
    };

    /// <summary>The title shown when the binding has no custom text.</summary>
    protected virtual string Title(IRenderContext context) => Name;

    /// <summary>The single line shown in a strip (<see cref="IRenderContext.IsCompact"/>); by default the title.</summary>
    protected virtual string CompactText(IRenderContext context, string title) => title;

    /// <summary>Draws the one line of a strip, in the user's text style.</summary>
    protected static void DrawCompact(IRenderContext context, string text, uint color = 0xFFFFFFFF)
    {
        if (!string.IsNullOrWhiteSpace(text))
        {
            context.DrawLabel(text, TextVerticalAlignment.Middle, Math.Max(10f, context.Height * 0.46f), color);
        }
    }

    /// <summary>Second line; by default the time the action last ran.</summary>
    protected virtual string? Subtitle(IRenderContext context) =>
        context.Variables.TryGetValue<string>(VariableKeys.For(context.DeviceId, context.ControlId, VariableKeys.LastActionAt), out var lastRun)
            ? lastRun
            : null;

    protected static float TitleSize(IRenderContext context) => Math.Max(12f, context.Height * 0.17f);

    protected static float SubtitleSize(IRenderContext context) => Math.Max(10f, context.Height * 0.12f);

    /// <summary>
    /// Runs slow or blocking work (starting a process, ...) off the host worker. A failure is
    /// shown on the control until the next successful run.
    /// </summary>
    protected static void RunInBackground(ActionContext context, Action work)
    {
        var errorKey = VariableKeys.For(context.DeviceId, context.ControlId, VariableKeys.LastError);
        _ = Task.Run(() =>
        {
            try
            {
                work();
                context.Variables.Publish(errorKey, string.Empty);
            }
            catch (Exception ex)
            {
                context.Variables.Publish(errorKey, ShortError(ex));
            }

            context.Host.RequestRender(context.DeviceId);
        });
    }

    /// <summary>Runs quick work on the worker and shows a failure on the control.</summary>
    protected static void RunGuarded(ActionContext context, Action work)
    {
        var errorKey = VariableKeys.For(context.DeviceId, context.ControlId, VariableKeys.LastError);
        try
        {
            work();
            context.Variables.Publish(errorKey, string.Empty);
        }
        catch (Exception ex)
        {
            context.Variables.Publish(errorKey, ShortError(ex));
        }
    }

    /// <summary>
    /// Runs simulated input (keys, media, volume, locking) on <see cref="Platform.SystemControl"/>'s
    /// queue: in order with other input, off the host worker. A failure is shown on the control
    /// until the next successful run; <paramref name="after"/> runs either way.
    /// </summary>
    protected static void RunQueued(ActionContext context, Action work, Action? after = null)
    {
        var errorKey = VariableKeys.For(context.DeviceId, context.ControlId, VariableKeys.LastError);
        _ = Platform.SystemControl.Enqueue(work).ContinueWith(
            task =>
            {
                context.Variables.Publish(errorKey, task.Exception is { } failure ? ShortError(failure) : string.Empty);
                after?.Invoke();
                context.Host.RequestRender(context.DeviceId);
            },
            TaskScheduler.Default);
    }

    /// <summary>The binding's custom text; no text when it only shows an image; else the fallback.</summary>
    protected static string ResolveDisplayText(IRenderContext context, string fallback)
    {
        var variables = context.Variables;
        if (variables.TryGetValue<string>(VariableKeys.For(context.DeviceId, context.ControlId, VariableKeys.AppearanceText), out var displayText) &&
            !string.IsNullOrWhiteSpace(displayText))
        {
            return displayText;
        }

        if (variables.TryGetValue<string>(VariableKeys.For(context.DeviceId, context.ControlId, VariableKeys.AppearanceImage), out var imagePath) &&
            !string.IsNullOrWhiteSpace(imagePath))
        {
            return string.Empty;
        }

        return fallback;
    }

    private static void RecordActivation(ActionContext context)
    {
        var variables = context.Variables;
        variables.Publish(VariableKeys.For(context.DeviceId, context.ControlId, VariableKeys.LastActionAt), DateTimeOffset.Now.ToString("HH:mm:ss"));

        var countKey = VariableKeys.For(context.DeviceId, context.ControlId, VariableKeys.ExecutionCount);
        variables.Publish(countKey, variables.TryGetValue<int>(countKey, out var executionCount) ? executionCount + 1 : 1);
    }

    private static string ShortError(Exception ex)
    {
        var message = ex.GetBaseException().Message;
        return message.Length > 40 ? message[..40] + "…" : message;
    }
}

internal sealed class SystemPlugin(string id, string name, string description, params IDeckAction[] actions) : IApplicationPlugin
{
    public string Id { get; } = id;

    public string Name { get; } = name;

    public string? Description { get; } = description;

    public string Version => AppInfo.Version;

    public string? Author => "DeckHost";

    public IReadOnlyCollection<IDeckAction> Actions { get; } = actions;
}

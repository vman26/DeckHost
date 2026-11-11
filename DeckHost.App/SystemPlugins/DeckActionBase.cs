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

    public string Id { get; } = id;

    public string Name { get; } = name;

    public InputCapabilities RequiredInputs { get; } = requiredInputs;

    public OutputCapabilities RequiredOutputs { get; } = requiredOutputs;

    public string? Description { get; } = description;

    public virtual IReadOnlyList<ActionSettingDefinition> Settings => [];

    public virtual bool ShowInPalette => true;

    public virtual bool CanBeMultiActionStep => true;

    public virtual TimeSpan? RefreshInterval => null;

    /// <summary>Most actions fire on press; a release only ends the press.</summary>
    protected virtual bool RunOnRelease => false;

    public void Execute(ActionContext context)
    {
        if (context.EventType == InputCapabilities.Release && !RunOnRelease)
        {
            return;
        }

        RecordActivation(context);
        Run(context);
    }

    public virtual void Render(IRenderContext context)
    {
        var title = ResolveDisplayText(context, Title(context));
        if (!string.IsNullOrWhiteSpace(title))
        {
            context.DrawCenteredText(title, context.Width / 2d, context.Height * 0.42d, TitleSize(context));
        }

        var variables = context.Variables;
        if (variables.TryGetValue<string>(VariableKeys.For(context.DeviceId, context.ControlId, VariableKeys.LastError), out var error) &&
            !string.IsNullOrWhiteSpace(error))
        {
            context.DrawCenteredText(error, context.Width / 2d, context.Height * 0.76d, SubtitleSize(context), ErrorTextColor);
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

    /// <summary>The title shown when the binding has no custom text.</summary>
    protected virtual string Title(IRenderContext context) => Name;

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

    protected static void RequireWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Windows only");
        }
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

    public string Version => typeof(SystemPlugin).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";

    public string? Author => "DeckHost";

    public IReadOnlyCollection<IDeckAction> Actions { get; } = actions;
}

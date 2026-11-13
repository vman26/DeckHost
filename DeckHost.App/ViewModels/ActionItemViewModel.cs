using DeckHost.App.SystemPlugins;
using DeckHost.Contracts.Enums;
using DeckHost.Contracts.Interfaces;
using DeckHost.Contracts.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace DeckHost.App.ViewModels;

internal static class EditorActionIds
{
    public const string MultiAction = "builtin.multi-action";
    public const string DisplayContent = "builtin.display-content";
    public const string BuiltInPluginId = SystemPluginSet.BuiltInPluginId;

    public static string Key(string pluginId, string actionId) => $"{pluginId}:{actionId}";
}

public class ActionItemViewModel : ViewModelBase
{
    private ActionItemViewModel(string pluginId, string pluginName, IDeckAction action)
    {
        PluginId = pluginId;
        PluginName = pluginName;
        Action = action;
        Id = action.Id;
        Name = action.Name;
        Description = action.Description;
        SettingDefinitions = action.Settings;
        InputsLabel = CapabilityFormatter.Format(action.RequiredInputs, "Any input");
        OutputsLabel = CapabilityFormatter.Format(action.RequiredOutputs, "No display needed");
    }

    public string PluginId { get; }

    public string PluginName { get; }

    public string Id { get; }

    public string Key => EditorActionIds.Key(PluginId, Id);

    public string Name { get; }

    public string? Description { get; }

    public IDeckAction Action { get; }

    public IReadOnlyList<ActionSettingDefinition> SettingDefinitions { get; }

    public bool HasSettings => SettingDefinitions.Count > 0;

    public string InputsLabel { get; }

    public string OutputsLabel { get; }

    public bool ShowInPalette => Action.ShowInPalette;

    /// <summary>The kind of binding this action makes (Multi Action, Multi Action Switch, Key Logic), or null for a plain action.</summary>
    public ProfileActionKind? ScaffoldKind => PluginId != EditorActionIds.BuiltInPluginId ? null : Id switch
    {
        EditorActionIds.MultiAction => ProfileActionKind.MultiAction,
        "builtin.multi-action-switch" => ProfileActionKind.MultiActionSwitch,
        "builtin.key-logic" => ProfileActionKind.KeyLogic,
        SystemPlugins.WorkflowAction.ActionId => ProfileActionKind.Workflow,
        _ => null,
    };

    public bool IsMultiActionScaffold => ScaffoldKind is not null;

    public bool IsDisplayContentAction => PluginId == EditorActionIds.BuiltInPluginId && Id == EditorActionIds.DisplayContent;

    public bool RequiresOutputCapableControl => IsDisplayContentAction;

    public bool RequiresInteractiveInput => IsMultiActionScaffold;

    public bool CanBeMultiActionStep => Action.CanBeMultiActionStep && !IsMultiActionScaffold && !IsDisplayContentAction;

    public string Summary => Description ?? $"{InputsLabel} / {OutputsLabel}";

    private ListIcon? _icon;

    /// <summary>The icon in the action list and on the drag ghost (made when first shown).</summary>
    public ListIcon Icon => _icon ??= ListIcon.From(
        SafeListIcon(Action),
        Action is DeckActionBase deckHostAction ? deckHostAction.ListIconColor : 0xFFE2E8F0,
        Contracts.Icons.Apps);

    private static string? SafeListIcon(IDeckAction action)
    {
        try
        {
            return action.ListIcon;
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or ArgumentException)
        {
            return null;
        }
    }

    public ActionSettings DefaultSettings => new(SettingDefinitions
        .Where(static definition => !string.IsNullOrEmpty(definition.DefaultValue))
        .Select(static definition => new KeyValuePair<string, string>(definition.Key, definition.DefaultValue)));

    public ActionReference ToActionReference(ActionSettings? settings = null) => new(
        PluginId,
        PluginName,
        Id,
        Name,
        Action.RequiredInputs,
        Action.RequiredOutputs,
        settings is null || settings.Count == 0 ? null : settings);

    /// <summary>Whether the action fits on the control (its inputs and outputs are available there).</summary>
    public bool FitsControl(PhysicalControl control)
    {
        if (RequiresOutputCapableControl && control.Outputs == OutputCapabilities.None && !control.HasLinkedDisplay)
        {
            return false;
        }

        if (RequiresInteractiveInput && control.Inputs == InputCapabilities.None)
        {
            return false;
        }

        // A control without a display of its own (a knob) shows its action on the display it's linked
        // to (the touch zone above it), so actions that show text or a reading (Counter, Key ID, Clock,
        // System Info) fit there too. Stream Deck key actions (a key image) still need a display of their own.
        var outputs = control.HasLinkedDisplay ? control.Outputs | LinkedDisplayOutputs : control.Outputs;
        return (control.Inputs & Action.RequiredInputs) == Action.RequiredInputs &&
               (outputs & Action.RequiredOutputs) == Action.RequiredOutputs;
    }

    /// <summary>What a linked display offers the control that borrows it: text and readouts, drawn by the host.</summary>
    private const OutputCapabilities LinkedDisplayOutputs = OutputCapabilities.Text;

    public static ActionItemViewModel Create(string pluginId, string pluginName, IDeckAction action) => new(pluginId, pluginName, action);
}

internal static class CapabilityFormatter
{
    public static string Format(InputCapabilities capabilities, string emptyText) =>
        capabilities == InputCapabilities.None
            ? emptyText
            : string.Join(", ", Enum.GetValues<InputCapabilities>()
                .Where(flag => flag is not InputCapabilities.None and not InputCapabilities.All && capabilities.HasFlag(flag)));

    public static string Format(OutputCapabilities capabilities, string emptyText) =>
        capabilities == OutputCapabilities.None
            ? emptyText
            : string.Join(", ", Enum.GetValues<OutputCapabilities>()
                .Where(flag => flag is not OutputCapabilities.None and not OutputCapabilities.All && capabilities.HasFlag(flag)));
}

/// <summary>A Multi Action step as shown in the inspector.</summary>
public sealed class StepItemViewModel(ActionItemViewModel action, ActionSettings settings)
{
    public ActionItemViewModel Action { get; } = action;

    public ActionSettings Settings { get; } = settings;

    public string Name => Action.Name;

    public string PluginName => Action.PluginName;
}

internal sealed class EmptyStepCollection : ObservableCollection<StepItemViewModel>
{
    public static EmptyStepCollection Instance { get; } = new();
}

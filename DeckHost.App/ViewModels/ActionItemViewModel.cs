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

    public bool IsMultiActionScaffold => PluginId == EditorActionIds.BuiltInPluginId && Id == EditorActionIds.MultiAction;

    public bool IsDisplayContentAction => PluginId == EditorActionIds.BuiltInPluginId && Id == EditorActionIds.DisplayContent;

    public bool RequiresOutputCapableControl => IsDisplayContentAction;

    public bool RequiresInteractiveInput => IsMultiActionScaffold;

    public bool CanBeMultiActionStep => Action.CanBeMultiActionStep && !IsMultiActionScaffold && !IsDisplayContentAction;

    public string Summary => Description ?? $"{InputsLabel} / {OutputsLabel}";

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

        return (control.Inputs & Action.RequiredInputs) == Action.RequiredInputs &&
               (control.Outputs & Action.RequiredOutputs) == Action.RequiredOutputs;
    }

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

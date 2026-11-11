using DeckHost.Contracts.Enums;
using DeckHost.Contracts.Interfaces;
using DeckHost.Contracts.Models;
using System;
using System.Collections.ObjectModel;
using System.Linq;

namespace DeckHost.App.ViewModels;

internal static class EditorActionIds
{
    public const string MultiAction = "builtin.multi-action";
    public const string DisplayContent = "builtin.display-content";
    public const string BuiltInPluginId = "deckhost.plugin.builtin";

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
        InputsLabel = CapabilityFormatter.Format(action.RequiredInputs, "No input required");
        OutputsLabel = CapabilityFormatter.Format(action.RequiredOutputs, "No output required");
    }

    public string PluginId { get; }

    public string PluginName { get; }

    public string Id { get; }

    public string Key => EditorActionIds.Key(PluginId, Id);

    public string Name { get; }

    public IDeckAction Action { get; }

    public string InputsLabel { get; }

    public string OutputsLabel { get; }

    public bool IsMultiActionScaffold => Id == EditorActionIds.MultiAction;

    public bool IsDisplayContentAction => Id == EditorActionIds.DisplayContent;

    public bool RequiresOutputCapableControl => IsDisplayContentAction;

    public bool RequiresInteractiveInput => IsMultiActionScaffold;

    public bool CanBeMultiActionStep => !IsMultiActionScaffold && !IsDisplayContentAction;

    public string Summary => $"{InputsLabel} / {OutputsLabel}";

    public ActionReference ToActionReference() => new(
        PluginId,
        PluginName,
        Id,
        Name,
        Action.RequiredInputs,
        Action.RequiredOutputs);

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

internal sealed class EmptyActionCollection : ObservableCollection<ActionItemViewModel>
{
    public static EmptyActionCollection Instance { get; } = new();
}

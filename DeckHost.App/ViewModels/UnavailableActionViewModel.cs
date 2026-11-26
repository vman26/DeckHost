using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using DeckHost.App.Profiles;
using DeckHost.Contracts.Models;
using DeckHost.Core.Plugins;

namespace DeckHost.App.ViewModels;

/// <summary>Why a control's action can't run here.</summary>
public enum UnavailableReason
{
    /// <summary>No plugin with the action's plugin id is installed.</summary>
    NotInstalled,

    /// <summary>The plugin is installed but turned off.</summary>
    TurnedOff,

    /// <summary>The plugin is installed and on, but has no such action (another version).</summary>
    ActionMissing,
}

/// <summary>The pure parts: the plugin's name and why the action is unavailable.</summary>
public static class UnavailableAction
{
    /// <summary>The plugin's name as the binding remembers it, else its id.</summary>
    public static string PluginName(ControlBinding binding) =>
        string.IsNullOrWhiteSpace(binding.Action.PluginName) ? binding.Action.PluginId : binding.Action.PluginName;

    /// <summary>The action's name as the binding remembers it, else its id.</summary>
    public static string ActionName(ControlBinding binding) =>
        string.IsNullOrWhiteSpace(binding.Action.ActionName) ? binding.Action.ActionId : binding.Action.ActionName;

    /// <summary>What the canvas says on the control: "Needs the Discord plugin".</summary>
    public static string Describe(ControlBinding binding) => $"Needs the {PluginName(binding)} plugin";

    /// <summary>Whether the action's plugin is missing, turned off, or there without this action.</summary>
    public static UnavailableReason ReasonFor(ControlBinding binding, IEnumerable<PluginEntry> installed)
    {
        var entry = installed.FirstOrDefault(candidate => string.Equals(candidate.Id, binding.Action.PluginId, StringComparison.OrdinalIgnoreCase));
        return entry switch
        {
            null => UnavailableReason.NotInstalled,
            { IsEnabled: false } => UnavailableReason.TurnedOff,
            _ => UnavailableReason.ActionMissing,
        };
    }

    /// <summary>The missing plugin as the store lookup takes it.</summary>
    public static MissingPlugin AsMissingPlugin(ControlBinding binding) =>
        new(binding.Action.PluginId, PluginName(binding), [binding.Action.ActionId]);
}

/// <summary>
/// The card above the editor for a control whose action's plugin isn't there: install it from the store (every key
/// with its actions works again by itself), turn it on, or remove the action. Dropping another action replaces it.
/// </summary>
public sealed partial class UnavailableActionViewModel : ViewModelBase
{
    private readonly PluginCatalog? _catalog;
    private string _status = string.Empty;

    /// <param name="binding">The control's unresolved binding.</param>
    /// <param name="catalog">The installed plugins; null in tests (nothing can be installed or turned on).</param>
    /// <param name="store">The store lookup; one for <paramref name="catalog"/> when null.</param>
    public UnavailableActionViewModel(ControlBinding binding, PluginCatalog? catalog, MissingPluginsViewModel? store = null)
    {
        Binding = binding;
        _catalog = catalog;
        Reason = UnavailableAction.ReasonFor(binding, catalog?.Entries ?? []);
        Store = store ?? new MissingPluginsViewModel(catalog);
    }

    /// <summary>Raised for Remove: the main view model clears the control.</summary>
    public event EventHandler? RemoveRequested;

    public ControlBinding Binding { get; }

    public string ControlId => Binding.ControlId;

    public string PluginId => Binding.Action.PluginId;

    public string PluginName => UnavailableAction.PluginName(Binding);

    public string ActionName => UnavailableAction.ActionName(Binding);

    public UnavailableReason Reason { get; }

    public bool IsNotInstalled => Reason == UnavailableReason.NotInstalled;

    public bool IsTurnedOff => Reason == UnavailableReason.TurnedOff;

    public string Title => Reason switch
    {
        UnavailableReason.NotInstalled => $"“{ActionName}” needs the {PluginName} plugin",
        UnavailableReason.TurnedOff => $"{PluginName} is turned off",
        _ => $"{PluginName} has no “{ActionName}” action",
    };

    public string Message => Reason switch
    {
        UnavailableReason.NotInstalled => "The plugin isn't installed, so this control does nothing yet. Install it and every control with its actions works again by itself. Or drop another action on the control to replace this one, or remove it.",
        UnavailableReason.TurnedOff => "Turn the plugin on and its actions work again. Or drop another action on the control to replace this one, or remove it.",
        _ => "The installed version doesn't have this action (it may come from another version). Drop another action on the control to replace it, or remove it.",
    };

    /// <summary>The store lookup and Install for the plugin (only when it isn't installed).</summary>
    public MissingPluginsViewModel Store { get; }

    public string Status
    {
        get => _status;
        private set
        {
            if (SetProperty(ref _status, value))
            {
                OnPropertyChanged(nameof(HasStatus));
            }
        }
    }

    public bool HasStatus => Status.Length > 0;

    /// <summary>Looks the plugin up in the store (when it isn't installed).</summary>
    public Task LookUpAsync() => IsNotInstalled ? Store.ShowAsync([UnavailableAction.AsMissingPlugin(Binding)]) : Task.CompletedTask;

    [RelayCommand]
    private void Remove() => RemoveRequested?.Invoke(this, EventArgs.Empty);

    private bool CanTurnOn() => IsTurnedOff && _catalog is not null;

    [RelayCommand(CanExecute = nameof(CanTurnOn))]
    private async Task TurnOnAsync()
    {
        if (_catalog is not { } catalog)
        {
            return;
        }

        Status = $"Turning {PluginName} on…";
        try
        {
            await Task.Run(() => catalog.SetEnabled(PluginId, enabled: true));
            Status = $"{PluginName} is on.";
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.IO.IOException or UnauthorizedAccessException)
        {
            Status = $"Couldn't turn it on: {ex.Message}";
        }
    }
}

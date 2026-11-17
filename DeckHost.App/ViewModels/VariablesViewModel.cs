using System;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.Input;
using DeckHost.Contracts;
using DeckHost.Core.Interfaces;
using DeckHost.Core.State;

namespace DeckHost.App.ViewModels;

/// <summary>
/// The Variables page: DeckHost's own variables (<c>system.cpu</c>, the app in front) and the ones workflows set,
/// with scope, type and live value; user variables can be created, changed and removed here.
/// </summary>
public sealed partial class VariablesViewModel : ViewModelBase
{
    private readonly IDeviceHostService _host;
    private string _newName = string.Empty;
    private string _newValue = string.Empty;
    private string _newType = VariableTypes.Text;
    private string _status = string.Empty;

    public VariablesViewModel(IDeviceHostService host)
    {
        _host = host;
        Refresh();
    }

    /// <summary>The variables, DeckHost's own first.</summary>
    public ObservableCollection<VariableItemViewModel> Rows { get; } = [];

    public bool HasRows => Rows.Count > 0;

    /// <summary>The types a new variable can have.</summary>
    public static System.Collections.Generic.IReadOnlyList<string> Types => VariableTypes.Choices;

    public string NewName
    {
        get => _newName;
        set => SetProperty(ref _newName, value ?? string.Empty);
    }

    public string NewValue
    {
        get => _newValue;
        set => SetProperty(ref _newValue, value ?? string.Empty);
    }

    public string NewType
    {
        get => _newType;
        set => SetProperty(ref _newType, value);
    }

    public string Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    /// <summary>Reads the variables from the host again, keeping rows (and what is typed in them) that are still there.</summary>
    [RelayCommand]
    public void Refresh()
    {
        var now = _host.Variables();
        for (var index = Rows.Count - 1; index >= 0; index--)
        {
            if (now.All(info => info.Key != Rows[index].Key))
            {
                Rows.RemoveAt(index);
            }
        }

        for (var index = 0; index < now.Count; index++)
        {
            var info = now[index];
            var row = Rows.FirstOrDefault(existing => existing.Key == info.Key);
            if (row is null)
            {
                Rows.Insert(Math.Min(index, Rows.Count), new VariableItemViewModel(info, this));
            }
            else
            {
                row.Update(info);
            }
        }

        OnPropertyChanged(nameof(HasRows));
    }

    /// <summary>Creates (or changes) a global variable from the fields at the top.</summary>
    [RelayCommand]
    public void Create()
    {
        var name = _newName.Trim();
        if (name.Length == 0)
        {
            Status = "Type the variable's name first.";
            return;
        }

        if (name.Contains('|') || name.StartsWith("system.", StringComparison.OrdinalIgnoreCase))
        {
            Status = "A name can't contain | or start with system.";
            return;
        }

        if (!VariableTypes.Fits(_newType, _newValue))
        {
            Status = $"'{_newValue}' isn't a valid {_newType.ToLowerInvariant()}.";
            return;
        }

        _host.SetVariable(VariableKeys.UserVariable(name), _newValue);
        Status = $"{name} saved.";
        NewName = string.Empty;
        NewValue = string.Empty;
        RefreshSoon();
    }

    internal void Save(VariableItemViewModel row)
    {
        _host.SetVariable(row.Key, row.EditValue);
        Status = $"{row.Name} saved.";
        RefreshSoon();
    }

    internal void Delete(VariableItemViewModel row)
    {
        _host.RemoveVariable(row.Key);
        Rows.Remove(row);
        OnPropertyChanged(nameof(HasRows));
        Status = $"{row.Name} removed.";
    }

    // The host applies a change on its own worker, so the list is read again a moment later (and by the page's timer).
    private void RefreshSoon() => Refresh();
}

/// <summary>One row of the Variables page.</summary>
public sealed partial class VariableItemViewModel : ViewModelBase
{
    private readonly VariablesViewModel _owner;
    private VariableInfo _info;
    private string _editValue;

    internal VariableItemViewModel(VariableInfo info, VariablesViewModel owner)
    {
        _info = info;
        _owner = owner;
        _editValue = info.Value;
    }

    public string Key => _info.Key;

    public string Name => _info.Name;

    public string ScopeLabel => _info.Scope switch
    {
        VariableScope.System => "DeckHost",
        VariableScope.Global => "Everywhere",
        VariableScope.Profile => "One profile",
        _ => "One control",
    };

    public string Type => _info.Type;

    public bool IsUser => _info.IsUser;

    /// <summary>The value as DeckHost has it now.</summary>
    public string Value => _info.Value.Length == 0 ? "—" : _info.Value;

    /// <summary>What is typed in the edit box (user variables).</summary>
    public string EditValue
    {
        get => _editValue;
        set
        {
            if (SetProperty(ref _editValue, value ?? string.Empty))
            {
                OnPropertyChanged(nameof(IsChanged));
            }
        }
    }

    public bool IsChanged => _editValue != _info.Value;

    internal void Update(VariableInfo info)
    {
        var typedHere = IsChanged;
        var oldValue = _info.Value;
        _info = info;
        OnPropertyChanged(nameof(Value));
        OnPropertyChanged(nameof(Type));
        OnPropertyChanged(nameof(Name));
        if (!typedHere && oldValue != info.Value)
        {
            _editValue = info.Value;
            OnPropertyChanged(nameof(EditValue));
        }

        OnPropertyChanged(nameof(IsChanged));
    }

    [RelayCommand]
    private void Save() => _owner.Save(this);

    [RelayCommand]
    private void Delete() => _owner.Delete(this);
}

using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace DeckHost.App.ViewModels;

public class PluginActionGroupViewModel : ViewModelBase
{
    private bool _isExpanded = true;

    public PluginActionGroupViewModel(string pluginId, string pluginName, IEnumerable<ActionItemViewModel> actions)
    {
        PluginId = pluginId;
        PluginName = pluginName;
        foreach (var action in actions)
        {
            Actions.Add(action);
        }
    }

    public string PluginId { get; }

    public string PluginName { get; }

    public ObservableCollection<ActionItemViewModel> Actions { get; } = new();

    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetProperty(ref _isExpanded, value);
    }
}

public class PluginSummaryViewModel : ViewModelBase
{
    private string _status;

    public PluginSummaryViewModel(string id, string name, string kind, string contents, string status)
    {
        Id = id;
        Name = name;
        Kind = kind;
        Contents = contents;
        _status = status;
    }

    public string Id { get; }

    public string Name { get; }

    public string Kind { get; }

    public string Contents { get; }

    public string Status
    {
        get => _status;
        set => SetProperty(ref _status, value);
    }
}

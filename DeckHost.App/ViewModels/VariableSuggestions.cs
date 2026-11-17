using System;
using System.Collections.Generic;
using System.Linq;
using DeckHost.Core.State;

namespace DeckHost.App.ViewModels;

/// <summary>
/// The variables the pickers offer (workflow nodes, Show Variable, a background that follows a variable). The app
/// sets <see cref="Source"/> to the host; without one nothing is offered and typing still works.
/// </summary>
public static class VariableSuggestions
{
    /// <summary>Where the current variables come from.</summary>
    public static Func<IReadOnlyList<VariableInfo>>? Source { get; set; }

    /// <summary>The names of the variables workflows set, without scope (the node's scope setting picks that), global ones first.</summary>
    public static IReadOnlyList<string> Names() =>
        Read().Where(static info => info.IsUser)
            .OrderBy(static info => info.Scope)
            .Select(static info => NameOf(info.Key))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    /// <summary>The full keys (<c>system.cpu</c>, <c>vars.name</c>) for a place that follows a variable of any kind.</summary>
    public static IReadOnlyList<string> Keys() => [.. Read().Select(static info => info.Key)];

    private static IReadOnlyList<VariableInfo> Read()
    {
        try
        {
            return Source?.Invoke() ?? [];
        }
        catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException)
        {
            return [];
        }
    }

    private static string NameOf(string key)
    {
        var bar = key.LastIndexOf('|');
        return bar >= 0 ? key[(bar + 1)..] : key["vars.".Length..];
    }
}

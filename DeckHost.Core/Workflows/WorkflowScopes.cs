using DeckHost.Contracts;
using DeckHost.Contracts.Models;

namespace DeckHost.Core.Workflows;

/// <summary>Where a workflow runs: the profile it belongs to and the control it is on.</summary>
public readonly record struct WorkflowScope(string ProfileId, string DeviceId, string ControlId);

/// <summary>Which variable a node means: the global one, the profile's or the control's.</summary>
public static class WorkflowScopes
{
    /// <summary>The variable store key of <paramref name="name"/> for the node's scope setting.</summary>
    public static string KeyOf(ActionSettings settings, string name, WorkflowScope? scope)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return settings.Get(WorkflowKeys.Scope, WorkflowKeys.ScopeGlobal) switch
        {
            WorkflowKeys.ScopeProfile when scope is { } where => VariableKeys.ProfileVariable(where.ProfileId, name),
            WorkflowKeys.ScopeControl when scope is { } where => VariableKeys.ControlVariable(where.DeviceId, where.ControlId, name),
            _ => VariableKeys.UserVariable(name),
        };
    }

    /// <summary>A variable key as the editor lists it: <c>name</c>, <c>name (profile id)</c> or <c>name (control on device)</c>.</summary>
    public static string Describe(string key)
    {
        const string prefix = "vars.";
        var rest = key.StartsWith(prefix, StringComparison.Ordinal) ? key[prefix.Length..] : key;
        var parts = rest.Split('|');
        return parts switch
        {
            ["profile", var profile, var name] => $"{name} (profile {profile})",
            ["control", var device, var control, var name] => $"{name} ({control} on {device})",
            _ => rest,
        };
    }
}

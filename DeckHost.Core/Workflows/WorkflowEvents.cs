using DeckHost.Contracts;
using DeckHost.Contracts.Models;

namespace DeckHost.Core.Workflows;

/// <summary>
/// What decides whether an event (a variable changed, an app came to the front, a timer is due)
/// starts a trigger node: pure checks on the node's settings, and the guard that stops an event
/// that keeps causing itself.
/// </summary>
public static class WorkflowEvents
{
    /// <summary>Whether a "variable changes" trigger node is about this variable (and this new value).</summary>
    public static bool MatchesVariable(WorkflowNode node, string variableKey, string value)
    {
        ArgumentNullException.ThrowIfNull(node);
        var settings = node.EffectiveSettings;
        var name = settings.Get(WorkflowKeys.Name);
        if (name.Trim().Length == 0 || !string.Equals(VariableKeys.UserVariable(name), variableKey, StringComparison.Ordinal))
        {
            return false;
        }

        var wanted = settings.Get(WorkflowKeys.Value);
        return wanted.Length == 0 || string.Equals(wanted.Trim(), (value ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The same, for the workflow on one control: a trigger node's scope decides which variable it means.</summary>
    public static bool MatchesVariable(WorkflowNode node, string variableKey, string value, WorkflowScope scope)
    {
        ArgumentNullException.ThrowIfNull(node);
        var settings = node.EffectiveSettings;
        var name = settings.Get(WorkflowKeys.Name);
        if (name.Trim().Length == 0 || !string.Equals(WorkflowScopes.KeyOf(settings, name, scope), variableKey, StringComparison.Ordinal))
        {
            return false;
        }

        var wanted = settings.Get(WorkflowKeys.Value);
        return wanted.Length == 0 || string.Equals(wanted.Trim(), (value ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Whether an "app comes to the front" trigger node is about this app (part of its name; an empty setting matches no app).</summary>
    public static bool MatchesApp(WorkflowNode node, string app)
    {
        ArgumentNullException.ThrowIfNull(node);
        var part = node.EffectiveSettings.Get(WorkflowKeys.App).Trim();
        return part.Length > 0 && !string.IsNullOrEmpty(app) && app.Contains(part, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The time between runs of a timer trigger node, kept within what is allowed.</summary>
    public static TimeSpan EveryOf(WorkflowNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        return TimeSpan.FromSeconds(Math.Clamp(node.EffectiveSettings.GetInt(WorkflowKeys.Every, 60), WorkflowKeys.MinEverySeconds, WorkflowKeys.MaxEverySeconds));
    }
}

/// <summary>
/// Stops an event trigger that keeps causing itself (a workflow that changes the variable it
/// starts on): more than <see cref="Limit"/> starts within <see cref="Window"/> pause that
/// trigger for <see cref="Pause"/>.
/// </summary>
public sealed class WorkflowEventThrottle
{
    public const int Limit = 20;

    public static readonly TimeSpan Window = TimeSpan.FromSeconds(5);

    public static readonly TimeSpan Pause = TimeSpan.FromSeconds(30);

    private readonly Dictionary<string, Queue<DateTime>> _starts = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DateTime> _pausedUntil = new(StringComparer.Ordinal);

    /// <summary>Whether the trigger may start now. <paramref name="justPaused"/> is true on the call that pauses it.</summary>
    public bool Allow(string key, DateTime now, out bool justPaused)
    {
        justPaused = false;
        if (_pausedUntil.TryGetValue(key, out var until))
        {
            if (now < until)
            {
                return false;
            }

            _pausedUntil.Remove(key);
            _starts.Remove(key);
        }

        if (!_starts.TryGetValue(key, out var queue))
        {
            queue = new Queue<DateTime>();
            _starts[key] = queue;
        }

        while (queue.Count > 0 && now - queue.Peek() > Window)
        {
            queue.Dequeue();
        }

        if (queue.Count >= Limit)
        {
            _pausedUntil[key] = now + Pause;
            justPaused = true;
            return false;
        }

        queue.Enqueue(now);
        return true;
    }

    /// <summary>Forgets everything (the profile changed or the host stopped).</summary>
    public void Reset()
    {
        _starts.Clear();
        _pausedUntil.Clear();
    }
}

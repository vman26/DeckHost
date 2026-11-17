using System.Globalization;
using DeckHost.Contracts;
using DeckHost.Contracts.Enums;
using DeckHost.Contracts.Interfaces;
using DeckHost.Contracts.Models;
using DeckHost.Core.Models;

namespace DeckHost.Core.Workflows;

/// <summary>
/// Runs visual workflows (<see cref="ProfileActionKind.Workflow"/>). Everything runs on the host's
/// worker: a run follows its links through a queue of its own (no deep recursion), and a wait hands
/// the rest to <see cref="Scheduler"/>, so the worker never blocks. Each step is reported through
/// <see cref="Traced"/> (the editor's live view).
/// </summary>
public sealed class WorkflowEngine
{
    /// <summary>The most nodes one run may enter; a run past it is stopped (a safety net, e.g. a big repeat in a repeat).</summary>
    public const int MaxNodesPerRun = 20_000;

    private readonly IVariableStore _variables;
    private readonly Func<ActionReference, DeviceInputEvent, IActionHost, string, bool> _dispatch;
    private readonly Dictionary<(string DeviceId, string ControlId, string TriggerId), List<Run>> _running = [];
    private int _lastRunId;

    /// <param name="dispatch">Runs one action for an input: (action, input, host, profile id) → whether it ran.</param>
    public WorkflowEngine(IVariableStore variables, Func<ActionReference, DeviceInputEvent, IActionHost, string, bool> dispatch)
    {
        ArgumentNullException.ThrowIfNull(variables);
        ArgumentNullException.ThrowIfNull(dispatch);
        _variables = variables;
        _dispatch = dispatch;
    }

    /// <summary>Runs work later on the host's worker: device id, delay, work. Without it waits are skipped.</summary>
    public Action<string, TimeSpan, Action>? Scheduler { get; set; }

    /// <summary>The local time, for time-of-day conditions (replaceable in tests).</summary>
    public Func<DateTime> Clock { get; set; } = static () => DateTime.Now;

    public event Action<WorkflowTrace>? Traced;

    /// <summary>Whether any run of the control's workflow is still going.</summary>
    public bool IsRunning(string deviceId, string controlId) =>
        _running.Any(pair => Same(pair.Key.DeviceId, deviceId) && Same(pair.Key.ControlId, controlId) && pair.Value.Count > 0);

    /// <summary>The triggers a workflow listens to.</summary>
    public static IReadOnlySet<string> TriggersOf(Workflow? workflow) =>
        (workflow?.Nodes ?? [])
            .Where(static node => node.Kind == WorkflowNodeKinds.Trigger)
            .Select(static node => node.EffectiveSettings.Get(WorkflowKeys.On, WorkflowTriggers.Down))
            .ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// Starts a run at every trigger node of the binding's workflow that listens to
    /// <paramref name="trigger"/>, as the workflow's <see cref="Workflow.WhileRunning"/> allows.
    /// </summary>
    /// <param name="accept">Narrows which of those trigger nodes start (an event trigger that also checks its settings).</param>
    /// <returns>Whether anything started or was stopped.</returns>
    public bool Fire(DeviceProfile profile, ControlBinding binding, string trigger, DeviceInputEvent input, IActionHost host, Func<WorkflowNode, bool>? accept = null)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(host);
        if (binding.Workflow is not { } workflow)
        {
            return false;
        }

        var handled = false;
        foreach (var node in workflow.Nodes.Where(node =>
                     node.Kind == WorkflowNodeKinds.Trigger &&
                     node.EffectiveSettings.Get(WorkflowKeys.On, WorkflowTriggers.Down) == trigger &&
                     (accept is null || accept(node))))
        {
            var key = (input.DeviceId, binding.ControlId, node.Id);
            if (_running.TryGetValue(key, out var going) && going.Count > 0)
            {
                // An event (a timer, a variable) never piles runs up or stops one: it waits for the run to end.
                if (WorkflowTriggers.IsEvent(trigger))
                {
                    continue;
                }

                switch (workflow.WhileRunning)
                {
                    case WorkflowConcurrency.Ignore:
                        Trace(going[0], node.Id, WorkflowTraceKind.Ignored, "Still running: input ignored.");
                        continue;
                    case WorkflowConcurrency.Stop:
                        foreach (var run in going.ToList())
                        {
                            Cancel(run, "Stopped by pressing again.");
                        }

                        handled = true;
                        continue;
                    case WorkflowConcurrency.Restart:
                        foreach (var run in going.ToList())
                        {
                            Cancel(run, "Restarted.");
                        }

                        break;
                }
            }

            var started = new Run(++_lastRunId, key, profile.Id, workflow, input with { ControlId = binding.ControlId }, host);
            if (!_running.TryGetValue(key, out var list))
            {
                list = [];
                _running[key] = list;
            }

            list.Add(started);
            Trace(started, node.Id, WorkflowTraceKind.Started, WorkflowTriggers.Describe(trigger));
            Post(started, () => Enter(started, node, () => Finish(started)));
            handled = true;
        }

        return handled;
    }

    /// <summary>Stops every run of a control's workflow (the editor's Stop, or a profile change).</summary>
    public void StopAll(string deviceId, string controlId)
    {
        foreach (var (key, runs) in _running.ToList())
        {
            if (Same(key.DeviceId, deviceId) && Same(key.ControlId, controlId))
            {
                foreach (var run in runs.ToList())
                {
                    Cancel(run, "Stopped.");
                }
            }
        }
    }

    private void Enter(Run run, WorkflowNode node, Action done)
    {
        if (run.Ended)
        {
            return;
        }

        if (++run.NodesEntered > MaxNodesPerRun)
        {
            Trace(run, node.Id, WorkflowTraceKind.Failed, $"Stopped after {MaxNodesPerRun} steps.");
            End(run);
            return;
        }

        var settings = node.EffectiveSettings;
        switch (node.Kind)
        {
            case WorkflowNodeKinds.Action:
            {
                var ran = node.Action is { } action && Pulse(run, action);
                Trace(run, node.Id, ran ? WorkflowTraceKind.Node : WorkflowTraceKind.Failed,
                    node.Action is null ? "No action chosen." : ran ? node.Action.ActionName : $"{node.Action.ActionName} did not run (not loaded, or it failed).");
                Follow(run, node, WorkflowPorts.Next, done);
                break;
            }

            case WorkflowNodeKinds.Wait:
            {
                var ms = Math.Clamp(settings.GetInt(WorkflowKeys.Milliseconds, 500), 0, WorkflowKeys.MaxWaitMs);
                Trace(run, node.Id, WorkflowTraceKind.Node, $"Waiting {FormatDuration(ms)}", ms > 0 ? TimeSpan.FromMilliseconds(ms) : null);
                if (ms > 0 && Scheduler is { } schedule)
                {
                    schedule(run.Input.DeviceId, TimeSpan.FromMilliseconds(ms), () => Post(run, () => Follow(run, node, WorkflowPorts.Next, done)));
                }
                else
                {
                    Follow(run, node, WorkflowPorts.Next, done);
                }

                break;
            }

            case WorkflowNodeKinds.If:
            {
                var yes = Evaluate(settings);
                Trace(run, node.Id, WorkflowTraceKind.Node, yes ? "Yes" : "No");
                Follow(run, node, yes ? WorkflowPorts.Yes : WorkflowPorts.No, done);
                break;
            }

            case WorkflowNodeKinds.Toggle:
            {
                var key = ToggleKey(run.Input.DeviceId, run.Input.ControlId, node.Id);
                var second = _variables.TryGetValue<bool>(key, out var state) && state;
                _variables.Publish(key, !second);
                Trace(run, node.Id, WorkflowTraceKind.Node, second ? "2nd" : "1st");
                Follow(run, node, second ? WorkflowPorts.Second : WorkflowPorts.First, done);
                break;
            }

            case WorkflowNodeKinds.Repeat:
            {
                var mode = settings.Get(WorkflowKeys.Mode, WorkflowKeys.ModeCount);
                var interval = Math.Clamp(settings.GetInt(WorkflowKeys.Interval, 0), 0, WorkflowKeys.MaxWaitMs);

                // A fixed number of rounds is known now; while / until ask before every round.
                int? times = mode switch
                {
                    WorkflowKeys.ModeVariable => Math.Clamp((int)Math.Round(Number(ReadVariable(settings.Get(WorkflowKeys.Times)), 0)), 0, WorkflowKeys.MaxRepeat),
                    WorkflowKeys.ModeWhile or WorkflowKeys.ModeUntil => null,
                    _ => Math.Clamp(settings.GetInt(WorkflowKeys.Count, 2), 1, WorkflowKeys.MaxRepeat),
                };
                var round = 0;

                bool Another() => times is { } fixedTimes
                    ? round < fixedTimes
                    : round < WorkflowKeys.MaxConditionRounds && Evaluate(settings) == (mode == WorkflowKeys.ModeWhile);

                void Loop()
                {
                    if (Another())
                    {
                        round++;
                        Trace(run, node.Id, WorkflowTraceKind.Node, times is { } total ? $"Round {round} of {total}" : $"Round {round}");
                        Follow(run, node, WorkflowPorts.Each, Pause);
                    }
                    else
                    {
                        if (times is null && round >= WorkflowKeys.MaxConditionRounds)
                        {
                            Trace(run, node.Id, WorkflowTraceKind.Failed, $"Stopped after {WorkflowKeys.MaxConditionRounds} rounds.");
                        }

                        Follow(run, node, WorkflowPorts.Done, done);
                    }
                }

                // Between rounds (not after the last of a fixed number): wait, then ask again.
                void Pause()
                {
                    var last = times is { } fixedTimes && round >= fixedTimes;
                    if (interval > 0 && !last && Scheduler is { } schedule)
                    {
                        Trace(run, node.Id, WorkflowTraceKind.Node, $"Next round in {FormatDuration(interval)}", TimeSpan.FromMilliseconds(interval));
                        schedule(run.Input.DeviceId, TimeSpan.FromMilliseconds(interval), () => Post(run, Loop));
                    }
                    else
                    {
                        Loop();
                    }
                }

                Loop();
                break;
            }

            case WorkflowNodeKinds.SetVariable:
                Trace(run, node.Id, WorkflowTraceKind.Node, Apply(settings));
                Follow(run, node, WorkflowPorts.Next, done);
                break;

            case WorkflowNodeKinds.Stop:
                Trace(run, node.Id, WorkflowTraceKind.Node, "Stop");
                Cancel(run, "Ended by a Stop node.");
                break;

            default:
                // The trigger (and kinds from a newer version) just continue.
                if (node.Kind != WorkflowNodeKinds.Trigger)
                {
                    Trace(run, node.Id, WorkflowTraceKind.Node, node.Kind);
                }

                Follow(run, node, WorkflowPorts.Next, done);
                break;
        }
    }

    /// <summary>Continues at every node linked from a port; <paramref name="done"/> runs once they all finished.</summary>
    private void Follow(Run run, WorkflowNode node, string port, Action done)
    {
        var targets = run.Workflow.LinksFrom(node.Id, port)
            .Select(link => run.Workflow.Find(link.To))
            .OfType<WorkflowNode>()
            .ToList();
        if (targets.Count == 0)
        {
            Post(run, done);
            return;
        }

        var remaining = targets.Count;
        foreach (var target in targets)
        {
            Post(run, () => Enter(run, target, () =>
            {
                if (--remaining == 0)
                {
                    done();
                }
            }));
        }
    }

    /// <summary>Runs the work on the run's queue: one item at a time, so long chains don't nest calls.</summary>
    private static void Post(Run run, Action work)
    {
        if (run.Ended)
        {
            return;
        }

        run.Work.Enqueue(work);
        if (run.Pumping)
        {
            return;
        }

        run.Pumping = true;
        try
        {
            while (!run.Ended && run.Work.TryDequeue(out var next))
            {
                next();
            }
        }
        finally
        {
            run.Pumping = false;
            if (run.Ended)
            {
                run.Work.Clear();
            }
        }
    }

    /// <summary>
    /// Runs an action for the run's input: turning and swiping as they happened, anything else as a
    /// press and release of the control (like a Multi Action step).
    /// </summary>
    private bool Pulse(Run run, ActionReference action)
    {
        var input = run.Input;
        if (input.EventType is InputCapabilities.Rotate or InputCapabilities.Swipe)
        {
            return _dispatch(action, input, run.Host, run.ProfileId);
        }

        var ran = _dispatch(action, input with { EventType = InputCapabilities.Press, Value = 1 }, run.Host, run.ProfileId);
        ran |= _dispatch(action, input with { EventType = InputCapabilities.Release, Value = 0 }, run.Host, run.ProfileId);
        return ran;
    }

    /// <summary>The answer of an If node.</summary>
    internal bool Evaluate(ActionSettings settings)
    {
        switch (settings.Get(WorkflowKeys.Check, WorkflowKeys.CheckVariable))
        {
            case WorkflowKeys.CheckApp:
            {
                var part = settings.Get(WorkflowKeys.App);
                return part.Length > 0 &&
                       _variables.TryGetValue<string>(VariableKeys.ForegroundApp, out var app) &&
                       app?.Contains(part.Trim(), StringComparison.OrdinalIgnoreCase) == true;
            }

            case WorkflowKeys.CheckTime:
            {
                if (!TryParseTime(settings.Get(WorkflowKeys.From), out var from) || !TryParseTime(settings.Get(WorkflowKeys.To), out var to))
                {
                    return false;
                }

                var now = TimeOnly.FromDateTime(Clock());
                return from <= to ? now >= from && now < to : now >= from || now < to;
            }

            default:
            {
                var name = settings.Get(WorkflowKeys.Name);
                if (name.Length == 0)
                {
                    return false;
                }

                var current = ReadVariable(name);
                var expected = settings.Get(WorkflowKeys.Value);
                return settings.Get(WorkflowKeys.Operation, WorkflowKeys.IsEqual) switch
                {
                    WorkflowKeys.IsNotEqual => !AreEqual(current, expected),
                    WorkflowKeys.IsGreater => Compare(current, expected) > 0,
                    WorkflowKeys.IsLess => Compare(current, expected) < 0,
                    WorkflowKeys.Contains => current.Contains(expected, StringComparison.OrdinalIgnoreCase),
                    WorkflowKeys.IsSet => current.Length > 0,
                    WorkflowKeys.IsEmpty => current.Length == 0,
                    _ => AreEqual(current, expected),
                };
            }
        }
    }

    /// <summary>What a Set variable node does; returns a line for the trace.</summary>
    internal string Apply(ActionSettings settings)
    {
        var name = settings.Get(WorkflowKeys.Name);
        if (name.Length == 0)
        {
            return "No variable name.";
        }

        var current = ReadVariable(name);
        var value = settings.Get(WorkflowKeys.Value);
        var result = settings.Get(WorkflowKeys.Operation, WorkflowKeys.Set) switch
        {
            WorkflowKeys.Add => Format(Number(current, 0) + Number(value, 1)),
            WorkflowKeys.Subtract => Format(Number(current, 0) - Number(value, 1)),
            WorkflowKeys.Flip => string.Equals(current, "true", StringComparison.OrdinalIgnoreCase) ? "false" : "true",
            WorkflowKeys.Clear => string.Empty,
            _ => value,
        };

        _variables.Publish(VariableKeys.UserVariable(name), result);
        return $"{name.Trim()} = {(result.Length == 0 ? "(empty)" : result)}";
    }

    public string ReadVariable(string name) =>
        _variables.TryGetValue<string>(VariableKeys.UserVariable(name), out var value) ? value ?? string.Empty : string.Empty;

    /// <summary>Where a Toggle node keeps which output is next.</summary>
    public static string ToggleKey(string deviceId, string controlId, string nodeId) =>
        VariableKeys.For(deviceId, controlId, $"{VariableKeys.WorkflowState}.{nodeId}");

    private void Finish(Run run)
    {
        if (run.Ended)
        {
            return;
        }

        Trace(run, null, WorkflowTraceKind.Finished, $"Finished after {run.NodesEntered} step{(run.NodesEntered == 1 ? string.Empty : "s")}.");
        End(run);
    }

    private void Cancel(Run run, string reason)
    {
        if (run.Ended)
        {
            return;
        }

        Trace(run, null, WorkflowTraceKind.Stopped, reason);
        End(run);
    }

    private void End(Run run)
    {
        run.Ended = true;
        if (_running.TryGetValue(run.Key, out var list))
        {
            list.Remove(run);
            if (list.Count == 0)
            {
                _running.Remove(run.Key);
            }
        }
    }

    private void Trace(Run run, string? nodeId, WorkflowTraceKind kind, string detail, TimeSpan? wait = null)
    {
        var handler = Traced;
        if (handler is null)
        {
            return;
        }

        try
        {
            handler(new WorkflowTrace(run.Input.DeviceId, run.ProfileId, run.Input.ControlId, run.Id, nodeId, kind, detail, DateTimeOffset.Now) { Wait = wait });
        }
        catch
        {
            // A listener must not stop a run.
        }
    }

    private static bool AreEqual(string current, string expected) =>
        TryNumber(current, out var a) && TryNumber(expected, out var b)
            ? Math.Abs(a - b) < 1e-9
            : string.Equals(current.Trim(), expected.Trim(), StringComparison.OrdinalIgnoreCase);

    private static int Compare(string current, string expected) =>
        TryNumber(current, out var a) && TryNumber(expected, out var b)
            ? a.CompareTo(b)
            : string.Compare(current, expected, StringComparison.OrdinalIgnoreCase);

    private static bool TryNumber(string text, out double value) =>
        double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value) && double.IsFinite(value);

    private static double Number(string text, double fallback) => TryNumber(text, out var value) ? value : fallback;

    private static string Format(double value) => value.ToString("0.#########", CultureInfo.InvariantCulture);

    private static bool TryParseTime(string text, out TimeOnly time) =>
        TimeOnly.TryParseExact(text.Trim(), ["H:mm", "HH:mm"], CultureInfo.InvariantCulture, DateTimeStyles.None, out time);

    public static string FormatDuration(int ms) => ms switch
    {
        < 1000 => $"{ms} ms",
        _ when ms % 1000 == 0 => $"{ms / 1000} s",
        _ => $"{(ms / 1000.0).ToString("0.##", CultureInfo.InvariantCulture)} s",
    };

    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private sealed class Run(int id, (string, string, string) key, string profileId, Workflow workflow, DeviceInputEvent input, IActionHost host)
    {
        public int Id { get; } = id;

        public (string DeviceId, string ControlId, string TriggerId) Key { get; } = key;

        public string ProfileId { get; } = profileId;

        public Workflow Workflow { get; } = workflow;

        public DeviceInputEvent Input { get; } = input;

        public IActionHost Host { get; } = host;

        public Queue<Action> Work { get; } = new();

        public bool Pumping { get; set; }

        public bool Ended { get; set; }

        public int NodesEntered { get; set; }
    }
}

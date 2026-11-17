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

    /// <summary>Finds a shared workflow by id (what a Call node runs); set by the host.</summary>
    public Func<string, Workflow?>? SharedWorkflows { get; set; }

    private readonly Dictionary<(string DeviceId, string ControlId), HashSet<string>> _breakpoints = [];

    /// <summary>The key of a breakpoint on a node: its id, with the shared workflow's id in front when it is in one.</summary>
    public static string BreakpointKey(string? workflowId, string nodeId) => workflowId is null ? nodeId : $"{workflowId}/{nodeId}";

    /// <summary>Sets where the runs of a control's workflow stop (<see cref="BreakpointKey"/>s). Runs that are going keep to it from the next node on.</summary>
    public void SetBreakpoints(string deviceId, string controlId, IReadOnlyCollection<string> keys)
    {
        var key = (deviceId.ToLowerInvariant(), controlId.ToLowerInvariant());
        if (keys.Count == 0)
        {
            _breakpoints.Remove(key);
        }
        else
        {
            _breakpoints[key] = new HashSet<string>(keys, StringComparer.Ordinal);
        }
    }

    /// <summary>The ids of the runs of a control's workflow that are held at a breakpoint or step.</summary>
    public IReadOnlyList<int> PausedRuns(string deviceId, string controlId) =>
        [.. _running.Where(pair => Same(pair.Key.DeviceId, deviceId) && Same(pair.Key.ControlId, controlId)).SelectMany(static pair => pair.Value).Where(static run => run.Paused).Select(static run => run.Id)];

    /// <summary>Lets a paused run go on until the next breakpoint (or its end).</summary>
    public bool Continue(int runId) => Release(runId, step: false);

    /// <summary>Lets every held run go on (and no run is held again until a breakpoint is set): returns how many were held.</summary>
    public int ReleaseAll()
    {
        var held = _running.Values.SelectMany(static list => list).Where(static run => run.Paused).Select(static run => run.Id).ToList();
        foreach (var runId in held)
        {
            Release(runId, step: false);
        }

        return held.Count;
    }

    /// <summary>Lets a paused run enter the node it waits in front of, then holds it before the next one.</summary>
    public bool Step(int runId) => Release(runId, step: true);

    private bool Release(int runId, bool step)
    {
        var run = _running.Values.SelectMany(static list => list).FirstOrDefault(candidate => candidate.Id == runId && candidate.Paused);
        if (run?.Resume is not { } resume)
        {
            return false;
        }

        run.StepMode = step;
        run.Paused = false;
        run.Resume = null;
        Trace(run, null, WorkflowTraceKind.Resumed, step ? "Step" : "Continue");
        Post(run, resume);
        return true;
    }

    private bool IsBreakpoint(Run run, Frame frame, WorkflowNode node) =>
        _breakpoints.Count > 0 &&
        _breakpoints.TryGetValue((run.Input.DeviceId.ToLowerInvariant(), run.Input.ControlId.ToLowerInvariant()), out var keys) &&
        keys.Contains(BreakpointKey(frame.CallId, node.Id));

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
            Post(started, () => Enter(started, new Frame(workflow, null, null), node, () => Finish(started)));
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

    private void Enter(Run run, Frame frame, WorkflowNode node, Action done, bool skipGate = false)
    {
        if (run.Ended)
        {
            return;
        }

        // A breakpoint, or a step that wants to stop at the next node, holds the run in front of this one.
        if (!skipGate && (run.StepMode || IsBreakpoint(run, frame, node)))
        {
            run.Paused = true;
            run.Resume = () => Enter(run, frame, node, done, skipGate: true);
            Trace(run, frame, node.Id, WorkflowTraceKind.Paused, run.StepMode ? "Paused (step)" : "Paused at a breakpoint");
            return;
        }

        if (++run.NodesEntered > MaxNodesPerRun)
        {
            Trace(run, frame, node.Id, WorkflowTraceKind.Failed, $"Stopped after {MaxNodesPerRun} steps.");
            End(run);
            return;
        }

        var settings = node.EffectiveSettings;
        switch (node.Kind)
        {
            case WorkflowNodeKinds.Action:
                RunAction(run, frame, node, 1, done);
                break;

            case WorkflowNodeKinds.Wait:
            {
                var ms = Math.Clamp(settings.GetInt(WorkflowKeys.Milliseconds, 500), 0, WorkflowKeys.MaxWaitMs);
                Trace(run, frame, node.Id, WorkflowTraceKind.Node, $"Waiting {FormatDuration(ms)}", ms > 0 ? TimeSpan.FromMilliseconds(ms) : null);
                if (ms > 0 && Scheduler is { } schedule)
                {
                    schedule(run.Input.DeviceId, TimeSpan.FromMilliseconds(ms), () => Post(run, () => Follow(run, frame, node, WorkflowPorts.Next, done)));
                }
                else
                {
                    Follow(run, frame, node, WorkflowPorts.Next, done);
                }

                break;
            }

            case WorkflowNodeKinds.If:
            {
                var yes = EvaluateDetailed(settings, run.Scope, out var problem);
                if (problem is not null)
                {
                    Trace(run, frame, node.Id, WorkflowTraceKind.Failed, $"{problem} Going on at No.");
                }
                else
                {
                    Trace(run, frame, node.Id, WorkflowTraceKind.Node, yes ? "Yes" : "No");
                }

                Follow(run, frame, node, yes ? WorkflowPorts.Yes : WorkflowPorts.No, done);
                break;
            }

            case WorkflowNodeKinds.Toggle:
            {
                var key = ToggleKey(run.Input.DeviceId, run.Input.ControlId, node.Id);
                var second = _variables.TryGetValue<bool>(key, out var state) && state;
                _variables.Publish(key, !second);
                Trace(run, frame, node.Id, WorkflowTraceKind.Node, second ? "2nd" : "1st");
                Follow(run, frame, node, second ? WorkflowPorts.Second : WorkflowPorts.First, done);
                break;
            }

            case WorkflowNodeKinds.Repeat:
            {
                var mode = settings.Get(WorkflowKeys.Mode, WorkflowKeys.ModeCount);
                var interval = Math.Clamp(settings.GetInt(WorkflowKeys.Interval, 0), 0, WorkflowKeys.MaxWaitMs);

                // A fixed number of rounds is known now; while / until ask before every round.
                int? times = mode switch
                {
                    WorkflowKeys.ModeVariable => Math.Clamp((int)Math.Round(Number(ReadVariable(settings.Get(WorkflowKeys.Times), settings, run.Scope), 0)), 0, WorkflowKeys.MaxRepeat),
                    WorkflowKeys.ModeWhile or WorkflowKeys.ModeUntil => null,
                    _ => Math.Clamp(settings.GetInt(WorkflowKeys.Count, 2), 1, WorkflowKeys.MaxRepeat),
                };
                var round = 0;

                bool Another() => times is { } fixedTimes
                    ? round < fixedTimes
                    : round < WorkflowKeys.MaxConditionRounds && Evaluate(settings, run.Scope) == (mode == WorkflowKeys.ModeWhile);

                void Loop()
                {
                    if (Another())
                    {
                        round++;
                        Trace(run, frame, node.Id, WorkflowTraceKind.Node, times is { } total ? $"Round {round} of {total}" : $"Round {round}");
                        Follow(run, frame, node, WorkflowPorts.Each, Pause);
                    }
                    else
                    {
                        if (times is null && round >= WorkflowKeys.MaxConditionRounds)
                        {
                            Trace(run, frame, node.Id, WorkflowTraceKind.Failed, $"Stopped after {WorkflowKeys.MaxConditionRounds} rounds.");
                        }

                        Follow(run, frame, node, WorkflowPorts.Done, done);
                    }
                }

                // Between rounds (not after the last of a fixed number): wait, then ask again.
                void Pause()
                {
                    var last = times is { } fixedTimes && round >= fixedTimes;
                    if (interval > 0 && !last && Scheduler is { } schedule)
                    {
                        Trace(run, frame, node.Id, WorkflowTraceKind.Node, $"Next round in {FormatDuration(interval)}", TimeSpan.FromMilliseconds(interval));
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
                {
                    var (line, ok) = TryApply(settings, run.Scope);
                    Trace(run, frame, node.Id, ok ? WorkflowTraceKind.Node : WorkflowTraceKind.Failed, line);
                }

                Follow(run, frame, node, WorkflowPorts.Next, done);
                break;

            case WorkflowNodeKinds.Call:
                RunCall(run, frame, node, done);
                break;

            case WorkflowNodeKinds.Switch:
            {
                var (port, line, ok) = ChooseCase(settings, run.Scope);
                Trace(run, frame, node.Id, ok ? WorkflowTraceKind.Node : WorkflowTraceKind.Failed, line);
                Follow(run, frame, node, port, done);
                break;
            }

            case WorkflowNodeKinds.Stop:
                Trace(run, frame, node.Id, WorkflowTraceKind.Node, "Stop");
                Cancel(run, "Ended by a Stop node.");
                break;

            default:
                // The trigger (and kinds from a newer version) just continue.
                if (node.Kind != WorkflowNodeKinds.Trigger)
                {
                    Trace(run, frame, node.Id, WorkflowTraceKind.Node, node.Kind);
                }

                Follow(run, frame, node, WorkflowPorts.Next, done);
                break;
        }
    }

    /// <summary>Runs a Call node: the shared workflow from its When node on, then goes on at Then (or On error when it can't run).</summary>
    private void RunCall(Run run, Frame frame, WorkflowNode node, Action done)
    {
        var id = node.EffectiveSettings.Get(WorkflowKeys.Workflow).Trim();
        var shared = id.Length == 0 ? null : SharedWorkflows?.Invoke(id);
        var entry = shared?.Nodes.FirstOrDefault(static candidate => candidate.Kind == WorkflowNodeKinds.Trigger);
        var problem = id.Length == 0 ? "No workflow chosen."
            : shared is null ? $"The shared workflow '{id}' is gone."
            : entry is null ? $"'{shared.Name ?? id}' has no When node to start at."
            : frame.Calls(id) ? $"'{shared.Name ?? id}' would call itself."
            : frame.Depth >= WorkflowKeys.MaxCallDepth ? $"Calls are nested more than {WorkflowKeys.MaxCallDepth} deep."
            : null;
        if (problem is not null)
        {
            var onError = frame.Workflow.LinksFrom(node.Id, WorkflowPorts.Error).Any();
            Trace(run, frame, node.Id, WorkflowTraceKind.Failed, onError ? problem + " Going on at \"On error\"." : problem);
            Follow(run, frame, node, onError ? WorkflowPorts.Error : WorkflowPorts.Next, done);
            return;
        }

        Trace(run, frame, node.Id, WorkflowTraceKind.Node, $"Calling {shared!.Name ?? id}");
        Enter(run, new Frame(shared, frame, id), entry!, () => Follow(run, frame, node, WorkflowPorts.Next, done));
    }

    /// <summary>Continues at every node linked from a port; <paramref name="done"/> runs once they all finished.</summary>
    /// <summary>
    /// Runs an action node. When the action did not run it is tried again as many times as the node says,
    /// then the run goes on at the node's error output when that is linked, else at its next one.
    /// </summary>
    private void RunAction(Run run, Frame frame, WorkflowNode node, int attempt, Action done)
    {
        if (run.Ended)
        {
            return;
        }

        var settings = node.EffectiveSettings;
        var ran = node.Action is { } action && Pulse(run, action);
        var retries = Math.Clamp(settings.GetInt(WorkflowKeys.Retries, 0), 0, WorkflowKeys.MaxRetries);
        if (!ran && node.Action is { } failed && attempt <= retries && Scheduler is { } schedule)
        {
            var delay = Math.Clamp(settings.GetInt(WorkflowKeys.RetryDelay, 1000), 0, WorkflowKeys.MaxWaitMs);
            Trace(run, frame, node.Id, WorkflowTraceKind.Node,
                $"{failed.ActionName} did not run; trying again in {FormatDuration(delay)} (try {attempt + 1} of {retries + 1})",
                delay > 0 ? TimeSpan.FromMilliseconds(delay) : null);
            schedule(run.Input.DeviceId, TimeSpan.FromMilliseconds(delay), () => Post(run, () => RunAction(run, frame, node, attempt + 1, done)));
            return;
        }

        var line = node.Action is null
            ? "No action chosen."
            : ran ? node.Action.ActionName : $"{node.Action.ActionName} did not run (not loaded, or it failed).";
        var onError = !ran && frame.Workflow.LinksFrom(node.Id, WorkflowPorts.Error).Any();
        Trace(run, frame, node.Id, ran ? WorkflowTraceKind.Node : WorkflowTraceKind.Failed, onError ? line + " Going on at \"On error\"." : line);
        Follow(run, frame, node, onError ? WorkflowPorts.Error : WorkflowPorts.Next, done);
    }

    private void Follow(Run run, Frame frame, WorkflowNode node, string port, Action done)
    {
        var targets = frame.Workflow.LinksFrom(node.Id, port)
            .Select(link => frame.Workflow.Find(link.To))
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
            Post(run, () => Enter(run, frame, target, () =>
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
        if (run.Pumping || run.Paused)
        {
            return;
        }

        run.Pumping = true;
        try
        {
            while (!run.Ended && !run.Paused && run.Work.TryDequeue(out var next))
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
    internal bool Evaluate(ActionSettings settings, WorkflowScope? scope = null) => EvaluateDetailed(settings, scope, out _);

    /// <summary>The answer of an If node, and why when the check could not be made (an expression that failed: the answer is then No).</summary>
    internal bool EvaluateDetailed(ActionSettings settings, WorkflowScope? scope, out string? problem)
    {
        problem = null;
        switch (settings.Get(WorkflowKeys.Check, WorkflowKeys.CheckVariable))
        {
            case WorkflowKeys.CheckExpression:
            {
                var result = Calculate(settings.Get(WorkflowKeys.Expression), scope);
                problem = result.Error;
                return result.Ok && result.Value.IsTrue;
            }

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

                var current = ReadVariable(name, settings, scope);
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
    internal string Apply(ActionSettings settings, WorkflowScope? scope = null) => TryApply(settings, scope).Line;

    /// <summary>
    /// What a Set variable node does, with the line for the trace and whether it worked. A calculation that
    /// fails, or a value that doesn't fit the chosen type, leaves the variable as it was.
    /// </summary>
    internal (string Line, bool Ok) TryApply(ActionSettings settings, WorkflowScope? scope = null)
    {
        var name = settings.Get(WorkflowKeys.Name);
        if (name.Length == 0)
        {
            return ("No variable name.", false);
        }

        var current = ReadVariable(name, settings, scope);
        var value = settings.Get(WorkflowKeys.Value);
        string result;
        switch (settings.Get(WorkflowKeys.Operation, WorkflowKeys.Set))
        {
            case WorkflowKeys.Add:
                result = Format(Number(current, 0) + Number(value, 1));
                break;
            case WorkflowKeys.Subtract:
                result = Format(Number(current, 0) - Number(value, 1));
                break;
            case WorkflowKeys.Flip:
                result = string.Equals(current, "true", StringComparison.OrdinalIgnoreCase) ? "false" : "true";
                break;
            case WorkflowKeys.Clear:
                result = string.Empty;
                break;
            case WorkflowKeys.Calculate:
            {
                var calculated = Calculate(settings.Get(WorkflowKeys.Expression), scope);
                if (!calculated.Ok)
                {
                    return ($"{name.Trim()}: {calculated.Error}", false);
                }

                result = calculated.Value.Text;
                break;
            }

            default:
                result = value;
                break;
        }

        if (ConvertTo(settings.Get(WorkflowKeys.As, WorkflowKeys.AsAuto), result, out var converted) is { } why)
        {
            return ($"{name.Trim()}: {why}", false);
        }

        result = converted;
        _variables.Publish(WorkflowScopes.KeyOf(settings, name, scope), result);
        return ($"{name.Trim()} = {(result.Length == 0 ? "(empty)" : result)}", true);
    }

    /// <summary>Stores a value as the chosen type: null when it fits (converted holds the text to store), else why not.</summary>
    internal static string? ConvertTo(string type, string text, out string converted)
    {
        converted = text;
        switch (type)
        {
            case WorkflowKeys.AsNumber:
                if (!TryNumber(text, out var number))
                {
                    return $"'{text}' is not a number.";
                }

                converted = Format(number);
                return null;
            case WorkflowKeys.AsBool:
                if (WorkflowExpression.ParseBool(text) is not { } flag)
                {
                    return $"'{text}' is not true or false.";
                }

                converted = flag ? "true" : "false";
                return null;
            default:
                return null;
        }
    }

    /// <summary>The output a Switch node goes on at: the first case equal to the value, else Else (also when the expression fails).</summary>
    internal (string Port, string Line, bool Ok) ChooseCase(ActionSettings settings, WorkflowScope? scope)
    {
        var result = Calculate(settings.Get(WorkflowKeys.Expression), scope);
        if (!result.Ok)
        {
            return (WorkflowPorts.Default, $"{result.Error} Going on at Else.", false);
        }

        foreach (var one in WorkflowCases.Parse(settings.Get(WorkflowKeys.Cases)))
        {
            if (AreEqual(result.Value.Text, one.Text))
            {
                return (WorkflowCases.PortOf(one.Id), $"{(result.Value.Text.Length == 0 ? "(empty)" : result.Value.Text)} = {(one.Text.Length == 0 ? "(empty)" : one.Text)}", true);
            }
        }

        return (WorkflowPorts.Default, $"{(result.Value.Text.Length == 0 ? "(empty)" : result.Value.Text)}: no case matches, Else", true);
    }

    /// <summary>Evaluates an expression for a node: <c>[name]</c> reads a variable ([profile:name] and [control:name] pick a scope).</summary>
    internal WorkflowExpressionResult Calculate(string expression, WorkflowScope? scope) =>
        WorkflowExpression.Evaluate(expression, name => ReadNamed(name, scope), Clock);

    private string ReadNamed(string name, WorkflowScope? scope)
    {
        const string profile = "profile:";
        const string control = "control:";
        var key = name.StartsWith(profile, StringComparison.OrdinalIgnoreCase) && scope is { } p ? VariableKeys.ProfileVariable(p.ProfileId, name[profile.Length..])
            : name.StartsWith(control, StringComparison.OrdinalIgnoreCase) && scope is { } c ? VariableKeys.ControlVariable(c.DeviceId, c.ControlId, name[control.Length..])
            : VariableKeys.UserVariable(name);
        return _variables.TryGetValue<string>(key, out var value) ? value ?? string.Empty : string.Empty;
    }

    public string ReadVariable(string name) =>
        _variables.TryGetValue<string>(VariableKeys.UserVariable(name), out var value) ? value ?? string.Empty : string.Empty;

    /// <summary>A variable's value for a node: global unless the node's scope setting says profile or control.</summary>
    public string ReadVariable(string name, ActionSettings settings, WorkflowScope? scope) =>
        _variables.TryGetValue<string>(WorkflowScopes.KeyOf(settings, name, scope), out var value) ? value ?? string.Empty : string.Empty;

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

    private void Trace(Run run, Frame frame, string? nodeId, WorkflowTraceKind kind, string detail, TimeSpan? wait = null) =>
        Trace(run, nodeId, kind, detail, wait, frame.CallId);

    private void Trace(Run run, string? nodeId, WorkflowTraceKind kind, string detail, TimeSpan? wait = null, string? workflowId = null)
    {
        var handler = Traced;
        if (handler is null)
        {
            return;
        }

        try
        {
            handler(new WorkflowTrace(run.Input.DeviceId, run.ProfileId, run.Input.ControlId, run.Id, nodeId, kind, detail, DateTimeOffset.Now) { Wait = wait, WorkflowId = workflowId });
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

        public WorkflowScope Scope => new(ProfileId, Input.DeviceId, Input.ControlId);

        public Workflow Workflow { get; } = workflow;

        public DeviceInputEvent Input { get; } = input;

        public IActionHost Host { get; } = host;

        public Queue<Action> Work { get; } = new();

        public bool Pumping { get; set; }

        public bool Ended { get; set; }

        public int NodesEntered { get; set; }

        /// <summary>Held in front of a node (a breakpoint or a step); the work queue doesn't run until it is released.</summary>
        public bool Paused { get; set; }

        /// <summary>Stop in front of every node (after a Step).</summary>
        public bool StepMode { get; set; }

        /// <summary>What continues the run from where it is held.</summary>
        public Action? Resume { get; set; }
    }

    /// <summary>The workflow a node runs in: the control's own (<see cref="CallId"/> null) or a shared one a Call node entered.</summary>
    private sealed class Frame(Workflow workflow, Frame? parent, string? callId)
    {
        public Workflow Workflow { get; } = workflow;

        public Frame? Parent { get; } = parent;

        public string? CallId { get; } = callId;

        public int Depth => Parent is null ? 0 : Parent.Depth + 1;

        /// <summary>Whether the shared workflow <paramref name="id"/> is already being run up this chain of calls.</summary>
        public bool Calls(string id) => CallId == id || Parent?.Calls(id) == true;
    }
}

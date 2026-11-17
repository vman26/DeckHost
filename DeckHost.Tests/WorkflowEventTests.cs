using DeckHost.Contracts;
using DeckHost.Contracts.Enums;
using DeckHost.Contracts.Models;
using DeckHost.Core.Devices;
using DeckHost.Core.Models;
using DeckHost.Core.State;
using DeckHost.Core.Workflows;

namespace DeckHost.Tests;

/// <summary>Workflows that start by themselves: a variable changes, an app comes to the front, a timer, the profile is shown.</summary>
public sealed class WorkflowEventTests : IAsyncDisposable
{
    private const string Device = FakeDevicePlugin.DeviceId;
    private readonly VariableStore _store = new();
    private readonly RecordingAction _a = new("a");
    private readonly RecordingAction _b = new("b");
    private readonly RecordingAction _scaffold = new("scaffold");
    private readonly DeviceHostService _host;
    private readonly List<string> _ran = [];
    private readonly List<WorkflowTrace> _traces = [];

    public WorkflowEventTests()
    {
        foreach (var action in new[] { _a, _b })
        {
            action.OnExecute = context =>
            {
                if (context.EventType == InputCapabilities.Press)
                {
                    lock (_ran)
                    {
                        _ran.Add(action.Id);
                    }
                }
            };
        }

        _host = new DeviceHostService([new FakeDevicePlugin()], [new TestApplicationPlugin("p", _a, _b, _scaffold)], _store);
        _host.WorkflowTraced += trace =>
        {
            lock (_traces)
            {
                _traces.Add(trace);
            }
        };
    }

    public async ValueTask DisposeAsync()
    {
        await _host.DisposeAsync();
        _store.Dispose();
    }

    private static WorkflowNode Trigger(string on, params (string Key, string Value)[] settings) =>
        new("t", WorkflowNodeKinds.Trigger)
        {
            Settings = new ActionSettings(settings.Append((Key: WorkflowKeys.On, Value: on)).Select(static pair => new KeyValuePair<string, string>(pair.Key, pair.Value))),
        };

    private static WorkflowNode Run(string id, string actionId) => new(id, WorkflowNodeKinds.Action) { Action = Bindings.Reference("p", actionId) };

    private static Workflow Flow(WorkflowNode trigger, string actionId = "a") =>
        new() { Nodes = [trigger, Run("1", actionId)], Links = [new("t", WorkflowPorts.Next, "1")] };

    private static ControlBinding Binding(Workflow workflow, string control = "key.0") =>
        new(control, ProfileActionKind.Workflow, Bindings.Reference("p", "scaffold"), new ControlAppearance(), []) { Workflow = workflow };

    private async Task Start(params DeviceProfile[] profiles)
    {
        _host.SetProfiles(profiles);
        await _host.StartAsync(CancellationToken.None);
        await _host.WhenIdleAsync();
    }

    private async Task Start(Workflow workflow) => await Start(new DeviceProfile("profile", "Profile", Device, [Binding(workflow)]));

    private List<string> Ran()
    {
        lock (_ran)
        {
            return [.. _ran];
        }
    }

    private async Task Publish(string key, string value)
    {
        _store.Publish(key, value);
        await _host.WhenIdleAsync();
    }

    [Test]
    public async Task A_workflow_starts_when_its_variable_changes_and_not_for_another_variable()
    {
        await Start(Flow(Trigger(WorkflowTriggers.VariableChanged, (WorkflowKeys.Name, "Mode"))));

        await Publish(VariableKeys.UserVariable("other"), "1");
        Assert.That(Ran(), Is.Empty);

        await Publish(VariableKeys.UserVariable("mode"), "on");
        Assert.That(Ran(), Is.EqualTo(new[] { "a" }));

        await Publish(VariableKeys.UserVariable("mode"), "off");
        Assert.That(Ran(), Is.EqualTo(new[] { "a", "a" }));
    }

    [Test]
    public async Task A_variable_trigger_can_wait_for_one_value_and_ignores_a_value_that_did_not_change()
    {
        await Start(Flow(Trigger(WorkflowTriggers.VariableChanged, (WorkflowKeys.Name, "mode"), (WorkflowKeys.Value, "ON"))));

        await Publish(VariableKeys.UserVariable("mode"), "off");
        Assert.That(Ran(), Is.Empty);

        await Publish(VariableKeys.UserVariable("mode"), "on");
        await Publish(VariableKeys.UserVariable("mode"), "on");
        Assert.That(Ran(), Is.EqualTo(new[] { "a" }), "the same value again is no change");
    }

    [Test]
    public async Task A_workflow_starts_when_the_app_it_names_comes_to_the_front()
    {
        await Start(Flow(Trigger(WorkflowTriggers.AppInFront, (WorkflowKeys.App, "obs"))));

        await Publish(VariableKeys.ForegroundApp, "chrome.exe");
        Assert.That(Ran(), Is.Empty);

        await Publish(VariableKeys.ForegroundApp, "OBS64.exe");
        Assert.That(Ran(), Is.EqualTo(new[] { "a" }));

        await Publish(VariableKeys.ForegroundApp, "chrome.exe");
        await Publish(VariableKeys.ForegroundApp, "obs64.exe");
        Assert.That(Ran(), Is.EqualTo(new[] { "a", "a" }), "each time it comes back");
    }

    [Test]
    public async Task An_app_trigger_without_an_app_name_starts_on_nothing()
    {
        await Start(Flow(Trigger(WorkflowTriggers.AppInFront)));

        await Publish(VariableKeys.ForegroundApp, "obs64.exe");

        Assert.That(Ran(), Is.Empty);
    }

    [Test]
    public async Task A_workflow_starts_when_the_host_starts_and_when_its_profile_comes_back()
    {
        var shown = Binding(Flow(Trigger(WorkflowTriggers.ProfileShown), "a"));
        var other = Binding(Flow(Trigger(WorkflowTriggers.ProfileShown), "b"));
        await Start(
            new DeviceProfile("one", "One", Device, [shown]),
            new DeviceProfile("two", "Two", Device, [other]));
        Assert.That(Ran(), Is.EqualTo(new[] { "a" }), "the profile on the device at start");

        _host.SetActiveProfile("two");
        await _host.WhenIdleAsync();
        Assert.That(Ran(), Is.EqualTo(new[] { "a", "b" }));

        _host.SetActiveProfile("one");
        await _host.WhenIdleAsync();
        Assert.That(Ran(), Is.EqualTo(new[] { "a", "b", "a" }));
    }

    [Test]
    public async Task A_timer_starts_the_workflow_every_so_many_seconds_counting_from_when_it_was_first_seen()
    {
        await Start(Flow(Trigger(WorkflowTriggers.Timer, (WorkflowKeys.Every, "30"))));
        var t0 = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

        await _host.TickTimersAsync(t0);
        await _host.TickTimersAsync(t0.AddSeconds(29));
        Assert.That(Ran(), Is.Empty, "it counts from the first tick");

        await _host.TickTimersAsync(t0.AddSeconds(30));
        Assert.That(Ran(), Is.EqualTo(new[] { "a" }));

        await _host.TickTimersAsync(t0.AddSeconds(59));
        Assert.That(Ran(), Has.Count.EqualTo(1));
        await _host.TickTimersAsync(t0.AddSeconds(60));
        Assert.That(Ran(), Has.Count.EqualTo(2));
    }

    [Test]
    public async Task Event_triggers_do_nothing_for_a_profile_that_is_not_shown()
    {
        await Start(
            new DeviceProfile("one", "One", Device, []),
            new DeviceProfile("two", "Two", Device, [Binding(Flow(Trigger(WorkflowTriggers.VariableChanged, (WorkflowKeys.Name, "x"))))]));
        _host.SetActiveProfile("one");
        await _host.WhenIdleAsync();

        await Publish(VariableKeys.UserVariable("x"), "1");

        Assert.That(Ran(), Is.Empty);
    }

    [Test]
    public async Task A_workflow_that_changes_the_variable_it_starts_on_is_paused_instead_of_looping_forever()
    {
        var count = new WorkflowNode("1", WorkflowNodeKinds.SetVariable)
        {
            Settings = new ActionSettings(new Dictionary<string, string>
            {
                [WorkflowKeys.Name] = "x",
                [WorkflowKeys.Operation] = WorkflowKeys.Add,
                [WorkflowKeys.Value] = "1",
            }),
        };
        await Start(new Workflow
        {
            Nodes = [Trigger(WorkflowTriggers.VariableChanged, (WorkflowKeys.Name, "x")), count],
            Links = [new("t", WorkflowPorts.Next, "1")],
        });

        await Publish(VariableKeys.UserVariable("x"), "0");
        for (var i = 0; i < 40; i++)
        {
            await Task.Delay(10);
            await _host.WhenIdleAsync();
        }

        List<WorkflowTrace> traces;
        lock (_traces)
        {
            traces = [.. _traces];
        }

        var started = traces.Count(static trace => trace.Kind == WorkflowTraceKind.Started);
        Assert.That(started, Is.EqualTo(WorkflowEventThrottle.Limit), "it stops at the limit");
    }

    [Test]
    public void The_throttle_allows_a_burst_up_to_its_limit_pauses_and_lets_the_trigger_back_in_later()
    {
        var throttle = new WorkflowEventThrottle();
        var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        for (var i = 0; i < WorkflowEventThrottle.Limit; i++)
        {
            Assert.That(throttle.Allow("k", t0.AddMilliseconds(i), out _), Is.True);
        }

        Assert.That(throttle.Allow("k", t0.AddSeconds(1), out var paused), Is.False);
        Assert.That(paused, Is.True, "the call that pauses says so");
        Assert.That(throttle.Allow("k", t0.AddSeconds(2), out paused), Is.False);
        Assert.That(paused, Is.False, "and only that one");
        Assert.That(throttle.Allow("other", t0.AddSeconds(2), out _), Is.True, "other triggers are not affected");
        Assert.That(throttle.Allow("k", t0.AddSeconds(1) + WorkflowEventThrottle.Pause + TimeSpan.FromSeconds(1), out _), Is.True);
    }

    [Test]
    public void The_matching_checks_read_the_trigger_settings()
    {
        var variable = Trigger(WorkflowTriggers.VariableChanged, (WorkflowKeys.Name, " Mode "), (WorkflowKeys.Value, "On"));
        Assert.That(WorkflowEvents.MatchesVariable(variable, VariableKeys.UserVariable("mode"), " on "), Is.True);
        Assert.That(WorkflowEvents.MatchesVariable(variable, VariableKeys.UserVariable("mode"), "off"), Is.False);
        Assert.That(WorkflowEvents.MatchesVariable(Trigger(WorkflowTriggers.VariableChanged), VariableKeys.UserVariable("mode"), "on"), Is.False, "no name, no match");

        Assert.That(WorkflowEvents.MatchesApp(Trigger(WorkflowTriggers.AppInFront, (WorkflowKeys.App, "Code")), "code.exe"), Is.True);
        Assert.That(WorkflowEvents.MatchesApp(Trigger(WorkflowTriggers.AppInFront, (WorkflowKeys.App, "")), "code.exe"), Is.False);

        Assert.That(WorkflowEvents.EveryOf(Trigger(WorkflowTriggers.Timer, (WorkflowKeys.Every, "0"))), Is.EqualTo(TimeSpan.FromSeconds(1)));
        Assert.That(WorkflowEvents.EveryOf(Trigger(WorkflowTriggers.Timer, (WorkflowKeys.Every, "999999"))), Is.EqualTo(TimeSpan.FromSeconds(WorkflowKeys.MaxEverySeconds)));
        Assert.That(WorkflowEvents.EveryOf(Trigger(WorkflowTriggers.Timer)), Is.EqualTo(TimeSpan.FromSeconds(60)));
    }

    [Test]
    public void The_trigger_node_describes_its_event()
    {
        Assert.That(WorkflowNodeTypes.Summarize(Trigger(WorkflowTriggers.ProfileShown)), Is.EqualTo("When the profile is shown"));
        Assert.That(WorkflowNodeTypes.Summarize(Trigger(WorkflowTriggers.AppInFront, (WorkflowKeys.App, "obs"))), Is.EqualTo("When obs comes to the front"));
        Assert.That(WorkflowNodeTypes.Summarize(Trigger(WorkflowTriggers.VariableChanged, (WorkflowKeys.Name, "mode"))), Is.EqualTo("When mode changes"));
        Assert.That(WorkflowNodeTypes.Summarize(Trigger(WorkflowTriggers.VariableChanged, (WorkflowKeys.Name, "mode"), (WorkflowKeys.Value, "on"))), Is.EqualTo("When mode becomes on"));
        Assert.That(WorkflowNodeTypes.Summarize(Trigger(WorkflowTriggers.Timer, (WorkflowKeys.Every, "90"))), Is.EqualTo("Every 90 s"));
        Assert.That(WorkflowTriggers.IsEvent(WorkflowTriggers.Timer), Is.True);
        Assert.That(WorkflowTriggers.IsEvent(WorkflowTriggers.Down), Is.False);
    }
}

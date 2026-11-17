using System.Text.Json;
using System.Text.Json.Serialization;
using DeckHost.Contracts;
using DeckHost.Contracts.Enums;
using DeckHost.Contracts.Models;
using DeckHost.Core.Devices;
using DeckHost.Core.Models;
using DeckHost.Core.State;
using DeckHost.Core.Workflows;

namespace DeckHost.Tests;

public sealed class WorkflowTests : IAsyncDisposable
{
    private const string Device = FakeDevicePlugin.DeviceId;
    private readonly VariableStore _store = new();
    private readonly RecordingAction _a = new("a");
    private readonly RecordingAction _b = new("b");
    private readonly RecordingAction _c = new("c");
    private readonly RecordingAction _scaffold = new("scaffold");
    private readonly DeviceHostService _host;
    private readonly List<string> _ran = [];
    private readonly List<WorkflowTrace> _traces = [];

    public WorkflowTests()
    {
        foreach (var action in new[] { _a, _b, _c })
        {
            action.OnExecute = context =>
            {
                if (context.EventType is InputCapabilities.Press or InputCapabilities.Rotate)
                {
                    lock (_ran)
                    {
                        _ran.Add(action.Id);
                    }
                }
            };
        }

        _host = new DeviceHostService([new FakeDevicePlugin()], [new TestApplicationPlugin("p", _a, _b, _c, _scaffold)], _store);
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

    private static WorkflowNode Node(string id, string kind, params (string Key, string Value)[] settings) =>
        new(id, kind) { Settings = new ActionSettings(settings.Select(static pair => new KeyValuePair<string, string>(pair.Key, pair.Value))) };

    private static WorkflowNode Trigger(string id, string on) => Node(id, WorkflowNodeKinds.Trigger, (WorkflowKeys.On, on));

    private static WorkflowNode Run(string id, string actionId) => new(id, WorkflowNodeKinds.Action) { Action = Bindings.Reference("p", actionId) };

    private static WorkflowLink Link(string from, string to, string port = WorkflowPorts.Next) => new(from, port, to);

    private static ControlBinding Binding(Workflow workflow) =>
        new("key.0", ProfileActionKind.Workflow, Bindings.Reference("p", "scaffold"), new ControlAppearance(), []) { Workflow = workflow };

    private async Task Start(Workflow workflow)
    {
        _host.SetProfiles([new DeviceProfile("profile", "Profile", Device, [Binding(workflow)])]);
        await _host.StartAsync(CancellationToken.None);
        await _host.WhenIdleAsync();
    }

    private async Task Input(InputCapabilities type, int? value = null)
    {
        _host.SimulateInput(new DeviceInputEvent(Device, "key.0", type, value ?? (type == InputCapabilities.Release ? 0 : 1)));
        await _host.WhenIdleAsync();
    }

    private async Task Click()
    {
        await Input(InputCapabilities.Press);
        await Input(InputCapabilities.Release);
    }

    private List<string> Ran()
    {
        lock (_ran)
        {
            return [.. _ran];
        }
    }

    private List<WorkflowTrace> Traces()
    {
        lock (_traces)
        {
            return [.. _traces];
        }
    }

    private async Task WaitUntil(Func<bool> condition)
    {
        var end = DateTime.UtcNow.AddSeconds(5);
        while (!condition() && DateTime.UtcNow < end)
        {
            await Task.Delay(20);
            await _host.WhenIdleAsync();
        }
    }

    [Test]
    public async Task A_chain_runs_its_actions_in_order_when_pressed()
    {
        await Start(new Workflow
        {
            Nodes = [Trigger("t", WorkflowTriggers.Down), Run("1", "a"), Run("2", "b")],
            Links = [Link("t", "1"), Link("1", "2")],
        });

        await Click();

        Assert.That(Ran(), Is.EqualTo(new[] { "a", "b" }));
        Assert.That(Traces().Any(trace => trace.Kind == WorkflowTraceKind.Finished && trace.ControlId == "key.0"), Is.True);
    }

    [Test]
    public async Task If_takes_yes_or_no_by_a_variable_that_set_variable_changed()
    {
        await Start(new Workflow
        {
            Nodes =
            [
                Trigger("t", WorkflowTriggers.Down),
                Node("if", WorkflowNodeKinds.If, (WorkflowKeys.Name, "Mode"), (WorkflowKeys.Operation, WorkflowKeys.IsEqual), (WorkflowKeys.Value, "2")),
                Run("yes", "a"),
                Run("no", "b"),
                Node("add", WorkflowNodeKinds.SetVariable, (WorkflowKeys.Name, "mode"), (WorkflowKeys.Operation, WorkflowKeys.Add)),
            ],
            Links = [Link("t", "if"), Link("if", "yes", WorkflowPorts.Yes), Link("if", "no", WorkflowPorts.No), Link("no", "add")],
        });

        await Click(); // mode empty → no, mode = 1
        await Click(); // 1 → no, mode = 2
        await Click(); // 2 → yes

        Assert.That(Ran(), Is.EqualTo(new[] { "b", "b", "a" }));
        Assert.That(_store.TryGetValue<string>(VariableKeys.UserVariable("MODE"), out var mode), Is.True);
        Assert.That(mode, Is.EqualTo("2"));
    }

    [Test]
    public async Task Toggle_alternates_and_repeat_runs_its_body_one_round_after_the_other()
    {
        await Start(new Workflow
        {
            Nodes =
            [
                Trigger("t", WorkflowTriggers.Down),
                Node("toggle", WorkflowNodeKinds.Toggle),
                Node("repeat", WorkflowNodeKinds.Repeat, (WorkflowKeys.Count, "3")),
                Run("each", "a"),
                Node("wait", WorkflowNodeKinds.Wait, (WorkflowKeys.Milliseconds, "20")),
                Run("done", "b"),
                Run("second", "c"),
            ],
            Links =
            [
                Link("t", "toggle"),
                Link("toggle", "repeat", WorkflowPorts.First),
                Link("toggle", "second", WorkflowPorts.Second),
                Link("repeat", "each", WorkflowPorts.Each),
                Link("each", "wait"),
                Link("repeat", "done", WorkflowPorts.Done),
            ],
        });

        await Click();
        await WaitUntil(() => Ran().Count >= 4);
        Assert.That(Ran(), Is.EqualTo(new[] { "a", "a", "a", "b" }));

        await Click();
        Assert.That(Ran(), Is.EqualTo(new[] { "a", "a", "a", "b", "c" }));
    }

    [Test]
    public async Task Repeat_can_take_its_count_from_a_variable()
    {
        _store.Publish(VariableKeys.UserVariable("rounds"), "3");
        await Start(new Workflow
        {
            Nodes = [Trigger("t", WorkflowTriggers.Down), Node("repeat", WorkflowNodeKinds.Repeat, (WorkflowKeys.Mode, WorkflowKeys.ModeVariable), (WorkflowKeys.Times, "Rounds")), Run("each", "a"), Run("done", "b")],
            Links = [Link("t", "repeat"), Link("repeat", "each", WorkflowPorts.Each), Link("repeat", "done", WorkflowPorts.Done)],
        });

        await Click();
        await WaitUntil(() => Ran().Count >= 4);

        Assert.That(Ran(), Is.EqualTo(new[] { "a", "a", "a", "b" }));
    }

    [Test]
    public async Task Repeat_while_a_variable_holds_checks_it_before_every_round_and_waits_between_rounds()
    {
        // The body counts "n" up; the loop runs while n is less than 3 (so three rounds), 30 ms apart.
        await Start(new Workflow
        {
            Nodes =
            [
                Trigger("t", WorkflowTriggers.Down),
                Node("repeat", WorkflowNodeKinds.Repeat, (WorkflowKeys.Mode, WorkflowKeys.ModeWhile), (WorkflowKeys.Name, "n"), (WorkflowKeys.Operation, WorkflowKeys.IsLess), (WorkflowKeys.Value, "3"), (WorkflowKeys.Interval, "30")),
                Node("count", WorkflowNodeKinds.SetVariable, (WorkflowKeys.Name, "n"), (WorkflowKeys.Operation, WorkflowKeys.Add), (WorkflowKeys.Value, "1")),
                Run("each", "a"),
                Run("done", "b"),
            ],
            Links = [Link("t", "repeat"), Link("repeat", "count", WorkflowPorts.Each), Link("count", "each"), Link("repeat", "done", WorkflowPorts.Done)],
        });
        _store.Publish(VariableKeys.UserVariable("n"), "0");

        await Click();
        await WaitUntil(() => Ran().Count >= 4);

        Assert.That(Ran(), Is.EqualTo(new[] { "a", "a", "a", "b" }));
        var pauses = Traces().Where(static trace => trace.Wait is not null).ToList();
        Assert.That(pauses, Has.Count.EqualTo(3), "a wait after each round, the last one before the condition ends the loop");
        Assert.That(pauses[0].Wait, Is.EqualTo(TimeSpan.FromMilliseconds(30)));
        Assert.That(pauses[0].NodeId, Is.EqualTo("repeat"));
    }

    [Test]
    public async Task Repeat_until_a_variable_holds_stops_when_it_does()
    {
        await Start(new Workflow
        {
            Nodes =
            [
                Trigger("t", WorkflowTriggers.Down),
                Node("repeat", WorkflowNodeKinds.Repeat, (WorkflowKeys.Mode, WorkflowKeys.ModeUntil), (WorkflowKeys.Name, "n"), (WorkflowKeys.Operation, WorkflowKeys.IsEqual), (WorkflowKeys.Value, "2")),
                Node("count", WorkflowNodeKinds.SetVariable, (WorkflowKeys.Name, "n"), (WorkflowKeys.Operation, WorkflowKeys.Add), (WorkflowKeys.Value, "1")),
                Run("each", "a"),
                Run("done", "b"),
            ],
            Links = [Link("t", "repeat"), Link("repeat", "count", WorkflowPorts.Each), Link("count", "each"), Link("repeat", "done", WorkflowPorts.Done)],
        });
        _store.Publish(VariableKeys.UserVariable("n"), "0");

        await Click();
        await WaitUntil(() => Ran().Count >= 3);

        Assert.That(Ran(), Is.EqualTo(new[] { "a", "a", "b" }));
    }

    [Test]
    public async Task A_condition_that_never_changes_cannot_repeat_forever()
    {
        await Start(new Workflow
        {
            Nodes = [Trigger("t", WorkflowTriggers.Down), Node("repeat", WorkflowNodeKinds.Repeat, (WorkflowKeys.Mode, WorkflowKeys.ModeWhile), (WorkflowKeys.Name, "forever"), (WorkflowKeys.Operation, WorkflowKeys.IsEmpty)), Run("done", "b")],
            Links = [Link("t", "repeat"), Link("repeat", "done", WorkflowPorts.Done)],
        });

        await Click();
        await WaitUntil(() => Traces().Any(static trace => trace.Kind is WorkflowTraceKind.Failed));

        // The empty body makes each round free; the step limit or the round limit ends it.
        Assert.That(Traces().Any(static trace => trace.Kind == WorkflowTraceKind.Failed), Is.True);
    }

    [Test]
    public void A_repeat_node_says_what_it_does_in_one_line()
    {
        static string Line(params (string Key, string Value)[] settings) => WorkflowNodeTypes.Summarize(Node("r", WorkflowNodeKinds.Repeat, settings));

        Assert.That(Line((WorkflowKeys.Count, "3")), Is.EqualTo("Repeat 3×"));
        Assert.That(Line(), Is.EqualTo("Repeat 2×"), "a node from before the modes");
        Assert.That(Line((WorkflowKeys.Mode, WorkflowKeys.ModeVariable), (WorkflowKeys.Times, "rounds"), (WorkflowKeys.Interval, "500")), Is.EqualTo("Repeat rounds×, every 500 ms"));
        Assert.That(Line((WorkflowKeys.Mode, WorkflowKeys.ModeWhile), (WorkflowKeys.Name, "mode"), (WorkflowKeys.Value, "on")), Is.EqualTo("Repeat while mode = on"));
        Assert.That(Line((WorkflowKeys.Mode, WorkflowKeys.ModeUntil), (WorkflowKeys.Name, "n"), (WorkflowKeys.Operation, WorkflowKeys.IsGreater), (WorkflowKeys.Value, "5"), (WorkflowKeys.Interval, "2000")), Is.EqualTo("Repeat until n > 5, every 2 s"));
    }

    [Test]
    public async Task A_wait_says_how_long_it_is_in_its_trace()
    {
        await Start(new Workflow
        {
            Nodes = [Trigger("t", WorkflowTriggers.Down), Node("wait", WorkflowNodeKinds.Wait, (WorkflowKeys.Milliseconds, "40")), Run("after", "a")],
            Links = [Link("t", "wait"), Link("wait", "after")],
        });

        await Click();
        await WaitUntil(() => Ran().Count == 1);

        Assert.That(Traces().Single(static trace => trace is { NodeId: "wait", Wait: not null }).Wait, Is.EqualTo(TimeSpan.FromMilliseconds(40)));
    }

    [Test]
    public async Task Waits_do_not_block_the_device_and_a_second_press_can_stop_the_run()
    {
        await Start(new Workflow
        {
            WhileRunning = WorkflowConcurrency.Stop,
            Nodes = [Trigger("t", WorkflowTriggers.Down), Node("wait", WorkflowNodeKinds.Wait, (WorkflowKeys.Milliseconds, "300")), Run("after", "a")],
            Links = [Link("t", "wait"), Link("wait", "after")],
        });

        await Click();
        await Click(); // stops the waiting run
        await Task.Delay(500);
        await _host.WhenIdleAsync();

        Assert.That(Ran(), Is.Empty);
        Assert.That(Traces().Any(trace => trace.Kind == WorkflowTraceKind.Stopped), Is.True);
    }

    [Test]
    public async Task Ignore_keeps_the_first_run_and_restart_starts_over()
    {
        await Start(new Workflow
        {
            WhileRunning = WorkflowConcurrency.Ignore,
            Nodes = [Trigger("t", WorkflowTriggers.Down), Node("wait", WorkflowNodeKinds.Wait, (WorkflowKeys.Milliseconds, "150")), Run("after", "a")],
            Links = [Link("t", "wait"), Link("wait", "after")],
        });

        await Click();
        await Click();
        await WaitUntil(() => Ran().Count >= 1);
        await Task.Delay(300);
        await _host.WhenIdleAsync();

        Assert.That(Ran(), Is.EqualTo(new[] { "a" }));
        Assert.That(Traces().Any(trace => trace.Kind == WorkflowTraceKind.Ignored), Is.True);
    }

    [Test]
    public async Task Tap_double_press_and_hold_are_told_apart()
    {
        await Start(new Workflow
        {
            Nodes =
            [
                Trigger("tap", WorkflowTriggers.Tap), Run("1", "a"),
                Trigger("double", WorkflowTriggers.DoublePress), Run("2", "b"),
                Trigger("hold", WorkflowTriggers.Hold), Run("3", "c"),
            ],
            Links = [Link("tap", "1"), Link("double", "2"), Link("hold", "3")],
        });

        await Click();
        await WaitUntil(() => Ran().Count >= 1);
        Assert.That(Ran(), Is.EqualTo(new[] { "a" }));

        await Click();
        await Click();
        await Task.Delay(DeviceHostService.DoublePressWindow * 2);
        await _host.WhenIdleAsync();
        Assert.That(Ran(), Is.EqualTo(new[] { "a", "b" }));

        await Input(InputCapabilities.Press);
        await Input(InputCapabilities.LongPress);
        await Input(InputCapabilities.Release);
        await Task.Delay(DeviceHostService.DoublePressWindow * 2);
        await _host.WhenIdleAsync();
        Assert.That(Ran(), Is.EqualTo(new[] { "a", "b", "c" }));
    }

    [Test]
    public async Task A_knob_turn_reaches_its_action_as_a_turn()
    {
        await Start(new Workflow
        {
            Nodes = [Trigger("left", WorkflowTriggers.RotateLeft), Run("1", "a"), Trigger("right", WorkflowTriggers.RotateRight), Run("2", "b")],
            Links = [Link("left", "1"), Link("right", "2")],
        });

        await Input(InputCapabilities.Rotate, -1);
        await Input(InputCapabilities.Rotate, 1);

        Assert.That(Ran(), Is.EqualTo(new[] { "a", "b" }));
        Assert.That(_a.Executions.Any(context => context.EventType == InputCapabilities.Rotate && context.Value == -1), Is.True);
    }

    [Test]
    public async Task Stop_node_ends_branches_that_are_still_waiting()
    {
        await Start(new Workflow
        {
            Nodes =
            [
                Trigger("t", WorkflowTriggers.Down),
                Node("wait", WorkflowNodeKinds.Wait, (WorkflowKeys.Milliseconds, "200")),
                Run("late", "a"),
                Node("stop", WorkflowNodeKinds.Stop),
            ],
            Links = [Link("t", "wait"), Link("wait", "late"), Link("t", "stop")],
        });

        await Click();
        await Task.Delay(400);
        await _host.WhenIdleAsync();

        Assert.That(Ran(), Is.Empty);
    }

    [Test]
    public async Task The_editor_can_run_a_trigger_and_a_missing_action_is_reported()
    {
        await Start(new Workflow
        {
            Nodes = [Trigger("t", WorkflowTriggers.Hold), new WorkflowNode("gone", WorkflowNodeKinds.Action) { Action = Bindings.Reference("missing", "x") }, Run("1", "a")],
            Links = [Link("t", "gone"), Link("gone", "1")],
        });

        _host.RunWorkflowTrigger(Device, "key.0", WorkflowTriggers.Hold);
        await _host.WhenIdleAsync();

        Assert.That(Ran(), Is.EqualTo(new[] { "a" }));
        Assert.That(Traces().Any(trace => trace.Kind == WorkflowTraceKind.Failed && trace.NodeId == "gone"), Is.True);
    }

    [Test]
    public void Conditions_on_the_app_in_front_and_the_time_of_day()
    {
        using var store = new VariableStore();
        var engine = new WorkflowEngine(store, (_, _, _, _) => true) { Clock = () => new DateTime(2026, 1, 1, 23, 30, 0) };
        store.Publish(VariableKeys.ForegroundApp, "obs64");

        Assert.That(engine.Evaluate(Settings((WorkflowKeys.Check, WorkflowKeys.CheckApp), (WorkflowKeys.App, "OBS"))), Is.True);
        Assert.That(engine.Evaluate(Settings((WorkflowKeys.Check, WorkflowKeys.CheckApp), (WorkflowKeys.App, "zoom"))), Is.False);
        Assert.That(engine.Evaluate(Settings((WorkflowKeys.Check, WorkflowKeys.CheckTime), (WorkflowKeys.From, "22:00"), (WorkflowKeys.To, "06:00"))), Is.True);
        Assert.That(engine.Evaluate(Settings((WorkflowKeys.Check, WorkflowKeys.CheckTime), (WorkflowKeys.From, "09:00"), (WorkflowKeys.To, "17:30"))), Is.False);
        Assert.That(engine.Evaluate(Settings((WorkflowKeys.Check, WorkflowKeys.CheckTime), (WorkflowKeys.From, "nonsense"), (WorkflowKeys.To, "17:30"))), Is.False);

        engine.Apply(Settings((WorkflowKeys.Name, "count"), (WorkflowKeys.Operation, WorkflowKeys.Add), (WorkflowKeys.Value, "2.5")));
        Assert.That(engine.ReadVariable("count"), Is.EqualTo("2.5"));
        Assert.That(engine.Evaluate(Settings((WorkflowKeys.Name, "count"), (WorkflowKeys.Operation, WorkflowKeys.IsGreater), (WorkflowKeys.Value, "2"))), Is.True);
        engine.Apply(Settings((WorkflowKeys.Name, "on"), (WorkflowKeys.Operation, WorkflowKeys.Flip)));
        Assert.That(engine.ReadVariable("on"), Is.EqualTo("true"));
    }

    [Test]
    public void Multi_action_switch_and_key_logic_convert_into_the_same_behavior()
    {
        var steps = new[]
        {
            new MultiActionStep(0, Bindings.Reference("p", "a")) { Group = StepGroups.First },
            new MultiActionStep(1, Bindings.Reference("p", "b")) { Group = StepGroups.Second, DelayMs = 250 },
        };
        var toggle = WorkflowConverter.FromBinding(new ControlBinding("key.0", ProfileActionKind.MultiActionSwitch, Bindings.Reference("p", "scaffold"), new ControlAppearance(), steps));

        var toggleNode = Expect.Single(toggle.Nodes, node => node.Kind == WorkflowNodeKinds.Toggle);
        var first = toggle.Find(Expect.Single(toggle.LinksFrom(toggleNode.Id, WorkflowPorts.First)).To)!;
        Assert.That(first.Action!.ActionId, Is.EqualTo("a"));
        var wait = toggle.Find(Expect.Single(toggle.LinksFrom(toggleNode.Id, WorkflowPorts.Second)).To)!;
        Assert.That(wait.Kind, Is.EqualTo(WorkflowNodeKinds.Wait));
        Assert.That(wait.EffectiveSettings.Get(WorkflowKeys.Milliseconds), Is.EqualTo("250"));
        Assert.That(toggle.Nodes.Select(static node => node.Id).Distinct().Count(), Is.EqualTo(toggle.Nodes.Count));

        var keyLogic = WorkflowConverter.FromBinding(new ControlBinding("key.0", ProfileActionKind.KeyLogic, Bindings.Reference("p", "scaffold"), new ControlAppearance(),
            [new MultiActionStep(0, Bindings.Reference("p", "a")) { Group = StepGroups.Press }, new MultiActionStep(1, Bindings.Reference("p", "c")) { Group = StepGroups.Hold }]));
        Assert.That(
            WorkflowEngine.TriggersOf(keyLogic).ToHashSet(),
            Is.EqualTo(new HashSet<string> { WorkflowTriggers.Tap, WorkflowTriggers.Hold }));
        Assert.That(WorkflowConverter.NextId(keyLogic), Is.EqualTo("n5"));
    }

    [Test]
    public void A_link_that_closes_a_loop_is_detected()
    {
        var workflow = new Workflow
        {
            Nodes = [Trigger("t", WorkflowTriggers.Down), Run("1", "a"), Run("2", "b")],
            Links = [Link("t", "1"), Link("1", "2")],
        };

        Assert.That(workflow.WouldCycle("2", "1"), Is.True);
        Assert.That(workflow.WouldCycle("1", "1"), Is.True);
        Assert.That(workflow.WouldCycle("t", "2"), Is.False);
    }

    private static WorkflowNode Flaky(string id, string actionId, params (string Key, string Value)[] settings) =>
        Run(id, actionId) with { Settings = Settings(settings) };

    [Test]
    public async Task An_action_that_did_not_run_goes_on_at_its_error_output_when_that_is_linked()
    {
        _a.OnExecute = _ => throw new InvalidOperationException("broken");
        await Start(new Workflow
        {
            Nodes = [Trigger("t", WorkflowTriggers.Down), Run("1", "a"), Run("ok", "b"), Run("bad", "c")],
            Links = [Link("t", "1"), Link("1", "ok"), Link("1", "bad", WorkflowPorts.Error)],
        });

        await Input(InputCapabilities.Press);
        await WaitUntil(() => Ran().Count > 0);

        Assert.That(Ran(), Is.EqualTo(new[] { "c" }), "the error branch, not the next one");
        Assert.That(Traces().Any(trace => trace.Kind == WorkflowTraceKind.Failed && trace.Detail.Contains("On error")), Is.True);
    }

    [Test]
    public async Task An_action_that_did_not_run_still_goes_on_at_next_when_nothing_is_linked_to_its_error_output()
    {
        _a.OnExecute = _ => throw new InvalidOperationException("broken");
        await Start(new Workflow
        {
            Nodes = [Trigger("t", WorkflowTriggers.Down), Run("1", "a"), Run("2", "b")],
            Links = [Link("t", "1"), Link("1", "2")],
        });

        await Input(InputCapabilities.Press);
        await WaitUntil(() => Ran().Count > 0);

        Assert.That(Ran(), Is.EqualTo(new[] { "b" }));
    }

    [Test]
    public async Task A_flaky_action_is_tried_again_and_the_run_goes_on_once_it_works()
    {
        var presses = 0;
        _a.OnExecute = context =>
        {
            // Fails both halves of the press and release while it is still "flaky".
            if (context.EventType == InputCapabilities.Press && Interlocked.Increment(ref presses) <= 2 ||
                context.EventType != InputCapabilities.Press && Volatile.Read(ref presses) <= 2)
            {
                throw new InvalidOperationException("not yet");
            }
        };
        await Start(new Workflow
        {
            Nodes = [Trigger("t", WorkflowTriggers.Down), Flaky("1", "a", (WorkflowKeys.Retries, "3"), (WorkflowKeys.RetryDelay, "0")), Run("ok", "b"), Run("bad", "c")],
            Links = [Link("t", "1"), Link("1", "ok"), Link("1", "bad", WorkflowPorts.Error)],
        });

        await Input(InputCapabilities.Press);
        await WaitUntil(() => Ran().Contains("b"));

        Assert.That(Volatile.Read(ref presses), Is.EqualTo(3), "two failures, then it worked");
        Assert.That(Ran(), Is.EqualTo(new[] { "b" }));
        Assert.That(Traces().Count(trace => trace.Detail.Contains("trying again")), Is.EqualTo(2));
    }

    [Test]
    public async Task An_action_that_keeps_failing_goes_to_its_error_output_after_its_tries()
    {
        var presses = 0;
        _a.OnExecute = context =>
        {
            if (context.EventType == InputCapabilities.Press)
            {
                Interlocked.Increment(ref presses);
            }

            throw new InvalidOperationException("never");
        };
        await Start(new Workflow
        {
            Nodes = [Trigger("t", WorkflowTriggers.Down), Flaky("1", "a", (WorkflowKeys.Retries, "2"), (WorkflowKeys.RetryDelay, "0")), Run("ok", "b"), Run("bad", "c")],
            Links = [Link("t", "1"), Link("1", "ok"), Link("1", "bad", WorkflowPorts.Error)],
        });

        await Input(InputCapabilities.Press);
        await WaitUntil(() => Ran().Contains("c"));

        Assert.That(Volatile.Read(ref presses), Is.EqualTo(3), "the first try and two more");
        Assert.That(Ran(), Is.EqualTo(new[] { "c" }));
    }

    [Test]
    public void An_action_node_has_an_error_output_next_to_its_next_one_and_the_others_are_unchanged()
    {
        Assert.That(WorkflowNodeKinds.OutputsOf(WorkflowNodeKinds.Action), Is.EqualTo(new[] { WorkflowPorts.Next, WorkflowPorts.Error }));
        Assert.That(WorkflowNodeKinds.OutputsOf(WorkflowNodeKinds.Wait), Is.EqualTo(new[] { WorkflowPorts.Next }));
        Assert.That(WorkflowPorts.Describe(WorkflowPorts.Error), Is.EqualTo("On error"));
    }

    [Test]
    public async Task The_host_lists_the_user_variables_for_the_editor_without_the_prefix_and_in_order()
    {
        await Start(new Workflow { Nodes = [Trigger("t", WorkflowTriggers.Down)] });
        _store.Publish(VariableKeys.UserVariable("Zed"), "last");
        _store.Publish(VariableKeys.UserVariable("alpha"), "1");
        _store.Publish(VariableKeys.ForegroundApp, "obs");

        var variables = _host.UserVariables();

        Assert.That(variables.Select(static pair => (pair.Key, pair.Value)), Is.EqualTo(new[] { ("alpha", "1"), ("zed", "last") }));
    }

    [Test]
    public void Variables_can_belong_to_a_profile_or_to_one_control_instead_of_to_everyone()
    {
        using var store = new VariableStore();
        var engine = new WorkflowEngine(store, (_, _, _, _) => true);
        var one = new WorkflowScope("p1", Device, "key.0");
        var two = new WorkflowScope("p2", Device, "key.1");

        string Set(string scope, WorkflowScope where, string value) =>
            engine.Apply(Settings((WorkflowKeys.Name, "Mode"), (WorkflowKeys.Scope, scope), (WorkflowKeys.Value, value)), where);
        string Read(string scope, WorkflowScope where) =>
            engine.ReadVariable("mode", Settings((WorkflowKeys.Scope, scope)), where);

        Set(WorkflowKeys.ScopeGlobal, one, "everyone");
        Set(WorkflowKeys.ScopeProfile, one, "profile one");
        Set(WorkflowKeys.ScopeControl, one, "control one");
        Set(WorkflowKeys.ScopeProfile, two, "profile two");

        Assert.That(Read(WorkflowKeys.ScopeGlobal, two), Is.EqualTo("everyone"), "global is shared");
        Assert.That(Read(WorkflowKeys.ScopeProfile, one), Is.EqualTo("profile one"));
        Assert.That(Read(WorkflowKeys.ScopeProfile, two), Is.EqualTo("profile two"), "another profile has its own");
        Assert.That(Read(WorkflowKeys.ScopeControl, one), Is.EqualTo("control one"));
        Assert.That(Read(WorkflowKeys.ScopeControl, two), Is.Empty, "another control has none yet");
        Assert.That(engine.Evaluate(Settings((WorkflowKeys.Name, "mode"), (WorkflowKeys.Scope, WorkflowKeys.ScopeProfile), (WorkflowKeys.Value, "profile one")), one), Is.True);
        Assert.That(engine.Evaluate(Settings((WorkflowKeys.Name, "mode"), (WorkflowKeys.Scope, WorkflowKeys.ScopeProfile), (WorkflowKeys.Value, "profile one")), two), Is.False);
        Assert.That(engine.Evaluate(Settings((WorkflowKeys.Name, "mode"), (WorkflowKeys.Value, "everyone")), two), Is.True, "no scope setting means global");
    }

    [Test]
    public async Task A_control_scoped_variable_set_by_a_running_workflow_stays_with_its_control()
    {
        var set = (string value) => new Workflow
        {
            Nodes = [Trigger("t", WorkflowTriggers.Down), Node("s", WorkflowNodeKinds.SetVariable, (WorkflowKeys.Name, "who"), (WorkflowKeys.Scope, WorkflowKeys.ScopeControl), (WorkflowKeys.Value, value))],
            Links = [Link("t", "s")],
        };
        _host.SetProfiles([new DeviceProfile("profile", "Profile", Device, [Binding(set("first")), Binding(set("second")) with { ControlId = "key.1" }])]);
        await _host.StartAsync(CancellationToken.None);
        await _host.WhenIdleAsync();

        await Input(InputCapabilities.Press);
        _host.SimulateInput(new DeviceInputEvent(Device, "key.1", InputCapabilities.Press, 1));
        await _host.WhenIdleAsync();

        Assert.That(_store.TryGetValue<string>(VariableKeys.ControlVariable(Device, "key.0", "who"), out var a) ? a : null, Is.EqualTo("first"));
        Assert.That(_store.TryGetValue<string>(VariableKeys.ControlVariable(Device, "key.1", "who"), out var b) ? b : null, Is.EqualTo("second"));
        Assert.That(_store.TryGetValue<string>(VariableKeys.UserVariable("who"), out _), Is.False, "nothing leaked into the global one");
    }

    [Test]
    public void Scoped_variables_are_listed_with_where_they_belong()
    {
        Assert.That(WorkflowScopes.Describe(VariableKeys.UserVariable("Mode")), Is.EqualTo("mode"));
        Assert.That(WorkflowScopes.Describe(VariableKeys.ProfileVariable("p1", "Mode")), Is.EqualTo("mode (profile p1)"));
        Assert.That(WorkflowScopes.Describe(VariableKeys.ControlVariable("mirabox.n4pro", "key.3", "Mode")), Is.EqualTo("mode (key.3 on mirabox.n4pro)"));
    }

    private static Workflow Shared(string id, params WorkflowNode[] nodes) =>
        new()
        {
            Id = id,
            Name = "Shared " + id,
            Nodes = [Trigger("t", WorkflowTriggers.Down), .. nodes],
            Links = [.. Enumerable.Range(0, nodes.Length).Select(i => Link(i == 0 ? "t" : nodes[i - 1].Id, nodes[i].Id))],
        };

    private static WorkflowNode CallOf(string id, string shared) => Node(id, WorkflowNodeKinds.Call, (WorkflowKeys.Workflow, shared));

    [Test]
    public async Task A_call_node_runs_a_shared_workflow_then_goes_on()
    {
        _host.SetSharedWorkflows([Shared("s1", Run("x", "b"), Run("y", "c"))]);
        await Start(new Workflow
        {
            Nodes = [Trigger("t", WorkflowTriggers.Down), Run("1", "a"), CallOf("call", "s1"), Run("2", "a")],
            Links = [Link("t", "1"), Link("1", "call"), Link("call", "2")],
        });

        await Input(InputCapabilities.Press);
        await WaitUntil(() => Ran().Count >= 4);

        Assert.That(Ran(), Is.EqualTo(new[] { "a", "b", "c", "a" }));
        Assert.That(Traces().Where(trace => trace.WorkflowId == "s1").Select(static trace => trace.NodeId), Is.EqualTo(new[] { "t", "x", "y" }).Or.Contain("x"), "steps inside the shared workflow say which one it is");
        Assert.That(Traces().Any(trace => trace.Detail.StartsWith("Calling")), Is.True);
    }

    [Test]
    public async Task A_call_that_cannot_run_goes_to_on_error_when_linked_and_to_next_otherwise()
    {
        await Start(new Workflow
        {
            Nodes = [Trigger("t", WorkflowTriggers.Down), CallOf("missing", "nope"), Run("ok", "a"), Run("bad", "b")],
            Links = [Link("t", "missing"), Link("missing", "ok"), Link("missing", "bad", WorkflowPorts.Error)],
        });

        await Input(InputCapabilities.Press);
        await WaitUntil(() => Ran().Count > 0);

        Assert.That(Ran(), Is.EqualTo(new[] { "b" }));
        Assert.That(Traces().Any(trace => trace.Kind == WorkflowTraceKind.Failed && trace.Detail.Contains("is gone")), Is.True);
    }

    [Test]
    public async Task A_shared_workflow_that_calls_itself_or_nests_too_deep_is_stopped_not_looped()
    {
        var chain = Enumerable.Range(0, 10).Select(i => Shared($"s{i}", i < 9 ? CallOf("next", $"s{i + 1}") : Run("end", "b"))).ToList();
        chain.Add(Shared("self", CallOf("again", "self"), Run("after", "c")));
        _host.SetSharedWorkflows(chain);
        await Start(new Workflow
        {
            Nodes = [Trigger("t", WorkflowTriggers.Down), CallOf("deep", "s0"), CallOf("loop", "self")],
            Links = [Link("t", "deep"), Link("deep", "loop")],
        });

        await Input(InputCapabilities.Press);
        await WaitUntil(() => Traces().Any(trace => trace.Kind == WorkflowTraceKind.Finished));

        Assert.That(Traces().Any(trace => trace.Detail.Contains("nested more than")), Is.True, "the chain of ten is cut at the depth limit");
        Assert.That(Traces().Any(trace => trace.Detail.Contains("would call itself")), Is.True);
        Assert.That(Ran(), Does.Not.Contain("b"), "the end of the too-deep chain was never reached");
        Assert.That(Ran(), Contains.Item("c"), "a call that can't run goes on at Then, so what follows it runs");
    }

    private static Workflow ThreeSteps() => new()
    {
        Nodes = [Trigger("t", WorkflowTriggers.Down), Run("1", "a"), Run("2", "b"), Run("3", "c")],
        Links = [Link("t", "1"), Link("1", "2"), Link("2", "3")],
    };

    private int PausedRun() => Traces().Last(trace => trace.Kind == WorkflowTraceKind.Paused).RunId;

    [Test]
    public async Task A_run_holds_in_front_of_a_breakpoint_and_goes_on_when_continued()
    {
        await Start(ThreeSteps());
        _host.SetWorkflowBreakpoints(Device, "key.0", ["2"]);
        await _host.WhenIdleAsync();

        await Input(InputCapabilities.Press);
        await WaitUntil(() => Traces().Any(trace => trace.Kind == WorkflowTraceKind.Paused));
        Assert.That(Ran(), Is.EqualTo(new[] { "a" }), "stopped in front of node 2");
        Assert.That(Traces().Last(trace => trace.Kind == WorkflowTraceKind.Paused).NodeId, Is.EqualTo("2"));

        _host.ContinueWorkflow(PausedRun());
        await WaitUntil(() => Ran().Count == 3);
        Assert.That(Ran(), Is.EqualTo(new[] { "a", "b", "c" }));
        Assert.That(Traces().Any(trace => trace.Kind == WorkflowTraceKind.Finished), Is.True);
    }

    [Test]
    public async Task A_step_runs_one_node_and_holds_again_and_stop_ends_a_held_run()
    {
        await Start(ThreeSteps());
        _host.SetWorkflowBreakpoints(Device, "key.0", ["2"]);
        await _host.WhenIdleAsync();
        await Input(InputCapabilities.Press);
        await WaitUntil(() => Traces().Any(trace => trace.Kind == WorkflowTraceKind.Paused));

        _host.StepWorkflow(PausedRun());
        await WaitUntil(() => Traces().Count(trace => trace.Kind == WorkflowTraceKind.Paused) == 2);
        Assert.That(Ran(), Is.EqualTo(new[] { "a", "b" }), "node 2 ran, node 3 is next");
        Assert.That(Traces().Last(trace => trace.Kind == WorkflowTraceKind.Paused).NodeId, Is.EqualTo("3"));

        _host.StopWorkflows(Device, "key.0");
        await _host.WhenIdleAsync();
        Assert.That(Traces().Any(trace => trace.Kind == WorkflowTraceKind.Stopped), Is.True);
        _host.ContinueWorkflow(PausedRun());
        await _host.WhenIdleAsync();
        Assert.That(Ran(), Is.EqualTo(new[] { "a", "b" }), "a stopped run does not go on");
    }

    [Test]
    public async Task Work_that_comes_due_while_a_run_is_held_waits_for_it_to_be_released()
    {
        await Start(new Workflow
        {
            Nodes = [Trigger("t", WorkflowTriggers.Down), Node("w", WorkflowNodeKinds.Wait, (WorkflowKeys.Milliseconds, "50")), Run("late", "a"), Run("held", "b")],
            Links = [Link("t", "w"), Link("w", "late"), Link("t", "held")],
        });
        _host.SetWorkflowBreakpoints(Device, "key.0", ["held"]);
        await _host.WhenIdleAsync();

        await Input(InputCapabilities.Press);
        await WaitUntil(() => Traces().Any(trace => trace.Kind == WorkflowTraceKind.Paused));
        await Task.Delay(200);
        await _host.WhenIdleAsync();
        Assert.That(Ran(), Is.Empty, "the wait ended but the held run does not move");

        _host.ContinueWorkflow(PausedRun());
        await WaitUntil(() => Ran().Count == 2);
        Assert.That(Ran(), Is.EquivalentTo(new[] { "a", "b" }));
    }

    [Test]
    public async Task Breakpoints_inside_a_shared_workflow_are_keyed_by_its_id()
    {
        _host.SetSharedWorkflows([Shared("s1", Run("x", "b"))]);
        await Start(new Workflow
        {
            Nodes = [Trigger("t", WorkflowTriggers.Down), Run("x", "a"), CallOf("call", "s1")],
            Links = [Link("t", "x"), Link("x", "call")],
        });
        _host.SetWorkflowBreakpoints(Device, "key.0", [WorkflowEngine.BreakpointKey("s1", "x")]);
        await _host.WhenIdleAsync();

        await Input(InputCapabilities.Press);
        await WaitUntil(() => Traces().Any(trace => trace.Kind == WorkflowTraceKind.Paused));

        Assert.That(Ran(), Is.EqualTo(new[] { "a" }), "the same node id in the control's own workflow did not stop it");
        Assert.That(Traces().Last(trace => trace.Kind == WorkflowTraceKind.Paused).WorkflowId, Is.EqualTo("s1"));
    }

    [Test]
    public void A_workflow_binding_survives_the_profile_file()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Converters = { new JsonStringEnumConverter() },
        };
        var binding = Binding(new Workflow
        {
            WhileRunning = WorkflowConcurrency.Restart,
            Nodes = [Trigger("t", WorkflowTriggers.Tap) with { X = 10, Y = 20 }, Run("1", "a") with { Title = "Mute" }],
            Links = [Link("t", "1")],
        });

        var json = JsonSerializer.Serialize(binding, options);
        var back = JsonSerializer.Deserialize<ControlBinding>(json, options)!;

        Assert.That(json, Does.Contain("\"workflow\""));
        Assert.That(back.Workflow, Is.EqualTo(binding.Workflow));
        Assert.That(back.ActionKind, Is.EqualTo(ProfileActionKind.Workflow));
        Assert.That(JsonSerializer.Serialize(binding with { Workflow = null }, options), Does.Not.Contain("\"workflow\""));
    }

    private static ActionSettings Settings(params (string Key, string Value)[] pairs) =>
        new(pairs.Select(static pair => new KeyValuePair<string, string>(pair.Key, pair.Value)));
}

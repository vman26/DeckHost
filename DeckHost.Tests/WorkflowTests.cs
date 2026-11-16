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

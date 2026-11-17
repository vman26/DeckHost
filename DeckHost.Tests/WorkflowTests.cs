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

    private const string KeyId = "key.0";
    private const int ShortWaitMilliseconds = 20;
    private const int AfterWaitDelay = 300;
    private static readonly string[] AThenB = ["a", "b"];

    // ---------------------------------------------------------------- chains, conditions, toggles

    [Test]
    public async Task Press_AChainOfActions_RunsThemInOrder()
    {
        // Arrange
        await Start(new Workflow
        {
            Nodes = [Trigger("t", WorkflowTriggers.Down), Run("1", "a"), Run("2", "b")],
            Links = [Link("t", "1"), Link("1", "2")],
        });

        // Act
        await Click();

        // Assert
        Assert.That(Ran(), Is.EqualTo(AThenB));
    }

    [Test]
    public async Task Press_AChainOfActions_TracesThatTheRunFinished()
    {
        // Arrange
        await Start(new Workflow
        {
            Nodes = [Trigger("t", WorkflowTriggers.Down), Run("1", "a"), Run("2", "b")],
            Links = [Link("t", "1"), Link("1", "2")],
        });

        // Act
        await Click();

        // Assert
        Assert.That(Traces().Any(trace => trace.Kind == WorkflowTraceKind.Finished && trace.ControlId == KeyId), Is.True);
    }

    private Workflow CountingIf() => new()
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
    };

    [Test]
    public async Task Press_AnIfOnAVariableSetByAnotherNode_TakesNoUntilTheVariableReachesTheValue()
    {
        // Arrange
        await Start(CountingIf());

        // Act
        await Click(); // mode empty → no, mode = 1
        await Click(); // 1 → no, mode = 2
        await Click(); // 2 → yes

        // Assert
        Assert.That(Ran(), Is.EqualTo(new[] { "b", "b", "a" }));
    }

    [Test]
    public async Task Press_AnIfOnAVariableSetByAnotherNode_LeavesTheVariableAtTwo()
    {
        // Arrange
        await Start(CountingIf());

        // Act
        await Click();
        await Click();
        await Click();

        // Assert
        Assert.That(_store.TryGetValue<string>(VariableKeys.UserVariable("MODE"), out var mode), Is.True);
        Assert.That(mode, Is.EqualTo("2"));
    }

    private Workflow ToggleOverARepeat() => new()
    {
        Nodes =
        [
            Trigger("t", WorkflowTriggers.Down),
            Node("toggle", WorkflowNodeKinds.Toggle),
            Node("repeat", WorkflowNodeKinds.Repeat, (WorkflowKeys.Count, "3")),
            Run("each", "a"),
            Node("wait", WorkflowNodeKinds.Wait, (WorkflowKeys.Milliseconds, ShortWaitMilliseconds.ToString())),
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
    };

    [Test]
    public async Task Press_AToggleOverARepeat_FirstRunsTheBodyRoundAfterRoundThenDone()
    {
        // Arrange
        await Start(ToggleOverARepeat());

        // Act
        await Click();
        await WaitUntil(() => Ran().Count >= 4);

        // Assert
        Assert.That(Ran(), Is.EqualTo(new[] { "a", "a", "a", "b" }));
    }

    [Test]
    public async Task Press_AToggleOverARepeat_TheSecondPressTakesTheOtherOutput()
    {
        // Arrange
        await Start(ToggleOverARepeat());
        await Click();
        await WaitUntil(() => Ran().Count >= 4);

        // Act
        await Click();

        // Assert
        Assert.That(Ran(), Is.EqualTo(new[] { "a", "a", "a", "b", "c" }));
    }

    [Test]
    public async Task Press_ARepeatWhoseCountComesFromAVariable_RunsThatManyRounds()
    {
        // Arrange
        _store.Publish(VariableKeys.UserVariable("rounds"), "3");
        await Start(new Workflow
        {
            Nodes = [Trigger("t", WorkflowTriggers.Down), Node("repeat", WorkflowNodeKinds.Repeat, (WorkflowKeys.Mode, WorkflowKeys.ModeVariable), (WorkflowKeys.Times, "Rounds")), Run("each", "a"), Run("done", "b")],
            Links = [Link("t", "repeat"), Link("repeat", "each", WorkflowPorts.Each), Link("repeat", "done", WorkflowPorts.Done)],
        });

        // Act
        await Click();
        await WaitUntil(() => Ran().Count >= 4);

        // Assert
        Assert.That(Ran(), Is.EqualTo(new[] { "a", "a", "a", "b" }));
    }

    private async Task StartCountingRepeat(string mode, string operation, string value, string? interval = null)
    {
        var settings = new List<(string, string)> { (WorkflowKeys.Mode, mode), (WorkflowKeys.Name, "n"), (WorkflowKeys.Operation, operation), (WorkflowKeys.Value, value) };
        if (interval is not null)
        {
            settings.Add((WorkflowKeys.Interval, interval));
        }

        await Start(new Workflow
        {
            Nodes =
            [
                Trigger("t", WorkflowTriggers.Down),
                Node("repeat", WorkflowNodeKinds.Repeat, [.. settings]),
                Node("count", WorkflowNodeKinds.SetVariable, (WorkflowKeys.Name, "n"), (WorkflowKeys.Operation, WorkflowKeys.Add), (WorkflowKeys.Value, "1")),
                Run("each", "a"),
                Run("done", "b"),
            ],
            Links = [Link("t", "repeat"), Link("repeat", "count", WorkflowPorts.Each), Link("count", "each"), Link("repeat", "done", WorkflowPorts.Done)],
        });
        _store.Publish(VariableKeys.UserVariable("n"), "0");
    }

    [Test]
    public async Task Press_ARepeatWhileAVariableHolds_ChecksItBeforeEveryRound()
    {
        // Arrange (the body counts "n" up; the loop runs while n is less than 3, so three rounds, 30 ms apart)
        await StartCountingRepeat(WorkflowKeys.ModeWhile, WorkflowKeys.IsLess, "3", "30");

        // Act
        await Click();
        await WaitUntil(() => Ran().Count >= 4);

        // Assert
        Assert.That(Ran(), Is.EqualTo(new[] { "a", "a", "a", "b" }));
    }

    [Test]
    public async Task Press_ARepeatWithAnInterval_WaitsAfterEachRoundIncludingTheLastBeforeTheConditionEnds()
    {
        // Arrange
        await StartCountingRepeat(WorkflowKeys.ModeWhile, WorkflowKeys.IsLess, "3", "30");

        // Act
        await Click();
        await WaitUntil(() => Ran().Count >= 4);

        // Assert
        var pauses = Traces().Where(static trace => trace.Wait is not null).ToList();
        Assert.That(pauses, Has.Count.EqualTo(3));
        Assert.That(pauses[0].Wait, Is.EqualTo(TimeSpan.FromMilliseconds(30)));
        Assert.That(pauses[0].NodeId, Is.EqualTo("repeat"));
    }

    [Test]
    public async Task Press_ARepeatUntilAVariableHolds_StopsWhenItDoes()
    {
        // Arrange
        await StartCountingRepeat(WorkflowKeys.ModeUntil, WorkflowKeys.IsEqual, "2");

        // Act
        await Click();
        await WaitUntil(() => Ran().Count >= 3);

        // Assert
        Assert.That(Ran(), Is.EqualTo(new[] { "a", "a", "b" }));
    }

    [Test]
    public async Task Press_AConditionThatNeverChanges_FailsInsteadOfRepeatingForever()
    {
        // Arrange (the empty body makes each round free; the step limit or the round limit ends it)
        await Start(new Workflow
        {
            Nodes = [Trigger("t", WorkflowTriggers.Down), Node("repeat", WorkflowNodeKinds.Repeat, (WorkflowKeys.Mode, WorkflowKeys.ModeWhile), (WorkflowKeys.Name, "forever"), (WorkflowKeys.Operation, WorkflowKeys.IsEmpty)), Run("done", "b")],
            Links = [Link("t", "repeat"), Link("repeat", "done", WorkflowPorts.Done)],
        });

        // Act
        await Click();
        await WaitUntil(() => Traces().Any(static trace => trace.Kind is WorkflowTraceKind.Failed));

        // Assert
        Assert.That(Traces().Any(static trace => trace.Kind == WorkflowTraceKind.Failed), Is.True);
    }

    private static string RepeatLine(params (string Key, string Value)[] settings) => WorkflowNodeTypes.Summarize(Node("r", WorkflowNodeKinds.Repeat, settings));

    [Test]
    public void Summarize_ARepeatWithACount_SaysHowManyTimes()
    {
        // Act
        var line = RepeatLine((WorkflowKeys.Count, "3"));

        // Assert
        Assert.That(line, Is.EqualTo("Repeat 3×"));
    }

    [Test]
    public void Summarize_ARepeatFromBeforeTheModes_AssumesTwoTimes()
    {
        // Act
        var line = RepeatLine();

        // Assert
        Assert.That(line, Is.EqualTo("Repeat 2×"));
    }

    [Test]
    public void Summarize_ARepeatWithACountVariableAndAnInterval_NamesBoth()
    {
        // Act
        var line = RepeatLine((WorkflowKeys.Mode, WorkflowKeys.ModeVariable), (WorkflowKeys.Times, "rounds"), (WorkflowKeys.Interval, "500"));

        // Assert
        Assert.That(line, Is.EqualTo("Repeat rounds×, every 500 ms"));
    }

    [Test]
    public void Summarize_ARepeatWhileAVariableEquals_SaysWhileItEquals()
    {
        // Act
        var line = RepeatLine((WorkflowKeys.Mode, WorkflowKeys.ModeWhile), (WorkflowKeys.Name, "mode"), (WorkflowKeys.Value, "on"));

        // Assert
        Assert.That(line, Is.EqualTo("Repeat while mode = on"));
    }

    [Test]
    public void Summarize_ARepeatUntilAComparison_SaysUntilItHoldsWithTheInterval()
    {
        // Act
        var line = RepeatLine((WorkflowKeys.Mode, WorkflowKeys.ModeUntil), (WorkflowKeys.Name, "n"), (WorkflowKeys.Operation, WorkflowKeys.IsGreater), (WorkflowKeys.Value, "5"), (WorkflowKeys.Interval, "2000"));

        // Assert
        Assert.That(line, Is.EqualTo("Repeat until n > 5, every 2 s"));
    }

    // ---------------------------------------------------------------- waits and concurrency

    [Test]
    public async Task Press_AWaitNode_TracesHowLongItIs()
    {
        // Arrange
        await Start(new Workflow
        {
            Nodes = [Trigger("t", WorkflowTriggers.Down), Node("wait", WorkflowNodeKinds.Wait, (WorkflowKeys.Milliseconds, "40")), Run("after", "a")],
            Links = [Link("t", "wait"), Link("wait", "after")],
        });

        // Act
        await Click();
        await WaitUntil(() => Ran().Count == 1);

        // Assert
        Assert.That(Traces().Single(static trace => trace is { NodeId: "wait", Wait: not null }).Wait, Is.EqualTo(TimeSpan.FromMilliseconds(40)));
    }

    private async Task StartWaiting(string whileRunning, int milliseconds) =>
        await Start(new Workflow
        {
            WhileRunning = whileRunning,
            Nodes = [Trigger("t", WorkflowTriggers.Down), Node("wait", WorkflowNodeKinds.Wait, (WorkflowKeys.Milliseconds, milliseconds.ToString())), Run("after", "a")],
            Links = [Link("t", "wait"), Link("wait", "after")],
        });

    [Test]
    public async Task Press_AgainWhileAWaitingRunWithStop_StopsTheRunSoNothingRuns()
    {
        // Arrange
        await StartWaiting(WorkflowConcurrency.Stop, AfterWaitDelay);

        // Act
        await Click();
        await Click(); // stops the waiting run
        await Task.Delay(500);
        await _host.WhenIdleAsync();

        // Assert
        Assert.That(Ran(), Is.Empty);
        Assert.That(Traces().Any(trace => trace.Kind == WorkflowTraceKind.Stopped), Is.True);
    }

    [Test]
    public async Task Press_AgainWhileARunWaitsWithIgnore_KeepsTheFirstRunAndTracesTheIgnoredOne()
    {
        // Arrange
        await StartWaiting(WorkflowConcurrency.Ignore, 150);

        // Act
        await Click();
        await Click();
        await WaitUntil(() => Ran().Count >= 1);
        await Task.Delay(AfterWaitDelay);
        await _host.WhenIdleAsync();

        // Assert
        Assert.That(Ran(), Is.EqualTo(new[] { "a" }));
        Assert.That(Traces().Any(trace => trace.Kind == WorkflowTraceKind.Ignored), Is.True);
    }

    // ---------------------------------------------------------------- gestures and turns

    private async Task StartGestureFlow() =>
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

    [Test]
    public async Task Click_OnceWithTapDoubleAndHoldTriggers_RunsTheTapWorkflowOnly()
    {
        // Arrange
        await StartGestureFlow();

        // Act
        await Click();
        await WaitUntil(() => Ran().Count >= 1);

        // Assert
        Assert.That(Ran(), Is.EqualTo(new[] { "a" }));
    }

    [Test]
    public async Task Click_TwiceQuicklyWithTapDoubleAndHoldTriggers_RunsTheDoublePressWorkflow()
    {
        // Arrange
        await StartGestureFlow();
        await Click();
        await WaitUntil(() => Ran().Count >= 1);

        // Act
        await Click();
        await Click();
        await Task.Delay(DeviceHostService.DoublePressWindow * 2);
        await _host.WhenIdleAsync();

        // Assert
        Assert.That(Ran(), Is.EqualTo(new[] { "a", "b" }));
    }

    [Test]
    public async Task LongPress_WithTapDoubleAndHoldTriggers_RunsTheHoldWorkflow()
    {
        // Arrange
        await StartGestureFlow();

        // Act
        await Input(InputCapabilities.Press);
        await Input(InputCapabilities.LongPress);
        await Input(InputCapabilities.Release);
        await Task.Delay(DeviceHostService.DoublePressWindow * 2);
        await _host.WhenIdleAsync();

        // Assert
        Assert.That(Ran(), Is.EqualTo(new[] { "c" }));
    }

    [Test]
    public async Task Rotate_AKnobTurn_ReachesItsActionAsATurn()
    {
        // Arrange
        await Start(new Workflow
        {
            Nodes = [Trigger("left", WorkflowTriggers.RotateLeft), Run("1", "a"), Trigger("right", WorkflowTriggers.RotateRight), Run("2", "b")],
            Links = [Link("left", "1"), Link("right", "2")],
        });

        // Act
        await Input(InputCapabilities.Rotate, -1);
        await Input(InputCapabilities.Rotate, 1);

        // Assert
        Assert.That(Ran(), Is.EqualTo(AThenB));
        Assert.That(_a.Executions.Any(context => context.EventType == InputCapabilities.Rotate && context.Value == -1), Is.True);
    }

    [Test]
    public async Task Press_AStopNodeBesideAWaitingBranch_EndsTheBranchThatIsStillWaiting()
    {
        // Arrange
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

        // Act
        await Click();
        await Task.Delay(400);
        await _host.WhenIdleAsync();

        // Assert
        Assert.That(Ran(), Is.Empty);
    }

    [Test]
    public async Task RunWorkflowTrigger_AFlowWithAMissingAction_ReportsTheMissingActionAndGoesOn()
    {
        // Arrange
        await Start(new Workflow
        {
            Nodes = [Trigger("t", WorkflowTriggers.Hold), new WorkflowNode("gone", WorkflowNodeKinds.Action) { Action = Bindings.Reference("missing", "x") }, Run("1", "a")],
            Links = [Link("t", "gone"), Link("gone", "1")],
        });

        // Act
        _host.RunWorkflowTrigger(Device, KeyId, WorkflowTriggers.Hold);
        await _host.WhenIdleAsync();

        // Assert
        Assert.That(Ran(), Is.EqualTo(new[] { "a" }));
        Assert.That(Traces().Any(trace => trace.Kind == WorkflowTraceKind.Failed && trace.NodeId == "gone"), Is.True);
    }

    // ---------------------------------------------------------------- conditions and converting

    private static WorkflowEngine NewEngine(VariableStore store, DateTime? now = null) =>
        new(store, (_, _, _, _) => true) { Clock = now is { } at ? () => at : () => DateTime.Now };

    [TestCase("OBS", true)]
    [TestCase("zoom", false)]
    public void Evaluate_AnAppCondition_ComparesWithTheAppInFront(string app, bool expected)
    {
        // Arrange
        using var store = new VariableStore();
        var engine = NewEngine(store);
        store.Publish(VariableKeys.ForegroundApp, "obs64");

        // Act
        var result = engine.Evaluate(Settings((WorkflowKeys.Check, WorkflowKeys.CheckApp), (WorkflowKeys.App, app)));

        // Assert
        Assert.That(result, Is.EqualTo(expected));
    }

    [TestCase("22:00", "06:00", true)] // past midnight
    [TestCase("09:00", "17:30", false)]
    [TestCase("nonsense", "17:30", false)]
    public void Evaluate_ATimeCondition_ComparesWithTheClockAlsoPastMidnight(string from, string to, bool expected)
    {
        // Arrange
        using var store = new VariableStore();
        var engine = NewEngine(store, new DateTime(2026, 1, 1, 23, 30, 0));

        // Act
        var result = engine.Evaluate(Settings((WorkflowKeys.Check, WorkflowKeys.CheckTime), (WorkflowKeys.From, from), (WorkflowKeys.To, to)));

        // Assert
        Assert.That(result, Is.EqualTo(expected));
    }

    [Test]
    public void Apply_AddingADecimal_StoresTheSumAndLaterComparisonsSeeIt()
    {
        // Arrange
        using var store = new VariableStore();
        var engine = NewEngine(store);

        // Act
        engine.Apply(Settings((WorkflowKeys.Name, "count"), (WorkflowKeys.Operation, WorkflowKeys.Add), (WorkflowKeys.Value, "2.5")));

        // Assert
        Assert.That(engine.ReadVariable("count"), Is.EqualTo("2.5"));
        Assert.That(engine.Evaluate(Settings((WorkflowKeys.Name, "count"), (WorkflowKeys.Operation, WorkflowKeys.IsGreater), (WorkflowKeys.Value, "2"))), Is.True);
    }

    [Test]
    public void Apply_FlippingAnUnsetVariable_MakesItTrue()
    {
        // Arrange
        using var store = new VariableStore();
        var engine = NewEngine(store);

        // Act
        engine.Apply(Settings((WorkflowKeys.Name, "on"), (WorkflowKeys.Operation, WorkflowKeys.Flip)));

        // Assert
        Assert.That(engine.ReadVariable("on"), Is.EqualTo("true"));
    }

    private static MultiActionStep[] SwitchSteps() =>
    [
        new MultiActionStep(0, Bindings.Reference("p", "a")) { Group = StepGroups.First },
        new MultiActionStep(1, Bindings.Reference("p", "b")) { Group = StepGroups.Second, DelayMs = 250 },
    ];

    private static Workflow ConvertedSwitch() =>
        WorkflowConverter.FromBinding(new ControlBinding(KeyId, ProfileActionKind.MultiActionSwitch, Bindings.Reference("p", "scaffold"), new ControlAppearance(), SwitchSteps()));

    [Test]
    public void FromBinding_AMultiActionSwitch_BecomesAToggleWhoseFirstOutputRunsTheFirstSet()
    {
        // Act
        var toggle = ConvertedSwitch();

        // Assert
        var toggleNode = Expect.Single(toggle.Nodes, node => node.Kind == WorkflowNodeKinds.Toggle);
        var first = toggle.Find(Expect.Single(toggle.LinksFrom(toggleNode.Id, WorkflowPorts.First)).To)!;
        Assert.That(first.Action!.ActionId, Is.EqualTo("a"));
    }

    [Test]
    public void FromBinding_AStepWithADelay_BecomesAWaitBeforeIt()
    {
        // Act
        var toggle = ConvertedSwitch();

        // Assert
        var toggleNode = Expect.Single(toggle.Nodes, node => node.Kind == WorkflowNodeKinds.Toggle);
        var wait = toggle.Find(Expect.Single(toggle.LinksFrom(toggleNode.Id, WorkflowPorts.Second)).To)!;
        Assert.That(wait.Kind, Is.EqualTo(WorkflowNodeKinds.Wait));
        Assert.That(wait.EffectiveSettings.Get(WorkflowKeys.Milliseconds), Is.EqualTo("250"));
    }

    [Test]
    public void FromBinding_AMultiActionSwitch_GivesEveryNodeItsOwnId()
    {
        // Act
        var toggle = ConvertedSwitch();

        // Assert
        Assert.That(toggle.Nodes.Select(static node => node.Id).Distinct().Count(), Is.EqualTo(toggle.Nodes.Count));
    }

    private static Workflow ConvertedKeyLogic() =>
        WorkflowConverter.FromBinding(new ControlBinding(KeyId, ProfileActionKind.KeyLogic, Bindings.Reference("p", "scaffold"), new ControlAppearance(),
            [new MultiActionStep(0, Bindings.Reference("p", "a")) { Group = StepGroups.Press }, new MultiActionStep(1, Bindings.Reference("p", "c")) { Group = StepGroups.Hold }]));

    [Test]
    public void TriggersOf_ConvertedKeyLogic_AreTapAndHold()
    {
        // Act
        var triggers = WorkflowEngine.TriggersOf(ConvertedKeyLogic()).ToHashSet();

        // Assert
        Assert.That(triggers, Is.EqualTo(new HashSet<string> { WorkflowTriggers.Tap, WorkflowTriggers.Hold }));
    }

    [Test]
    public void NextId_ConvertedKeyLogic_ContinuesAfterTheExistingNodes()
    {
        // Act
        var next = WorkflowConverter.NextId(ConvertedKeyLogic());

        // Assert
        Assert.That(next, Is.EqualTo("n5"));
    }

    [TestCase("2", "1", true)] // closes the chain back on itself
    [TestCase("1", "1", true)] // a node to itself
    [TestCase("t", "2", false)] // a shortcut forward
    public void WouldCycle_ALink_IsTrueOnlyWhenItClosesALoop(string from, string to, bool expected)
    {
        // Arrange
        var workflow = new Workflow
        {
            Nodes = [Trigger("t", WorkflowTriggers.Down), Run("1", "a"), Run("2", "b")],
            Links = [Link("t", "1"), Link("1", "2")],
        };

        // Act
        var wouldCycle = workflow.WouldCycle(from, to);

        // Assert
        Assert.That(wouldCycle, Is.EqualTo(expected));
    }

    // ---------------------------------------------------------------- errors and retries

    private static WorkflowNode Flaky(string id, string actionId, params (string Key, string Value)[] settings) =>
        Run(id, actionId) with { Settings = Settings(settings) };

    private async Task StartErrorBranch(WorkflowNode first)
    {
        await Start(new Workflow
        {
            Nodes = [Trigger("t", WorkflowTriggers.Down), first, Run("ok", "b"), Run("bad", "c")],
            Links = [Link("t", first.Id), Link(first.Id, "ok"), Link(first.Id, "bad", WorkflowPorts.Error)],
        });
    }

    [Test]
    public async Task Press_AnActionThatDidNotRunAndHasAnErrorOutputLinked_GoesOnAtTheErrorOutput()
    {
        // Arrange
        _a.OnExecute = _ => throw new InvalidOperationException("broken");
        await StartErrorBranch(Run("1", "a"));

        // Act
        await Input(InputCapabilities.Press);
        await WaitUntil(() => Ran().Count > 0);

        // Assert
        Assert.That(Ran(), Is.EqualTo(new[] { "c" }), "the error branch, not the next one");
        Assert.That(Traces().Any(trace => trace.Kind == WorkflowTraceKind.Failed && trace.Detail.Contains("On error")), Is.True);
    }

    [Test]
    public async Task Press_AnActionThatDidNotRunAndNothingIsLinkedToItsErrorOutput_StillGoesOnAtNext()
    {
        // Arrange
        _a.OnExecute = _ => throw new InvalidOperationException("broken");
        await Start(new Workflow
        {
            Nodes = [Trigger("t", WorkflowTriggers.Down), Run("1", "a"), Run("2", "b")],
            Links = [Link("t", "1"), Link("1", "2")],
        });

        // Act
        await Input(InputCapabilities.Press);
        await WaitUntil(() => Ran().Count > 0);

        // Assert
        Assert.That(Ran(), Is.EqualTo(new[] { "b" }));
    }

    private int _flakyPresses;

    private void MakeActionFailForTheFirstTwoPresses()
    {
        _a.OnExecute = context =>
        {
            // Fails both halves of the press and release while it is still "flaky".
            if (context.EventType == InputCapabilities.Press && Interlocked.Increment(ref _flakyPresses) <= 2 ||
                context.EventType != InputCapabilities.Press && Volatile.Read(ref _flakyPresses) <= 2)
            {
                throw new InvalidOperationException("not yet");
            }
        };
    }

    [Test]
    public async Task Press_AFlakyActionWithRetries_IsTriedAgainAndTheRunGoesOnOnceItWorks()
    {
        // Arrange
        MakeActionFailForTheFirstTwoPresses();
        await StartErrorBranch(Flaky("1", "a", (WorkflowKeys.Retries, "3"), (WorkflowKeys.RetryDelay, "0")));

        // Act
        await Input(InputCapabilities.Press);
        await WaitUntil(() => Ran().Contains("b"));

        // Assert
        Assert.That(Volatile.Read(ref _flakyPresses), Is.EqualTo(3), "two failures, then it worked");
        Assert.That(Ran(), Is.EqualTo(new[] { "b" }));
    }

    [Test]
    public async Task Press_AFlakyActionWithRetries_TracesEachRetry()
    {
        // Arrange
        MakeActionFailForTheFirstTwoPresses();
        await StartErrorBranch(Flaky("1", "a", (WorkflowKeys.Retries, "3"), (WorkflowKeys.RetryDelay, "0")));

        // Act
        await Input(InputCapabilities.Press);
        await WaitUntil(() => Ran().Contains("b"));

        // Assert
        Assert.That(Traces().Count(trace => trace.Detail.Contains("trying again")), Is.EqualTo(2));
    }

    [Test]
    public async Task Press_AnActionThatKeepsFailing_GoesToItsErrorOutputAfterItsTries()
    {
        // Arrange
        var presses = 0;
        _a.OnExecute = context =>
        {
            if (context.EventType == InputCapabilities.Press)
            {
                Interlocked.Increment(ref presses);
            }

            throw new InvalidOperationException("never");
        };
        await StartErrorBranch(Flaky("1", "a", (WorkflowKeys.Retries, "2"), (WorkflowKeys.RetryDelay, "0")));

        // Act
        await Input(InputCapabilities.Press);
        await WaitUntil(() => Ran().Contains("c"));

        // Assert
        Assert.That(Volatile.Read(ref presses), Is.EqualTo(3), "the first try and two more");
        Assert.That(Ran(), Is.EqualTo(new[] { "c" }));
    }

    [Test]
    public void OutputsOf_AnActionNode_HasAnErrorOutputNextToItsNextOne()
    {
        // Act
        var outputs = WorkflowNodeKinds.OutputsOf(WorkflowNodeKinds.Action);

        // Assert
        Assert.That(outputs, Is.EqualTo(new[] { WorkflowPorts.Next, WorkflowPorts.Error }));
    }

    [Test]
    public void OutputsOf_AWaitNode_IsUnchanged()
    {
        // Act
        var outputs = WorkflowNodeKinds.OutputsOf(WorkflowNodeKinds.Wait);

        // Assert
        Assert.That(outputs, Is.EqualTo(new[] { WorkflowPorts.Next }));
    }

    [Test]
    public void Describe_TheErrorPort_IsOnError()
    {
        // Act
        var text = WorkflowPorts.Describe(WorkflowPorts.Error);

        // Assert
        Assert.That(text, Is.EqualTo("On error"));
    }

    // ---------------------------------------------------------------- variables and scopes

    [Test]
    public async Task UserVariables_SeveralPublished_AreListedWithoutThePrefixInOrderAndWithoutSystemOnes()
    {
        // Arrange
        await Start(new Workflow { Nodes = [Trigger("t", WorkflowTriggers.Down)] });
        _store.Publish(VariableKeys.UserVariable("Zed"), "last");
        _store.Publish(VariableKeys.UserVariable("alpha"), "1");
        _store.Publish(VariableKeys.ForegroundApp, "obs");

        // Act
        var variables = _host.UserVariables();

        // Assert
        Assert.That(variables.Select(static pair => (pair.Key, pair.Value)), Is.EqualTo(new[] { ("alpha", "1"), ("zed", "last") }));
    }

    private static readonly WorkflowScope ScopeOne = new("p1", Device, KeyId);
    private static readonly WorkflowScope ScopeTwo = new("p2", Device, "key.1");

    private static WorkflowEngine EngineWithScopedMode(VariableStore store)
    {
        var engine = NewEngine(store);
        void Set(string scope, WorkflowScope where, string value) =>
            engine.Apply(Settings((WorkflowKeys.Name, "Mode"), (WorkflowKeys.Scope, scope), (WorkflowKeys.Value, value)), where);

        Set(WorkflowKeys.ScopeGlobal, ScopeOne, "everyone");
        Set(WorkflowKeys.ScopeProfile, ScopeOne, "profile one");
        Set(WorkflowKeys.ScopeControl, ScopeOne, "control one");
        Set(WorkflowKeys.ScopeProfile, ScopeTwo, "profile two");
        return engine;
    }

    private static string ReadMode(WorkflowEngine engine, string scope, WorkflowScope where) =>
        engine.ReadVariable("mode", Settings((WorkflowKeys.Scope, scope)), where);

    [Test]
    public void ReadVariable_AGlobalVariable_IsSharedByEveryScope()
    {
        // Arrange
        using var store = new VariableStore();
        var engine = EngineWithScopedMode(store);

        // Act
        var value = ReadMode(engine, WorkflowKeys.ScopeGlobal, ScopeTwo);

        // Assert
        Assert.That(value, Is.EqualTo("everyone"));
    }

    [TestCase(true, "profile one")]
    [TestCase(false, "profile two")]
    public void ReadVariable_AProfileVariable_BelongsToItsOwnProfile(bool firstProfile, string expected)
    {
        // Arrange
        using var store = new VariableStore();
        var engine = EngineWithScopedMode(store);

        // Act
        var value = ReadMode(engine, WorkflowKeys.ScopeProfile, firstProfile ? ScopeOne : ScopeTwo);

        // Assert
        Assert.That(value, Is.EqualTo(expected));
    }

    [TestCase(true, "control one")]
    [TestCase(false, "")]
    public void ReadVariable_AControlVariable_BelongsToItsOwnControl(bool firstControl, string expected)
    {
        // Arrange
        using var store = new VariableStore();
        var engine = EngineWithScopedMode(store);

        // Act
        var value = ReadMode(engine, WorkflowKeys.ScopeControl, firstControl ? ScopeOne : ScopeTwo);

        // Assert
        Assert.That(value, Is.EqualTo(expected));
    }

    [TestCase(true, true)]
    [TestCase(false, false)]
    public void Evaluate_AProfileScopedCondition_ChecksThatProfilesVariable(bool firstProfile, bool expected)
    {
        // Arrange
        using var store = new VariableStore();
        var engine = EngineWithScopedMode(store);
        var condition = Settings((WorkflowKeys.Name, "mode"), (WorkflowKeys.Scope, WorkflowKeys.ScopeProfile), (WorkflowKeys.Value, "profile one"));

        // Act
        var result = engine.Evaluate(condition, firstProfile ? ScopeOne : ScopeTwo);

        // Assert
        Assert.That(result, Is.EqualTo(expected));
    }

    [Test]
    public void Evaluate_ACondition_WithoutAScopeSettingMeansGlobal()
    {
        // Arrange
        using var store = new VariableStore();
        var engine = EngineWithScopedMode(store);

        // Act
        var result = engine.Evaluate(Settings((WorkflowKeys.Name, "mode"), (WorkflowKeys.Value, "everyone")), ScopeTwo);

        // Assert
        Assert.That(result, Is.True);
    }

    [Test]
    public async Task Press_ASetVariableOnControlScope_StaysWithItsControl()
    {
        // Arrange
        Workflow SetWho(string value) => new()
        {
            Nodes = [Trigger("t", WorkflowTriggers.Down), Node("s", WorkflowNodeKinds.SetVariable, (WorkflowKeys.Name, "who"), (WorkflowKeys.Scope, WorkflowKeys.ScopeControl), (WorkflowKeys.Value, value))],
            Links = [Link("t", "s")],
        };
        _host.SetProfiles([new DeviceProfile("profile", "Profile", Device, [Binding(SetWho("first")), Binding(SetWho("second")) with { ControlId = "key.1" }])]);
        await _host.StartAsync(CancellationToken.None);
        await _host.WhenIdleAsync();

        // Act
        await Input(InputCapabilities.Press);
        _host.SimulateInput(new DeviceInputEvent(Device, "key.1", InputCapabilities.Press, 1));
        await _host.WhenIdleAsync();

        // Assert
        Assert.That(_store.TryGetValue<string>(VariableKeys.ControlVariable(Device, KeyId, "who"), out var a) ? a : null, Is.EqualTo("first"));
        Assert.That(_store.TryGetValue<string>(VariableKeys.ControlVariable(Device, "key.1", "who"), out var b) ? b : null, Is.EqualTo("second"));
        Assert.That(_store.TryGetValue<string>(VariableKeys.UserVariable("who"), out _), Is.False, "nothing leaked into the global one");
    }

    [Test]
    public void Describe_AGlobalVariableKey_IsJustItsName()
    {
        // Act
        var text = WorkflowScopes.Describe(VariableKeys.UserVariable("Mode"));

        // Assert
        Assert.That(text, Is.EqualTo("mode"));
    }

    [Test]
    public void Describe_AProfileVariableKey_NamesTheProfile()
    {
        // Act
        var text = WorkflowScopes.Describe(VariableKeys.ProfileVariable("p1", "Mode"));

        // Assert
        Assert.That(text, Is.EqualTo("mode (profile p1)"));
    }

    [Test]
    public void Describe_AControlVariableKey_NamesTheControlAndDevice()
    {
        // Act
        var text = WorkflowScopes.Describe(VariableKeys.ControlVariable("mirabox.n4pro", "key.3", "Mode"));

        // Assert
        Assert.That(text, Is.EqualTo("mode (key.3 on mirabox.n4pro)"));
    }

    // ---------------------------------------------------------------- shared workflows (Call)

    private static Workflow Shared(string id, params WorkflowNode[] nodes) =>
        new()
        {
            Id = id,
            Name = "Shared " + id,
            Nodes = [Trigger("t", WorkflowTriggers.Down), .. nodes],
            Links = [.. Enumerable.Range(0, nodes.Length).Select(i => Link(i == 0 ? "t" : nodes[i - 1].Id, nodes[i].Id))],
        };

    private static WorkflowNode CallOf(string id, string shared) => Node(id, WorkflowNodeKinds.Call, (WorkflowKeys.Workflow, shared));

    private async Task StartCallingASharedWorkflow()
    {
        _host.SetSharedWorkflows([Shared("s1", Run("x", "b"), Run("y", "c"))]);
        await Start(new Workflow
        {
            Nodes = [Trigger("t", WorkflowTriggers.Down), Run("1", "a"), CallOf("call", "s1"), Run("2", "a")],
            Links = [Link("t", "1"), Link("1", "call"), Link("call", "2")],
        });
    }

    [Test]
    public async Task Press_ACallNode_RunsTheSharedWorkflowThenGoesOn()
    {
        // Arrange
        await StartCallingASharedWorkflow();

        // Act
        await Input(InputCapabilities.Press);
        await WaitUntil(() => Ran().Count >= 4);

        // Assert
        Assert.That(Ran(), Is.EqualTo(new[] { "a", "b", "c", "a" }));
    }

    [Test]
    public async Task Press_ACallNode_TracesTheStepsInsideTheSharedWorkflowWithItsId()
    {
        // Arrange
        await StartCallingASharedWorkflow();

        // Act
        await Input(InputCapabilities.Press);
        await WaitUntil(() => Ran().Count >= 4);

        // Assert
        Assert.That(Traces().Where(trace => trace.WorkflowId == "s1").Select(static trace => trace.NodeId), Does.Contain("x"));
        Assert.That(Traces().Any(trace => trace.Detail.StartsWith("Calling")), Is.True);
    }

    [Test]
    public async Task Press_ACallToAWorkflowThatIsGone_GoesToOnErrorWhenLinked()
    {
        // Arrange
        await Start(new Workflow
        {
            Nodes = [Trigger("t", WorkflowTriggers.Down), CallOf("missing", "nope"), Run("ok", "a"), Run("bad", "b")],
            Links = [Link("t", "missing"), Link("missing", "ok"), Link("missing", "bad", WorkflowPorts.Error)],
        });

        // Act
        await Input(InputCapabilities.Press);
        await WaitUntil(() => Ran().Count > 0);

        // Assert
        Assert.That(Ran(), Is.EqualTo(new[] { "b" }));
        Assert.That(Traces().Any(trace => trace.Kind == WorkflowTraceKind.Failed && trace.Detail.Contains("is gone")), Is.True);
    }

    private async Task StartCallsThatNestTooDeepOrLoop()
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
    }

    [Test]
    public async Task Press_ACallChainOfTen_IsCutAtTheDepthLimitSoItsEndIsNeverReached()
    {
        // Arrange / Act
        await StartCallsThatNestTooDeepOrLoop();

        // Assert
        Assert.That(Traces().Any(trace => trace.Detail.Contains("nested more than")), Is.True);
        Assert.That(Ran(), Does.Not.Contain("b"));
    }

    [Test]
    public async Task Press_AWorkflowThatCallsItself_IsStoppedAndWhatFollowsStillRuns()
    {
        // Arrange / Act
        await StartCallsThatNestTooDeepOrLoop();

        // Assert
        Assert.That(Traces().Any(trace => trace.Detail.Contains("would call itself")), Is.True);
        Assert.That(Ran(), Contains.Item("c"), "a call that can't run goes on at Then, so what follows it runs");
    }

    // ---------------------------------------------------------------- debugging: breakpoints

    private static Workflow ThreeSteps() => new()
    {
        Nodes = [Trigger("t", WorkflowTriggers.Down), Run("1", "a"), Run("2", "b"), Run("3", "c")],
        Links = [Link("t", "1"), Link("1", "2"), Link("2", "3")],
    };

    private int PausedRun() => Traces().Last(trace => trace.Kind == WorkflowTraceKind.Paused).RunId;

    private async Task PressAndWaitForTheBreakpoint()
    {
        await Start(ThreeSteps());
        _host.SetWorkflowBreakpoints(Device, KeyId, ["2"]);
        await _host.WhenIdleAsync();
        await Input(InputCapabilities.Press);
        await WaitUntil(() => Traces().Any(trace => trace.Kind == WorkflowTraceKind.Paused));
    }

    [Test]
    public async Task Press_ARunWithABreakpoint_HoldsInFrontOfTheNode()
    {
        // Arrange / Act
        await PressAndWaitForTheBreakpoint();

        // Assert
        Assert.That(Ran(), Is.EqualTo(new[] { "a" }), "stopped in front of node 2");
        Assert.That(Traces().Last(trace => trace.Kind == WorkflowTraceKind.Paused).NodeId, Is.EqualTo("2"));
    }

    [Test]
    public async Task ContinueWorkflow_ARunHeldAtABreakpoint_GoesOnToTheEnd()
    {
        // Arrange
        await PressAndWaitForTheBreakpoint();

        // Act
        _host.ContinueWorkflow(PausedRun());
        await WaitUntil(() => Ran().Count == 3);

        // Assert
        Assert.That(Ran(), Is.EqualTo(new[] { "a", "b", "c" }));
        Assert.That(Traces().Any(trace => trace.Kind == WorkflowTraceKind.Finished), Is.True);
    }

    [Test]
    public async Task ReleaseWorkflowRuns_RunHeldAtABreakpoint_GoesOnToTheEnd()
    {
        // Arrange
        await PressAndWaitForTheBreakpoint();

        // Act
        _host.ReleaseWorkflowRuns();
        await WaitUntil(() => Ran().Count == 3);

        // Assert
        Assert.That(Ran(), Is.EqualTo(new[] { "a", "b", "c" }));
    }

    [Test]
    public async Task StepWorkflow_ARunHeldAtABreakpoint_RunsOneNodeAndHoldsAgain()
    {
        // Arrange
        await PressAndWaitForTheBreakpoint();

        // Act
        _host.StepWorkflow(PausedRun());
        await WaitUntil(() => Traces().Count(trace => trace.Kind == WorkflowTraceKind.Paused) == 2);

        // Assert
        Assert.That(Ran(), Is.EqualTo(AThenB), "node 2 ran, node 3 is next");
        Assert.That(Traces().Last(trace => trace.Kind == WorkflowTraceKind.Paused).NodeId, Is.EqualTo("3"));
    }

    [Test]
    public async Task StopWorkflows_ARunHeldAtAStep_EndsItSoContinuingDoesNothing()
    {
        // Arrange
        await PressAndWaitForTheBreakpoint();
        _host.StepWorkflow(PausedRun());
        await WaitUntil(() => Traces().Count(trace => trace.Kind == WorkflowTraceKind.Paused) == 2);

        // Act
        _host.StopWorkflows(Device, KeyId);
        await _host.WhenIdleAsync();
        _host.ContinueWorkflow(PausedRun());
        await _host.WhenIdleAsync();

        // Assert
        Assert.That(Traces().Any(trace => trace.Kind == WorkflowTraceKind.Stopped), Is.True);
        Assert.That(Ran(), Is.EqualTo(AThenB), "a stopped run does not go on");
    }

    [Test]
    public async Task Press_WorkThatComesDueWhileARunIsHeld_WaitsForTheRunToBeReleased()
    {
        // Arrange
        await Start(new Workflow
        {
            Nodes = [Trigger("t", WorkflowTriggers.Down), Node("w", WorkflowNodeKinds.Wait, (WorkflowKeys.Milliseconds, "50")), Run("late", "a"), Run("held", "b")],
            Links = [Link("t", "w"), Link("w", "late"), Link("t", "held")],
        });
        _host.SetWorkflowBreakpoints(Device, KeyId, ["held"]);
        await _host.WhenIdleAsync();
        await Input(InputCapabilities.Press);
        await WaitUntil(() => Traces().Any(trace => trace.Kind == WorkflowTraceKind.Paused));
        await Task.Delay(200);
        await _host.WhenIdleAsync();
        var whileHeld = Ran();

        // Act
        _host.ContinueWorkflow(PausedRun());
        await WaitUntil(() => Ran().Count == 2);

        // Assert
        Assert.That(whileHeld, Is.Empty, "the wait ended but the held run does not move");
        Assert.That(Ran(), Is.EquivalentTo(AThenB));
    }

    [Test]
    public async Task Press_ABreakpointInASharedWorkflow_IsKeyedByItsIdSoTheSameNodeIdElsewhereDoesNotStop()
    {
        // Arrange
        _host.SetSharedWorkflows([Shared("s1", Run("x", "b"))]);
        await Start(new Workflow
        {
            Nodes = [Trigger("t", WorkflowTriggers.Down), Run("x", "a"), CallOf("call", "s1")],
            Links = [Link("t", "x"), Link("x", "call")],
        });
        _host.SetWorkflowBreakpoints(Device, KeyId, [WorkflowEngine.BreakpointKey("s1", "x")]);
        await _host.WhenIdleAsync();

        // Act
        await Input(InputCapabilities.Press);
        await WaitUntil(() => Traces().Any(trace => trace.Kind == WorkflowTraceKind.Paused));

        // Assert
        Assert.That(Ran(), Is.EqualTo(new[] { "a" }), "the same node id in the control's own workflow did not stop it");
        Assert.That(Traces().Last(trace => trace.Kind == WorkflowTraceKind.Paused).WorkflowId, Is.EqualTo("s1"));
    }

    // ---------------------------------------------------------------- calculating and typing variables

    private static readonly WorkflowScope CalcScope = new("p", Device, KeyId);

    private static (string Line, bool Ok) Calc(WorkflowEngine engine, string name, string expression, string? type = null, string? scope = null) =>
        engine.TryApply(Settings(
            (WorkflowKeys.Name, name), (WorkflowKeys.Operation, WorkflowKeys.Calculate), (WorkflowKeys.Expression, expression),
            (WorkflowKeys.As, type ?? WorkflowKeys.AsAuto), (WorkflowKeys.Scope, scope ?? WorkflowKeys.ScopeGlobal)), CalcScope);

    private static WorkflowEngine EngineWithCountFour(VariableStore store)
    {
        var engine = NewEngine(store);
        store.Publish(VariableKeys.UserVariable("count"), "4");
        store.Publish(VariableKeys.ProfileVariable("p", "mode"), "night");
        return engine;
    }

    [Test]
    public void TryApply_ACalculation_StoresItsResultAndReportsTheLine()
    {
        // Arrange
        using var store = new VariableStore();
        var engine = EngineWithCountFour(store);

        // Act
        var result = Calc(engine, "double", "[count] * 2 + 1");

        // Assert
        Assert.That(result, Is.EqualTo(("double = 9", true)));
        Assert.That(engine.ReadVariable("double"), Is.EqualTo("9"));
    }

    [Test]
    public void TryApply_ATextCalculationWithAScopedVariable_JoinsThem()
    {
        // Arrange
        using var store = new VariableStore();
        var engine = EngineWithCountFour(store);

        // Act
        var result = Calc(engine, "label", "[profile:mode] + ' ' + [count]");

        // Assert
        Assert.That(result.Ok, Is.True);
        Assert.That(engine.ReadVariable("label"), Is.EqualTo("night 4"));
    }

    [Test]
    public void TryApply_AComparisonStoredAsABool_IsTrueOrFalse()
    {
        // Arrange
        using var store = new VariableStore();
        var engine = EngineWithCountFour(store);

        // Act
        var result = Calc(engine, "big", "[count] > 3", WorkflowKeys.AsBool);

        // Assert
        Assert.That(result.Ok, Is.True);
        Assert.That(engine.ReadVariable("big"), Is.EqualTo("true"));
    }

    [Test]
    public void TryApply_ACalculationWithAControlScope_StoresItInThatScope()
    {
        // Arrange
        using var store = new VariableStore();
        var engine = EngineWithCountFour(store);

        // Act
        var result = Calc(engine, "mine", "[count]", scope: WorkflowKeys.ScopeControl);

        // Assert
        Assert.That(result.Ok, Is.True);
        Assert.That(store.TryGetValue<string>(VariableKeys.ControlVariable(Device, KeyId, "mine"), out var control) ? control : null, Is.EqualTo("4"));
    }

    [Test]
    public void TryApply_ABrokenExpression_FailsAndLeavesTheVariableAlone()
    {
        // Arrange
        using var store = new VariableStore();
        var engine = EngineWithCountFour(store);
        store.Publish(VariableKeys.UserVariable("keep"), "unchanged");

        // Act
        var broken = Calc(engine, "keep", "[count] +");

        // Assert
        Assert.That(broken.Ok, Is.False);
        Assert.That(broken.Line, Does.Contain("keep").And.Contain("valid expression"));
        Assert.That(engine.ReadVariable("keep"), Is.EqualTo("unchanged"));
    }

    [Test]
    public void TryApply_TextStoredAsANumber_FailsAndLeavesTheVariableAlone()
    {
        // Arrange
        using var store = new VariableStore();
        var engine = EngineWithCountFour(store);
        store.Publish(VariableKeys.UserVariable("keep"), "unchanged");

        // Act
        var result = Calc(engine, "keep", "'abc'", WorkflowKeys.AsNumber);

        // Assert
        Assert.That(result.Ok, Is.False, "text can't be stored as a number");
        Assert.That(engine.ReadVariable("keep"), Is.EqualTo("unchanged"));
    }

    private static (string Line, bool Ok) SetTyped(WorkflowEngine engine, string value, string type) =>
        engine.TryApply(Settings((WorkflowKeys.Name, "x"), (WorkflowKeys.Value, value), (WorkflowKeys.As, type)));

    [TestCase("3.50", WorkflowKeys.AsNumber, "3.5")]
    [TestCase("YES", WorkflowKeys.AsBool, "true")]
    [TestCase("12abc", WorkflowKeys.AsText, "12abc")]
    public void TryApply_APlainValueThatFitsItsType_IsStoredNormalised(string value, string type, string expected)
    {
        // Arrange
        using var store = new VariableStore();
        var engine = NewEngine(store);

        // Act
        var result = SetTyped(engine, value, type);

        // Assert
        Assert.That(result.Ok, Is.True);
        Assert.That(engine.ReadVariable("x"), Is.EqualTo(expected));
    }

    [TestCase(WorkflowKeys.AsNumber)]
    [TestCase(WorkflowKeys.AsBool)]
    public void TryApply_APlainValueThatDoesNotFitItsType_FailsAndChangesNothing(string type)
    {
        // Arrange
        using var store = new VariableStore();
        var engine = NewEngine(store);
        SetTyped(engine, "YES", WorkflowKeys.AsBool);

        // Act
        var result = SetTyped(engine, "hello", type);

        // Assert
        Assert.That(result.Ok, Is.False);
        Assert.That(engine.ReadVariable("x"), Is.EqualTo("true"));
    }

    [Test]
    public async Task Press_AnIfNodeCheckingAnExpression_TakesYesWhenItIsTrue()
    {
        // Arrange
        _store.Publish(VariableKeys.UserVariable("count"), "7");
        await Start(new Workflow
        {
            Nodes = [Trigger("t", WorkflowTriggers.Down), Node("if", WorkflowNodeKinds.If, (WorkflowKeys.Check, WorkflowKeys.CheckExpression), (WorkflowKeys.Expression, "[count] > 5 && [count] < 10")), Run("yes", "a"), Run("no", "b")],
            Links = [Link("t", "if"), Link("if", "yes", WorkflowPorts.Yes), Link("if", "no", WorkflowPorts.No)],
        });

        // Act
        await Input(InputCapabilities.Press);
        await WaitUntil(() => Ran().Count >= 1);

        // Assert
        Assert.That(Ran(), Is.EqualTo(new[] { "a" }));
    }

    [Test]
    public async Task Press_AnIfNodeWithABrokenExpression_GoesToNoAndSaysWhy()
    {
        // Arrange
        _store.Publish(VariableKeys.UserVariable("count"), "7");
        await Start(new Workflow
        {
            Nodes = [Trigger("t", WorkflowTriggers.Down), Node("bad", WorkflowNodeKinds.If, (WorkflowKeys.Check, WorkflowKeys.CheckExpression), (WorkflowKeys.Expression, "[count] >")), Run("never", "c"), Run("fallback", "a")],
            Links = [Link("t", "bad"), Link("bad", "never", WorkflowPorts.Yes), Link("bad", "fallback", WorkflowPorts.No)],
        });

        // Act
        await Input(InputCapabilities.Press);
        await WaitUntil(() => Ran().Count >= 1);

        // Assert
        Assert.That(Ran(), Is.EqualTo(new[] { "a" }));
        Assert.That(Traces().Any(trace => trace.Kind == WorkflowTraceKind.Failed && trace.NodeId == "bad" && trace.Detail.Contains("Going on at No")), Is.True);
    }

    [TestCase("[n] == 1", true, TestName = "Evaluate_AnUnprefixedVariableInAnExpression_IsGlobal")]
    [TestCase("[profile:n] == 5", true, TestName = "Evaluate_AProfilePrefixedVariableInAnExpression_ReadsTheProfilesVariable")]
    [TestCase("[control:n] == 9", true, TestName = "Evaluate_AControlPrefixedVariableInAnExpression_ReadsTheControlsVariable")]
    [TestCase("hour() == 10", true, TestName = "Evaluate_TheHourFunction_UsesTheEnginesClock")]
    public void Evaluate_AnExpressionCondition_SeesScopedVariablesAndTheClock(string expression, bool expected)
    {
        // Arrange
        using var store = new VariableStore();
        var engine = NewEngine(store, new DateTime(2026, 1, 1, 10, 0, 0));
        var where = new WorkflowScope("p1", Device, "key.2");
        store.Publish(VariableKeys.ProfileVariable("p1", "n"), "5");
        store.Publish(VariableKeys.ControlVariable(Device, "key.2", "n"), "9");
        store.Publish(VariableKeys.UserVariable("n"), "1");

        // Act
        var result = engine.Evaluate(Settings((WorkflowKeys.Check, WorkflowKeys.CheckExpression), (WorkflowKeys.Expression, expression)), where);

        // Assert
        Assert.That(result, Is.EqualTo(expected));
    }

    // ---------------------------------------------------------------- switch

    private static WorkflowNode SwitchOn(string id, string expression, params (string Id, string Text)[] cases) =>
        Node(id, WorkflowNodeKinds.Switch, (WorkflowKeys.Expression, expression), (WorkflowKeys.Cases, WorkflowCases.Serialize(cases.Select(static one => new WorkflowCase(one.Id, one.Text)))));

    [TestCase("night", "b")]
    [TestCase("DAY", "a")]
    [TestCase("evening", "c")]
    public async Task Press_ASwitch_GoesOnAtTheFirstCaseThatEqualsTheValueElseAtElse(string mode, string expectedAction)
    {
        // Arrange
        _store.Publish(VariableKeys.UserVariable("mode"), mode);
        await Start(new Workflow
        {
            Nodes = [Trigger("t", WorkflowTriggers.Down), SwitchOn("sw", "[mode]", ("c1", "day"), ("c2", "night")), Run("day", "a"), Run("night", "b"), Run("other", "c")],
            Links = [Link("t", "sw"), Link("sw", "day", "case:c1"), Link("sw", "night", "case:c2"), Link("sw", "other", WorkflowPorts.Default)],
        });

        // Act
        await Input(InputCapabilities.Press);
        await WaitUntil(() => Ran().Count > 0);

        // Assert
        Assert.That(Ran(), Is.EqualTo(new[] { expectedAction }));
    }

    private static ActionSettings SwitchSettings(string expression, params (string, string)[] cases) =>
        Settings((WorkflowKeys.Expression, expression), (WorkflowKeys.Cases, WorkflowCases.Serialize(cases.Select(static one => new WorkflowCase(one.Item1, one.Item2)))));

    [Test]
    public void ChooseCase_TenAndTenPointZero_AreEqualAndTheFirstMatchWins()
    {
        // Arrange
        using var store = new VariableStore();
        var engine = NewEngine(store);
        store.Publish(VariableKeys.UserVariable("n"), "10");

        // Act
        var port = engine.ChooseCase(SwitchSettings("[n]", ("a", "9"), ("b", "10.0"), ("c", "10")), null).Port;

        // Assert
        Assert.That(port, Is.EqualTo("case:b"), "10 equals 10.0, and the first match wins");
    }

    [Test]
    public void ChooseCase_AnArithmeticExpression_MatchesItsResult()
    {
        // Arrange
        using var store = new VariableStore();
        var engine = NewEngine(store);
        store.Publish(VariableKeys.UserVariable("n"), "10");

        // Act
        var port = engine.ChooseCase(SwitchSettings("[n] % 3", ("a", "0"), ("b", "1")), null).Port;

        // Assert
        Assert.That(port, Is.EqualTo("case:b"));
    }

    [Test]
    public void ChooseCase_AComparison_MatchesTrue()
    {
        // Arrange
        using var store = new VariableStore();
        var engine = NewEngine(store);
        store.Publish(VariableKeys.UserVariable("n"), "10");

        // Act
        var port = engine.ChooseCase(SwitchSettings("[n] > 5", ("t", "true")), null).Port;

        // Assert
        Assert.That(port, Is.EqualTo("case:t"));
    }

    [Test]
    public void ChooseCase_AnUnsetVariable_MatchesAnEmptyCase()
    {
        // Arrange
        using var store = new VariableStore();
        var engine = NewEngine(store);

        // Act
        var port = engine.ChooseCase(SwitchSettings("[unset]", ("e", string.Empty)), null).Port;

        // Assert
        Assert.That(port, Is.EqualTo("case:e"), "an empty case matches an unset variable");
    }

    [Test]
    public void ChooseCase_ABrokenExpression_GoesToElseAndSaysSo()
    {
        // Arrange
        using var store = new VariableStore();
        var engine = NewEngine(store);
        store.Publish(VariableKeys.UserVariable("n"), "10");

        // Act
        var broken = engine.ChooseCase(SwitchSettings("[n] +", ("a", "1")), null);

        // Assert
        Assert.That((broken.Port, broken.Ok), Is.EqualTo((WorkflowPorts.Default, false)));
        Assert.That(broken.Line, Does.Contain("Else"));
    }

    [Test]
    public void ChooseCase_NoCases_AlwaysGoesToElse()
    {
        // Arrange
        using var store = new VariableStore();
        var engine = NewEngine(store);
        store.Publish(VariableKeys.UserVariable("n"), "10");

        // Act
        var port = engine.ChooseCase(SwitchSettings("[n]"), null).Port;

        // Assert
        Assert.That(port, Is.EqualTo(WorkflowPorts.Default));
    }

    private static readonly WorkflowCase[] OddCases = [new WorkflowCase("c1", "a=b"), new WorkflowCase("c7", "multi\nline"), new WorkflowCase("c3", string.Empty)];

    [Test]
    public void Parse_SerializedCases_KeepTheirIdsAndOddTextWhereItCan()
    {
        // Act
        var back = WorkflowCases.Parse(WorkflowCases.Serialize(OddCases));

        // Assert
        Assert.That(back.Select(static one => one.Id), Is.EqualTo(new[] { "c1", "c7", "c3" }));
        Assert.That(back[0].Text, Is.EqualTo("a=b"), "only the first = separates the id");
        Assert.That(back[1].Text, Is.EqualTo("multi line"), "a new line in a case becomes a space");
        Assert.That(back[2].Text, Is.Empty);
    }

    [Test]
    public void NextId_ParsedCases_IsTheFirstFreeNumber()
    {
        // Arrange
        var back = WorkflowCases.Parse(WorkflowCases.Serialize(OddCases));

        // Act
        var next = WorkflowCases.NextId(back);

        // Assert
        Assert.That(next, Is.EqualTo("c2"));
    }

    [Test]
    public void Parse_BadLinesAndRepeatedIds_AreLeftOut()
    {
        // Act
        var cases = WorkflowCases.Parse("no id\n=empty id\nx=1\nx=2");

        // Assert
        Assert.That(cases.Select(static one => one.Text), Is.EqualTo(new[] { "1" }));
    }

    [Test]
    public void OutputsOf_ASwitchNode_IsItsCasesInOrderThenElse()
    {
        // Arrange
        var node = Node("s", WorkflowNodeKinds.Switch, (WorkflowKeys.Cases, WorkflowCases.Serialize(OddCases)));

        // Act
        var outputs = WorkflowNodeKinds.OutputsOf(node);

        // Assert
        Assert.That(outputs, Is.EqualTo(new[] { "case:c1", "case:c7", "case:c3", "default" }));
    }

    [TestCase("case:c1", "= a=b")]
    [TestCase("case:c3", "= (empty)")]
    [TestCase("default", "Else")]
    public void Describe_ASwitchPort_ShowsItsCaseTextOrElse(string port, string expected)
    {
        // Arrange
        var node = Node("s", WorkflowNodeKinds.Switch, (WorkflowKeys.Cases, WorkflowCases.Serialize(OddCases)));

        // Act
        var text = WorkflowPorts.Describe(port, node);

        // Assert
        Assert.That(text, Is.EqualTo(expected));
    }

    // ---------------------------------------------------------------- saving

    private static readonly JsonSerializerOptions ProfileFileOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    private static ControlBinding SavedWorkflowBinding() => Binding(new Workflow
    {
        WhileRunning = WorkflowConcurrency.Restart,
        Nodes = [Trigger("t", WorkflowTriggers.Tap) with { X = 10, Y = 20 }, Run("1", "a") with { Title = "Mute" }],
        Links = [Link("t", "1")],
    });

    [Test]
    public void Serialize_AWorkflowBinding_SurvivesTheProfileFile()
    {
        // Arrange
        var binding = SavedWorkflowBinding();

        // Act
        var json = JsonSerializer.Serialize(binding, ProfileFileOptions);
        var back = JsonSerializer.Deserialize<ControlBinding>(json, ProfileFileOptions)!;

        // Assert
        Assert.That(json, Does.Contain("\"workflow\""));
        Assert.That(back.Workflow, Is.EqualTo(binding.Workflow));
        Assert.That(back.ActionKind, Is.EqualTo(ProfileActionKind.Workflow));
    }

    [Test]
    public void Serialize_ABindingWithoutAWorkflow_WritesNone()
    {
        // Arrange
        var binding = SavedWorkflowBinding() with { Workflow = null };

        // Act
        var json = JsonSerializer.Serialize(binding, ProfileFileOptions);

        // Assert
        Assert.That(json, Does.Not.Contain("\"workflow\""));
    }

    private static ActionSettings Settings(params (string Key, string Value)[] pairs) =>
        new(pairs.Select(static pair => new KeyValuePair<string, string>(pair.Key, pair.Value)));
}

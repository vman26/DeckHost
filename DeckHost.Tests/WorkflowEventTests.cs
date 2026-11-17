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

    private const string ModeVariable = "mode";
    private const string XVariable = "x";
    private const string ObsApp = "obs";
    private const string ChromeProcess = "chrome.exe";
    private const string ObsProcess = "OBS64.exe";
    private const int ThrottleWaitRounds = 40;
    private const int RoundMilliseconds = 10;
    private const int TimerEverySeconds = 30;

    private static readonly string[] OnlyA = ["a"];

    // ---------------------------------------------------------------- a variable changes

    [Test]
    public async Task VariableChanged_ForAnotherVariable_StartsNothing()
    {
        // Arrange
        await Start(Flow(Trigger(WorkflowTriggers.VariableChanged, (WorkflowKeys.Name, "Mode"))));

        // Act
        await Publish(VariableKeys.UserVariable("other"), "1");

        // Assert
        Assert.That(Ran(), Is.Empty);
    }

    [Test]
    public async Task VariableChanged_ForTheNamedVariable_StartsTheWorkflow()
    {
        // Arrange
        await Start(Flow(Trigger(WorkflowTriggers.VariableChanged, (WorkflowKeys.Name, "Mode"))));

        // Act
        await Publish(VariableKeys.UserVariable(ModeVariable), "on");

        // Assert
        Assert.That(Ran(), Is.EqualTo(OnlyA));
    }

    [Test]
    public async Task VariableChanged_ForTheNamedVariableAgainWithAnotherValue_StartsItAgain()
    {
        // Arrange
        await Start(Flow(Trigger(WorkflowTriggers.VariableChanged, (WorkflowKeys.Name, "Mode"))));
        await Publish(VariableKeys.UserVariable(ModeVariable), "on");

        // Act
        await Publish(VariableKeys.UserVariable(ModeVariable), "off");

        // Assert
        Assert.That(Ran(), Is.EqualTo(new[] { "a", "a" }));
    }

    [Test]
    public async Task VariableChanged_AControlScopedTrigger_IgnoresAnotherControlsVariable()
    {
        // Arrange
        var own = Flow(Trigger(WorkflowTriggers.VariableChanged, (WorkflowKeys.Name, XVariable), (WorkflowKeys.Scope, WorkflowKeys.ScopeControl)));
        await Start(new DeviceProfile("profile", "Profile", Device, [Binding(own, "key.0")]));

        // Act
        await Publish(VariableKeys.ControlVariable(Device, "key.1", XVariable), "1");

        // Assert
        Assert.That(Ran(), Is.Empty);
    }

    [Test]
    public async Task VariableChanged_AControlScopedTrigger_IgnoresTheGlobalVariable()
    {
        // Arrange
        var own = Flow(Trigger(WorkflowTriggers.VariableChanged, (WorkflowKeys.Name, XVariable), (WorkflowKeys.Scope, WorkflowKeys.ScopeControl)));
        await Start(new DeviceProfile("profile", "Profile", Device, [Binding(own, "key.0")]));

        // Act
        await Publish(VariableKeys.UserVariable(XVariable), "1");

        // Assert
        Assert.That(Ran(), Is.Empty);
    }

    [Test]
    public async Task VariableChanged_AControlScopedTrigger_StartsForItsOwnControlsVariable()
    {
        // Arrange
        var own = Flow(Trigger(WorkflowTriggers.VariableChanged, (WorkflowKeys.Name, XVariable), (WorkflowKeys.Scope, WorkflowKeys.ScopeControl)));
        await Start(new DeviceProfile("profile", "Profile", Device, [Binding(own, "key.0")]));

        // Act
        await Publish(VariableKeys.ControlVariable(Device, "key.0", XVariable), "1");

        // Assert
        Assert.That(Ran(), Is.EqualTo(OnlyA));
    }

    [Test]
    public async Task VariableChanged_WaitingForOneValue_IgnoresOtherValues()
    {
        // Arrange
        await Start(Flow(Trigger(WorkflowTriggers.VariableChanged, (WorkflowKeys.Name, ModeVariable), (WorkflowKeys.Value, "ON"))));

        // Act
        await Publish(VariableKeys.UserVariable(ModeVariable), "off");

        // Assert
        Assert.That(Ran(), Is.Empty);
    }

    [Test]
    public async Task VariableChanged_TheSameValueSetAgain_IsNoChange()
    {
        // Arrange
        await Start(Flow(Trigger(WorkflowTriggers.VariableChanged, (WorkflowKeys.Name, ModeVariable), (WorkflowKeys.Value, "ON"))));

        // Act
        await Publish(VariableKeys.UserVariable(ModeVariable), "on");
        await Publish(VariableKeys.UserVariable(ModeVariable), "on");

        // Assert
        Assert.That(Ran(), Is.EqualTo(OnlyA));
    }

    // ---------------------------------------------------------------- an app comes to the front

    [Test]
    public async Task AppInFront_AnotherApp_StartsNothing()
    {
        // Arrange
        await Start(Flow(Trigger(WorkflowTriggers.AppInFront, (WorkflowKeys.App, ObsApp))));

        // Act
        await Publish(VariableKeys.ForegroundApp, ChromeProcess);

        // Assert
        Assert.That(Ran(), Is.Empty);
    }

    [Test]
    public async Task AppInFront_TheNamedApp_StartsTheWorkflow()
    {
        // Arrange
        await Start(Flow(Trigger(WorkflowTriggers.AppInFront, (WorkflowKeys.App, ObsApp))));

        // Act
        await Publish(VariableKeys.ForegroundApp, ObsProcess);

        // Assert
        Assert.That(Ran(), Is.EqualTo(OnlyA));
    }

    [Test]
    public async Task AppInFront_TheAppComingBack_StartsItEachTime()
    {
        // Arrange
        await Start(Flow(Trigger(WorkflowTriggers.AppInFront, (WorkflowKeys.App, ObsApp))));
        await Publish(VariableKeys.ForegroundApp, ObsProcess);

        // Act
        await Publish(VariableKeys.ForegroundApp, ChromeProcess);
        await Publish(VariableKeys.ForegroundApp, "obs64.exe");

        // Assert
        Assert.That(Ran(), Is.EqualTo(new[] { "a", "a" }));
    }

    [Test]
    public async Task AppInFront_WithoutAnAppName_StartsOnNothing()
    {
        // Arrange
        await Start(Flow(Trigger(WorkflowTriggers.AppInFront)));

        // Act
        await Publish(VariableKeys.ForegroundApp, "obs64.exe");

        // Assert
        Assert.That(Ran(), Is.Empty);
    }

    // ---------------------------------------------------------------- the profile is shown

    private async Task StartTwoProfilesThatRunAnActionWhenShown()
    {
        var shown = Binding(Flow(Trigger(WorkflowTriggers.ProfileShown), "a"));
        var other = Binding(Flow(Trigger(WorkflowTriggers.ProfileShown), "b"));
        await Start(
            new DeviceProfile("one", "One", Device, [shown]),
            new DeviceProfile("two", "Two", Device, [other]));
    }

    [Test]
    public async Task ProfileShown_TheHostStarting_RunsTheWorkflowOfTheProfileOnTheDevice()
    {
        // Act
        await StartTwoProfilesThatRunAnActionWhenShown();

        // Assert
        Assert.That(Ran(), Is.EqualTo(OnlyA));
    }

    [Test]
    public async Task ProfileShown_AnotherProfileGetsActive_RunsThatProfilesWorkflow()
    {
        // Arrange
        await StartTwoProfilesThatRunAnActionWhenShown();

        // Act
        _host.SetActiveProfile("two");
        await _host.WhenIdleAsync();

        // Assert
        Assert.That(Ran(), Is.EqualTo(new[] { "a", "b" }));
    }

    [Test]
    public async Task ProfileShown_TheFirstProfileComesBack_RunsItsWorkflowAgain()
    {
        // Arrange
        await StartTwoProfilesThatRunAnActionWhenShown();
        _host.SetActiveProfile("two");
        await _host.WhenIdleAsync();

        // Act
        _host.SetActiveProfile("one");
        await _host.WhenIdleAsync();

        // Assert
        Assert.That(Ran(), Is.EqualTo(new[] { "a", "b", "a" }));
    }

    // ---------------------------------------------------------------- timers

    private static readonly DateTime TimerStart = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    private async Task StartEveryThirtySeconds() =>
        await Start(Flow(Trigger(WorkflowTriggers.Timer, (WorkflowKeys.Every, TimerEverySeconds.ToString()))));

    [Test]
    public async Task Timer_BeforeItsIntervalHasPassed_StartsNothing()
    {
        // Arrange
        await StartEveryThirtySeconds();

        // Act
        await _host.TickTimersAsync(TimerStart);
        await _host.TickTimersAsync(TimerStart.AddSeconds(TimerEverySeconds - 1));

        // Assert
        Assert.That(Ran(), Is.Empty, "it counts from the first tick");
    }

    [Test]
    public async Task Timer_WhenItsIntervalHasPassed_StartsTheWorkflow()
    {
        // Arrange
        await StartEveryThirtySeconds();
        await _host.TickTimersAsync(TimerStart);

        // Act
        await _host.TickTimersAsync(TimerStart.AddSeconds(TimerEverySeconds));

        // Assert
        Assert.That(Ran(), Is.EqualTo(OnlyA));
    }

    [Test]
    public async Task Timer_AfterAnotherInterval_StartsTheWorkflowAgain()
    {
        // Arrange
        await StartEveryThirtySeconds();
        await _host.TickTimersAsync(TimerStart);
        await _host.TickTimersAsync(TimerStart.AddSeconds(TimerEverySeconds));

        // Act
        await _host.TickTimersAsync(TimerStart.AddSeconds((2 * TimerEverySeconds) - 1));
        var afterJustUnder = Ran().Count;
        await _host.TickTimersAsync(TimerStart.AddSeconds(2 * TimerEverySeconds));

        // Assert
        Assert.That(afterJustUnder, Is.EqualTo(1));
        Assert.That(Ran(), Has.Count.EqualTo(2));
    }

    // ---------------------------------------------------------------- shown profile only, and loops

    [Test]
    public async Task VariableChanged_OnAProfileThatIsNotShown_StartsNothing()
    {
        // Arrange
        await Start(
            new DeviceProfile("one", "One", Device, []),
            new DeviceProfile("two", "Two", Device, [Binding(Flow(Trigger(WorkflowTriggers.VariableChanged, (WorkflowKeys.Name, XVariable))))]));
        _host.SetActiveProfile("one");
        await _host.WhenIdleAsync();

        // Act
        await Publish(VariableKeys.UserVariable(XVariable), "1");

        // Assert
        Assert.That(Ran(), Is.Empty);
    }

    [Test]
    public async Task VariableChanged_AWorkflowThatChangesTheVariableItStartsOn_IsPausedAtTheLimitInsteadOfLoopingForever()
    {
        // Arrange
        var count = new WorkflowNode("1", WorkflowNodeKinds.SetVariable)
        {
            Settings = new ActionSettings(new Dictionary<string, string>
            {
                [WorkflowKeys.Name] = XVariable,
                [WorkflowKeys.Operation] = WorkflowKeys.Add,
                [WorkflowKeys.Value] = "1",
            }),
        };
        await Start(new Workflow
        {
            Nodes = [Trigger(WorkflowTriggers.VariableChanged, (WorkflowKeys.Name, XVariable)), count],
            Links = [new("t", WorkflowPorts.Next, "1")],
        });

        // Act
        await Publish(VariableKeys.UserVariable(XVariable), "0");
        for (var i = 0; i < ThrottleWaitRounds; i++)
        {
            await Task.Delay(RoundMilliseconds);
            await _host.WhenIdleAsync();
        }

        // Assert
        List<WorkflowTrace> traces;
        lock (_traces)
        {
            traces = [.. _traces];
        }

        Assert.That(traces.Count(static trace => trace.Kind == WorkflowTraceKind.Started), Is.EqualTo(WorkflowEventThrottle.Limit), "it stops at the limit");
    }

    // ---------------------------------------------------------------- the throttle

    private static readonly DateTime ThrottleStart = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static WorkflowEventThrottle ThrottleUsedUpToItsLimit()
    {
        var throttle = new WorkflowEventThrottle();
        for (var i = 0; i < WorkflowEventThrottle.Limit; i++)
        {
            throttle.Allow("k", ThrottleStart.AddMilliseconds(i), out _);
        }

        return throttle;
    }

    [Test]
    public void Allow_ABurstUpToTheLimit_IsAllowed()
    {
        // Arrange
        var throttle = new WorkflowEventThrottle();
        var allowed = new List<bool>();

        // Act
        for (var i = 0; i < WorkflowEventThrottle.Limit; i++)
        {
            allowed.Add(throttle.Allow("k", ThrottleStart.AddMilliseconds(i), out _));
        }

        // Assert
        Assert.That(allowed, Has.All.True);
    }

    [Test]
    public void Allow_OneMoreThanTheLimit_PausesAndSaysSo()
    {
        // Arrange
        var throttle = ThrottleUsedUpToItsLimit();

        // Act
        var allowed = throttle.Allow("k", ThrottleStart.AddSeconds(1), out var paused);

        // Assert
        Assert.That(allowed, Is.False);
        Assert.That(paused, Is.True, "the call that pauses says so");
    }

    [Test]
    public void Allow_WhilePaused_IsRefusedWithoutSayingPausedAgain()
    {
        // Arrange
        var throttle = ThrottleUsedUpToItsLimit();
        throttle.Allow("k", ThrottleStart.AddSeconds(1), out _);

        // Act
        var allowed = throttle.Allow("k", ThrottleStart.AddSeconds(2), out var paused);

        // Assert
        Assert.That(allowed, Is.False);
        Assert.That(paused, Is.False, "only the first call says it");
    }

    [Test]
    public void Allow_AnotherTriggerWhileOneIsPaused_IsNotAffected()
    {
        // Arrange
        var throttle = ThrottleUsedUpToItsLimit();
        throttle.Allow("k", ThrottleStart.AddSeconds(1), out _);

        // Act
        var allowed = throttle.Allow("other", ThrottleStart.AddSeconds(2), out _);

        // Assert
        Assert.That(allowed, Is.True);
    }

    [Test]
    public void Allow_AfterThePauseHasEnded_LetsTheTriggerBackIn()
    {
        // Arrange
        var throttle = ThrottleUsedUpToItsLimit();
        throttle.Allow("k", ThrottleStart.AddSeconds(1), out _);

        // Act
        var allowed = throttle.Allow("k", ThrottleStart.AddSeconds(1) + WorkflowEventThrottle.Pause + TimeSpan.FromSeconds(1), out _);

        // Assert
        Assert.That(allowed, Is.True);
    }

    // ---------------------------------------------------------------- reading the trigger settings

    [Test]
    public void MatchesVariable_ANameAndValueWithSpacesAndOtherCase_StillMatches()
    {
        // Arrange
        var variable = Trigger(WorkflowTriggers.VariableChanged, (WorkflowKeys.Name, " Mode "), (WorkflowKeys.Value, "On"));

        // Act
        var matches = WorkflowEvents.MatchesVariable(variable, VariableKeys.UserVariable(ModeVariable), " on ");

        // Assert
        Assert.That(matches, Is.True);
    }

    [Test]
    public void MatchesVariable_ADifferentValue_DoesNotMatch()
    {
        // Arrange
        var variable = Trigger(WorkflowTriggers.VariableChanged, (WorkflowKeys.Name, " Mode "), (WorkflowKeys.Value, "On"));

        // Act
        var matches = WorkflowEvents.MatchesVariable(variable, VariableKeys.UserVariable(ModeVariable), "off");

        // Assert
        Assert.That(matches, Is.False);
    }

    [Test]
    public void MatchesVariable_NoName_DoesNotMatch()
    {
        // Arrange
        var variable = Trigger(WorkflowTriggers.VariableChanged);

        // Act
        var matches = WorkflowEvents.MatchesVariable(variable, VariableKeys.UserVariable(ModeVariable), "on");

        // Assert
        Assert.That(matches, Is.False);
    }

    [TestCase("Code", "code.exe", true)]
    [TestCase("", "code.exe", false)]
    public void MatchesApp_ANameAgainstTheAppInFront_MatchesPartOfItsNameIgnoringCase(string wanted, string app, bool expected)
    {
        // Arrange
        var trigger = Trigger(WorkflowTriggers.AppInFront, (WorkflowKeys.App, wanted));

        // Act
        var matches = WorkflowEvents.MatchesApp(trigger, app);

        // Assert
        Assert.That(matches, Is.EqualTo(expected));
    }

    [TestCase("0", 1)]
    [TestCase("999999", WorkflowKeys.MaxEverySeconds)]
    public void EveryOf_AnIntervalOutOfRange_IsClamped(string every, int expectedSeconds)
    {
        // Act
        var interval = WorkflowEvents.EveryOf(Trigger(WorkflowTriggers.Timer, (WorkflowKeys.Every, every)));

        // Assert
        Assert.That(interval, Is.EqualTo(TimeSpan.FromSeconds(expectedSeconds)));
    }

    [Test]
    public void EveryOf_NoInterval_IsAMinute()
    {
        // Act
        var interval = WorkflowEvents.EveryOf(Trigger(WorkflowTriggers.Timer));

        // Assert
        Assert.That(interval, Is.EqualTo(TimeSpan.FromSeconds(60)));
    }

    // ---------------------------------------------------------------- describing a trigger

    [Test]
    public void Summarize_AProfileShownTrigger_SaysWhenTheProfileIsShown()
    {
        // Act
        var summary = WorkflowNodeTypes.Summarize(Trigger(WorkflowTriggers.ProfileShown));

        // Assert
        Assert.That(summary, Is.EqualTo("When the profile is shown"));
    }

    [Test]
    public void Summarize_AnAppTrigger_NamesTheApp()
    {
        // Act
        var summary = WorkflowNodeTypes.Summarize(Trigger(WorkflowTriggers.AppInFront, (WorkflowKeys.App, ObsApp)));

        // Assert
        Assert.That(summary, Is.EqualTo("When obs comes to the front"));
    }

    [Test]
    public void Summarize_AVariableTriggerWithoutAValue_SaysWhenItChanges()
    {
        // Act
        var summary = WorkflowNodeTypes.Summarize(Trigger(WorkflowTriggers.VariableChanged, (WorkflowKeys.Name, ModeVariable)));

        // Assert
        Assert.That(summary, Is.EqualTo("When mode changes"));
    }

    [Test]
    public void Summarize_AVariableTriggerWithAValue_SaysWhenItBecomesThatValue()
    {
        // Act
        var summary = WorkflowNodeTypes.Summarize(Trigger(WorkflowTriggers.VariableChanged, (WorkflowKeys.Name, ModeVariable), (WorkflowKeys.Value, "on")));

        // Assert
        Assert.That(summary, Is.EqualTo("When mode becomes on"));
    }

    [Test]
    public void Summarize_ATimerTrigger_SaysHowOften()
    {
        // Act
        var summary = WorkflowNodeTypes.Summarize(Trigger(WorkflowTriggers.Timer, (WorkflowKeys.Every, "90")));

        // Assert
        Assert.That(summary, Is.EqualTo("Every 90 s"));
    }

    [TestCase(WorkflowTriggers.Timer, true)]
    [TestCase(WorkflowTriggers.Down, false)]
    public void IsEvent_ATriggerKind_IsTrueOnlyForTheOnesThatStartByThemselves(string trigger, bool expected)
    {
        // Act
        var isEvent = WorkflowTriggers.IsEvent(trigger);

        // Assert
        Assert.That(isEvent, Is.EqualTo(expected));
    }
}

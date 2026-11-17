using DeckHost.App.ViewModels;
using DeckHost.Contracts.Enums;
using DeckHost.Contracts.Models;
using DeckHost.Core.Devices;
using DeckHost.Core.Models;
using DeckHost.Core.State;

namespace DeckHost.Tests;

public sealed class KeyLogicTests : IAsyncDisposable
{
    private const string Device = FakeDevicePlugin.DeviceId;
    private const string PluginId = "p";
    private const string Key = "key.0";
    private const string Scaffold = "scaffold";
    private const string A = "a";
    private const string B = "b";
    private const string C = "c";
    private const int LongDelayMilliseconds = 250;
    private const int SomeDelayMilliseconds = 1500;
    private const int EditorDelayMilliseconds = 750;
    private const int FirstDelayMilliseconds = 200;
    private const int TheWaitWasAtLeast = 240;
    private readonly VariableStore _store = new();
    private readonly RecordingAction _a = new("a");
    private readonly RecordingAction _b = new("b");
    private readonly RecordingAction _c = new("c");
    private readonly RecordingAction _scaffold = new("scaffold");
    private readonly DeviceHostService _host;
    private readonly List<string> _ran = [];

    public KeyLogicTests()
    {
        foreach (var action in new[] { _a, _b, _c })
        {
            // Count presses only: a step gets a press and a release.
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

        _host = new DeviceHostService([new FakeDevicePlugin()], [new TestApplicationPlugin(PluginId, _a, _b, _c, _scaffold)], _store);
    }

    public async ValueTask DisposeAsync()
    {
        await _host.DisposeAsync();
        _store.Dispose();
    }

    private static MultiActionStep Step(int order, string actionId, string? group = null, int delayMs = 0) =>
        new(order, Bindings.Reference(PluginId, actionId)) { Group = group, DelayMs = delayMs };

    private async Task Start(ControlBinding binding)
    {
        _host.SetProfiles([new DeviceProfile("profile", "Profile", Device, [binding])]);
        await _host.StartAsync(CancellationToken.None);
        await _host.WhenIdleAsync();
    }

    private async Task Input(InputCapabilities type)
    {
        _host.SimulateInput(new DeviceInputEvent(Device, Key, type, type == InputCapabilities.Release ? 0 : 1));
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

    private async Task WaitFor(int count)
    {
        var end = DateTime.UtcNow.AddSeconds(5);
        while (Ran().Count < count && DateTime.UtcNow < end)
        {
            await Task.Delay(20);
            await _host.WhenIdleAsync();
        }
    }

    private static ControlBinding SwitchBinding() => new(Key, ProfileActionKind.MultiActionSwitch, Bindings.Reference(PluginId, Scaffold), new ControlAppearance(),
        [Step(0, A, StepGroups.First), Step(1, B, StepGroups.Second), Step(2, C, StepGroups.First)]);

    private static ControlBinding KeyLogicBinding(params MultiActionStep[] steps) =>
        new(Key, ProfileActionKind.KeyLogic, Bindings.Reference(PluginId, Scaffold), new ControlAppearance(), steps);

    private static ControlBinding PressDoublePressAndHold() =>
        KeyLogicBinding(Step(0, A, StepGroups.Press), Step(1, B, StepGroups.DoublePress), Step(2, C, StepGroups.Hold));

    private async Task WaitOutTheDoublePressWindow()
    {
        await Task.Delay(DeviceHostService.DoublePressWindow * 2);
        await _host.WhenIdleAsync();
    }

    [Test]
    public async Task Click_MultiActionSwitchThreeTimes_AlternatesBetweenItsSets()
    {
        // Arrange
        await Start(SwitchBinding());

        // Act
        await Click();
        await Click();
        await Click();

        // Assert
        Assert.That(Ran(), Is.EqualTo(new[] { A, C, B, A, C }));
    }

    [Test]
    public async Task Click_MultiActionSwitchThreeTimes_RemembersTheSetToRunNext()
    {
        // Arrange
        await Start(SwitchBinding());

        // Act
        await Click();
        await Click();
        await Click();

        // Assert
        Assert.That(_store.TryGetValue<string>(DeckHost.Contracts.VariableKeys.For(Device, Key, DeckHost.Contracts.VariableKeys.SwitchState), out var next), Is.True);
        Assert.That(next, Is.EqualTo(StepGroups.Second));
    }

    [Test]
    public async Task Click_KeyLogicSinglePress_RunsThePressStepsAfterTheDoublePressWindow()
    {
        // Arrange
        await Start(PressDoublePressAndHold());

        // Act
        await Click();
        await WaitFor(1);

        // Assert
        Assert.That(Ran(), Is.EqualTo(new[] { A }));
    }

    [Test]
    public async Task Click_KeyLogicClickedTwiceQuickly_RunsTheDoublePressStepsAndNotTwoPresses()
    {
        // Arrange
        await Start(PressDoublePressAndHold());

        // Act
        await Click();
        await Click();
        await WaitOutTheDoublePressWindow();

        // Assert
        Assert.That(Ran(), Is.EqualTo(new[] { B }));
    }

    [Test]
    public async Task Input_KeyLogicHeld_RunsTheHoldStepsAndNoPressAfterIt()
    {
        // Arrange
        await Start(PressDoublePressAndHold());

        // Act
        await Input(InputCapabilities.Press);
        await Input(InputCapabilities.LongPress);
        await Input(InputCapabilities.Release);
        await WaitOutTheDoublePressWindow();

        // Assert
        Assert.That(Ran(), Is.EqualTo(new[] { C }));
    }

    [Test]
    public async Task Click_KeyLogicWithoutDoublePressSteps_RunsAPressAtOnce()
    {
        // Arrange
        await Start(KeyLogicBinding(Step(0, A, StepGroups.Press)));

        // Act
        await Click();

        // Assert
        Assert.That(Ran(), Is.EqualTo(new[] { A }));
    }

    private static ControlBinding MultiActionWithAWait() => new(Key, ProfileActionKind.MultiAction, Bindings.Reference(PluginId, Scaffold), new ControlAppearance(),
        [Step(0, A), Step(1, B, delayMs: LongDelayMilliseconds), Step(2, C)]);

    [Test]
    public async Task Input_MultiActionWithAWaitingStep_RunsTheFirstStepAndLeavesTheRestWaiting()
    {
        // Arrange
        await Start(MultiActionWithAWait());

        // Act
        await Input(InputCapabilities.Press);

        // Assert
        Assert.That(Ran(), Is.EqualTo(new[] { A }));
    }

    [Test]
    public async Task Input_MultiActionWithAWaitingStep_RunsTheRestAfterTheWait()
    {
        // Arrange
        await Start(MultiActionWithAWait());
        var started = DateTime.UtcNow;

        // Act
        await Input(InputCapabilities.Press);
        await WaitFor(3);

        // Assert
        Assert.That(Ran(), Is.EqualTo(new[] { A, B, C }));
        Assert.That(DateTime.UtcNow - started, Is.GreaterThanOrEqualTo(TimeSpan.FromMilliseconds(TheWaitWasAtLeast)));
    }

    [Test]
    public void Deserialize_StepSavedWithASetAndAWait_KeepsBoth()
    {
        // Arrange
        var step = Step(3, A, StepGroups.Hold, SomeDelayMilliseconds);
        var json = System.Text.Json.JsonSerializer.Serialize(step);

        // Act
        var loaded = System.Text.Json.JsonSerializer.Deserialize<MultiActionStep>(json)!;

        // Assert
        Assert.That((loaded.Group, loaded.DelayMs), Is.EqualTo((StepGroups.Hold, SomeDelayMilliseconds)));
    }

    [Test]
    public void Of_StepOfAConvertedMultiActionWithoutASet_IsInTheFirstSetOfTheSwitch()
    {
        // Arrange
        var step = Step(0, A);

        // Act
        var group = StepGroups.Of(step, ProfileActionKind.MultiActionSwitch);

        // Assert
        Assert.That(group, Is.EqualTo(StepGroups.First));
    }

    [Test]
    public void Of_StepWithASetInAPlainMultiAction_IsInNoSet()
    {
        // Arrange
        var step = Step(0, A, StepGroups.Hold);

        // Act
        var group = StepGroups.Of(step, ProfileActionKind.MultiAction);

        // Assert
        Assert.That(group, Is.Null);
    }

    private static ControlCustomizationViewModel KeyLogicEditor()
    {
        var builtIns = DeckHost.App.SystemPlugins.SystemPluginSet.Create();
        var items = builtIns.SelectMany(plugin => plugin.Actions.Select(action => ActionItemViewModel.Create(plugin.Id, plugin.Name, action))).ToList();
        var keyLogic = items.First(item => item.Name == "Key Logic");
        var mute = items.First(item => item.Name == "Mute");
        var stop = items.First(item => item.Name == "Stop");
        var control = new ControlItemViewModel(new PhysicalControl(Key, "Key 1", ControlType.Key, new Rect(0, 0, 10, 10),
            InputCapabilities.Press | InputCapabilities.Release | InputCapabilities.LongPress, OutputCapabilities.ColorImage));
        var model = new ControlBinding(Key, ProfileActionKind.KeyLogic, keyLogic.ToActionReference(), new ControlAppearance(),
        [
            new MultiActionStep(0, mute.ToActionReference()) { Group = StepGroups.Hold, DelayMs = FirstDelayMilliseconds },
            new MultiActionStep(1, stop.ToActionReference()) { Group = StepGroups.Press },
        ]);
        return new ControlCustomizationViewModel(control,
            new ControlBindingViewModel(model, keyLogic, [new StepItemViewModel(mute, ActionSettings.Empty), new StepItemViewModel(stop, ActionSettings.Empty)]),
            items, null);
    }

    [Test]
    public void StepGroupOptions_KeyLogicEditor_OffersPressDoublePressAndHold()
    {
        // Arrange
        var editor = KeyLogicEditor();

        // Act
        var labels = editor.StepGroupOptions.Select(option => option.Label);

        // Assert
        Assert.That(labels, Is.EqualTo(new[] { "Press", "Double press", "Hold" }));
    }

    [Test]
    public void BuildBinding_StepGivenAnotherSetAndWait_KeepsEachStepsSetAndWait()
    {
        // Arrange
        var editor = KeyLogicEditor();
        editor.Steps[1].Group = editor.StepGroupOptions[1];
        editor.Steps[1].DelayMilliseconds = EditorDelayMilliseconds;

        // Act
        var built = editor.BuildBinding()!;

        // Assert
        Assert.That(built.ActionKind, Is.EqualTo(ProfileActionKind.KeyLogic));
        Assert.That(built.Steps.Select(step => (step.Group, step.DelayMs)), Is.EqualTo(new[] { (StepGroups.Hold, FirstDelayMilliseconds), (StepGroups.DoublePress, EditorDelayMilliseconds) }));
    }
}

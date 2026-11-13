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

        _host = new DeviceHostService([new FakeDevicePlugin()], [new TestApplicationPlugin("p", _a, _b, _c, _scaffold)], _store);
    }

    public async ValueTask DisposeAsync()
    {
        await _host.DisposeAsync();
        _store.Dispose();
    }

    private static MultiActionStep Step(int order, string actionId, string? group = null, int delayMs = 0) =>
        new(order, Bindings.Reference("p", actionId)) { Group = group, DelayMs = delayMs };

    private async Task Start(ControlBinding binding)
    {
        _host.SetProfiles([new DeviceProfile("profile", "Profile", Device, [binding])]);
        await _host.StartAsync(CancellationToken.None);
        await _host.WhenIdleAsync();
    }

    private async Task Input(InputCapabilities type)
    {
        _host.SimulateInput(new DeviceInputEvent(Device, "key.0", type, type == InputCapabilities.Release ? 0 : 1));
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

    [Fact]
    public async Task A_multi_action_switch_alternates_between_its_sets()
    {
        await Start(new ControlBinding("key.0", ProfileActionKind.MultiActionSwitch, Bindings.Reference("p", "scaffold"), new ControlAppearance(),
            [Step(0, "a", StepGroups.First), Step(1, "b", StepGroups.Second), Step(2, "c", StepGroups.First)]));

        await Click();
        await Click();
        await Click();

        Assert.Equal(["a", "c", "b", "a", "c"], Ran());
        Assert.True(_store.TryGetValue<string>(DeckHost.Contracts.VariableKeys.For(Device, "key.0", DeckHost.Contracts.VariableKeys.SwitchState), out var next));
        Assert.Equal(StepGroups.Second, next);
    }

    [Fact]
    public async Task Key_logic_tells_a_press_a_double_press_and_a_hold_apart()
    {
        await Start(new ControlBinding("key.0", ProfileActionKind.KeyLogic, Bindings.Reference("p", "scaffold"), new ControlAppearance(),
            [Step(0, "a", StepGroups.Press), Step(1, "b", StepGroups.DoublePress), Step(2, "c", StepGroups.Hold)]));

        await Click();
        await WaitFor(1);
        Assert.Equal(["a"], Ran()); // after the double-press window

        await Click();
        await Click();
        await Task.Delay(DeviceHostService.DoublePressWindow * 2);
        await _host.WhenIdleAsync();
        Assert.Equal(["a", "b"], Ran()); // the double press, not two presses

        await Input(InputCapabilities.Press);
        await Input(InputCapabilities.LongPress);
        await Input(InputCapabilities.Release);
        await Task.Delay(DeviceHostService.DoublePressWindow * 2);
        await _host.WhenIdleAsync();
        Assert.Equal(["a", "b", "c"], Ran()); // the hold, and no press after it
    }

    [Fact]
    public async Task Key_logic_without_double_press_steps_runs_a_press_at_once()
    {
        await Start(new ControlBinding("key.0", ProfileActionKind.KeyLogic, Bindings.Reference("p", "scaffold"), new ControlAppearance(),
            [Step(0, "a", StepGroups.Press)]));

        await Click();

        Assert.Equal(["a"], Ran());
    }

    [Fact]
    public async Task Steps_wait_without_blocking_the_device()
    {
        await Start(new ControlBinding("key.0", ProfileActionKind.MultiAction, Bindings.Reference("p", "scaffold"), new ControlAppearance(),
            [Step(0, "a"), Step(1, "b", delayMs: 250), Step(2, "c")]));
        var started = DateTime.UtcNow;

        await Input(InputCapabilities.Press);
        Assert.Equal(["a"], Ran()); // the rest waits; the worker is free meanwhile

        await WaitFor(3);
        Assert.Equal(["a", "b", "c"], Ran());
        Assert.True(DateTime.UtcNow - started >= TimeSpan.FromMilliseconds(240));
    }

    [Fact]
    public void Sets_and_waits_survive_saving()
    {
        var step = Step(3, "a", StepGroups.Hold, 1500);
        var loaded = System.Text.Json.JsonSerializer.Deserialize<MultiActionStep>(System.Text.Json.JsonSerializer.Serialize(step))!;

        Assert.Equal((StepGroups.Hold, 1500), (loaded.Group, loaded.DelayMs));
        Assert.Equal(StepGroups.First, StepGroups.Of(Step(0, "a"), ProfileActionKind.MultiActionSwitch)); // a converted Multi Action's steps
        Assert.Null(StepGroups.Of(Step(0, "a", StepGroups.Hold), ProfileActionKind.MultiAction));
    }

    [Fact]
    public void The_editor_keeps_each_steps_set_and_wait()
    {
        var builtIns = DeckHost.App.SystemPlugins.SystemPluginSet.Create();
        var items = builtIns.SelectMany(plugin => plugin.Actions.Select(action => ActionItemViewModel.Create(plugin.Id, plugin.Name, action))).ToList();
        var keyLogic = items.First(item => item.Name == "Key Logic");
        var mute = items.First(item => item.Name == "Mute");
        var stop = items.First(item => item.Name == "Stop");
        var control = new ControlItemViewModel(new PhysicalControl("key.0", "Key 1", ControlType.Key, new Rect(0, 0, 10, 10),
            InputCapabilities.Press | InputCapabilities.Release | InputCapabilities.LongPress, OutputCapabilities.ColorImage));
        var model = new ControlBinding("key.0", ProfileActionKind.KeyLogic, keyLogic.ToActionReference(), new ControlAppearance(),
        [
            new MultiActionStep(0, mute.ToActionReference()) { Group = StepGroups.Hold, DelayMs = 200 },
            new MultiActionStep(1, stop.ToActionReference()) { Group = StepGroups.Press },
        ]);
        var editor = new ControlCustomizationViewModel(control,
            new ControlBindingViewModel(model, keyLogic, [new StepItemViewModel(mute, ActionSettings.Empty), new StepItemViewModel(stop, ActionSettings.Empty)]),
            items, null);

        Assert.Equal(["Press", "Double press", "Hold"], editor.StepGroupOptions.Select(option => option.Label));
        editor.Steps[1].Group = editor.StepGroupOptions[1];
        editor.Steps[1].DelayMilliseconds = 750;
        var built = editor.BuildBinding()!;

        Assert.Equal(ProfileActionKind.KeyLogic, built.ActionKind);
        Assert.Equal([(StepGroups.Hold, 200), (StepGroups.DoublePress, 750)], built.Steps.Select(step => (step.Group, step.DelayMs)));
    }
}

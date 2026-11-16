using DeckHost.App.SystemPlugins;
using DeckHost.App.ViewModels;
using DeckHost.Contracts;
using DeckHost.Contracts.Enums;
using DeckHost.Contracts.Interfaces;
using DeckHost.Contracts.Models;

namespace DeckHost.Tests;

public class InlineEditorTests
{
    private static readonly IReadOnlyList<ActionItemViewModel> Actions =
        [.. SystemPluginSet.Create().SelectMany(plugin => plugin.Actions.Select(action => ActionItemViewModel.Create(plugin.Id, plugin.Name, action)))];

    private static ActionItemViewModel Item(string name) => Actions.First(action => action.Name == name);

    private static ControlCustomizationViewModel MultiActionEditor(params string[] steps)
    {
        var control = new ControlItemViewModel(new PhysicalControl("key.0", "Key 1", ControlType.Key, new Rect(0, 0, 10, 10),
            InputCapabilities.Press | InputCapabilities.Release, OutputCapabilities.ColorImage | OutputCapabilities.Text));
        var multi = Item("Multi Action");
        var stepItems = steps.Select(name => new StepItemViewModel(Item(name), new ActionSettings([]))).ToList();
        var model = new ControlBinding("key.0", ProfileActionKind.MultiAction, multi.ToActionReference(), new ControlAppearance(),
            [.. stepItems.Select((step, index) => new MultiActionStep(index, step.Action.ToActionReference()))]);
        return new ControlCustomizationViewModel(control, new ControlBindingViewModel(model, multi, stepItems), Actions, null);
    }

    private static IEnumerable<string> StepNames(ControlCustomizationViewModel editor) =>
        editor.BuildBinding()!.Steps.Select(step => step.Action.ActionName);

    [Test]
    public void A_dropped_action_becomes_a_step_where_it_was_dropped()
    {
        var editor = MultiActionEditor("Mute", "Next Track");
        var changes = 0;
        editor.Changed += (_, _) => changes++;

        Assert.That(editor.CanInsertStep(Item("Lock Computer").Action), Is.True);
        Assert.That(editor.InsertStep(Item("Lock Computer").Action, 1), Is.True);

        Assert.That(StepNames(editor), Is.EqualTo(new[] { "Mute", "Lock Computer", "Next Track" }));
        Assert.That(changes, Is.EqualTo(1));
    }

    [Test]
    public void Actions_that_cant_be_steps_are_refused()
    {
        var editor = MultiActionEditor("Mute");

        Assert.That(editor.CanInsertStep(Item("Multi Action").Action), Is.False);
        Assert.That(editor.InsertStep(Item("Multi Action").Action, 0), Is.False);
        Assert.That(StepNames(editor), Is.EqualTo(new[] { "Mute" }));
    }

    [TestCase(0, 3, new[] { "B", "C", "A" })] // dragged to the end
    [TestCase(2, 0, new[] { "C", "A", "B" })] // dragged to the top
    [TestCase(0, 2, new[] { "B", "A", "C" })] // dropped above C
    [TestCase(1, 1, new[] { "A", "B", "C" })] // dropped where it is
    [TestCase(1, 2, new[] { "A", "B", "C" })] // just below itself: same place
    public void A_dragged_step_moves_to_where_it_was_dropped(int from, int to, string[] expected)
    {
        var names = new Dictionary<string, string> { ["A"] = "Mute", ["B"] = "Next Track", ["C"] = "Stop" };
        var editor = MultiActionEditor(names["A"], names["B"], names["C"]);

        editor.MoveStepTo(from, to);

        Assert.That(StepNames(editor), Is.EqualTo(expected.Select(letter => names[letter])));
    }

    [Test]
    public void The_drop_marker_shows_where_a_step_lands()
    {
        var editor = MultiActionEditor("Mute", "Next Track");

        editor.DropIndex = 1;
        Assert.That(editor.Steps.Select(step => step.IsDropBefore), Is.EqualTo(new[] { false, true }));
        Assert.That(editor.IsDropAtEnd, Is.False);

        editor.DropIndex = 2;
        Expect.All(editor.Steps, step => Assert.That(step.IsDropBefore, Is.False));
        Assert.That(editor.IsDropAtEnd, Is.True);

        editor.DropIndex = null;
        Assert.That(editor.IsDropAtEnd, Is.False);
    }

    [Test]
    public void Revert_and_remove_are_asked_of_the_main_window()
    {
        var editor = MultiActionEditor("Mute");
        var asked = new List<string>();
        editor.RevertRequested += (_, _) => asked.Add("revert");
        editor.RemoveRequested += (_, _) => asked.Add("remove");

        editor.RevertCommand.Execute(null);
        editor.RemoveCommand.Execute(null);

        Assert.That(asked, Is.EqualTo(new[] { "revert", "remove" }));
        Assert.That(editor.HasAssignment, Is.True);
    }

    [TestCase("Play / Pause", Icons.PlayPause)]
    [TestCase("Clock", Icons.Clock)]
    [TestCase("Counter", Icons.Counter)]
    [TestCase("Multi Action", Icons.Stack)]
    [TestCase("Track Swipe", Icons.Swipe)]
    public void Actions_have_an_icon_for_the_list(string name, string icon) =>
        Assert.That(Item(name).Action.ListIcon, Is.EqualTo(icon));

    [Test]
    public void Every_listed_action_draws_a_list_icon_without_a_control()
    {
        foreach (var action in Actions.Where(action => action.ShowInPalette))
        {
            Assert.That(string.IsNullOrEmpty(action.Action.ListIcon), Is.False, $"{action.Name} has no list icon");
        }
    }
}

using DeckHost.App.SystemPlugins;
using DeckHost.App.ViewModels;
using DeckHost.Contracts;
using DeckHost.Contracts.Enums;
using DeckHost.Contracts.Interfaces;
using DeckHost.Contracts.Models;

namespace DeckHost.Tests;

public class InlineEditorTests
{
    private const string Mute = "Mute";
    private const string NextTrack = "Next Track";
    private const string Stop = "Stop";
    private const string LockComputer = "Lock Computer";
    private const string MultiAction = "Multi Action";
    private const string RevertAsked = "revert";
    private const string RemoveAsked = "remove";

    private static readonly IReadOnlyList<ActionItemViewModel> Actions =
        [.. SystemPluginSet.Create().SelectMany(plugin => plugin.Actions.Select(action => ActionItemViewModel.Create(plugin.Id, plugin.Name, action)))];

    private static ActionItemViewModel Item(string name) => Actions.First(action => action.Name == name);

    private static ControlCustomizationViewModel MultiActionEditor(params string[] steps)
    {
        var control = new ControlItemViewModel(new PhysicalControl("key.0", "Key 1", ControlType.Key, new Rect(0, 0, 10, 10),
            InputCapabilities.Press | InputCapabilities.Release, OutputCapabilities.ColorImage | OutputCapabilities.Text));
        var multi = Item(MultiAction);
        var stepItems = steps.Select(name => new StepItemViewModel(Item(name), new ActionSettings([]))).ToList();
        var model = new ControlBinding("key.0", ProfileActionKind.MultiAction, multi.ToActionReference(), new ControlAppearance(),
            [.. stepItems.Select((step, index) => new MultiActionStep(index, step.Action.ToActionReference()))]);
        return new ControlCustomizationViewModel(control, new ControlBindingViewModel(model, multi, stepItems), Actions, null);
    }

    private static IEnumerable<string> StepNames(ControlCustomizationViewModel editor) =>
        editor.BuildBinding()!.Steps.Select(step => step.Action.ActionName);

    // ---------------------------------------------------------------- dropping actions as steps

    [Test]
    public void InsertStep_AnActionDroppedBetweenTwoSteps_BecomesTheStepAtThatPlace()
    {
        // Arrange
        var editor = MultiActionEditor(Mute, NextTrack);

        // Act
        var inserted = editor.InsertStep(Item(LockComputer).Action, 1);

        // Assert
        Assert.That(inserted, Is.True);
        Assert.That(StepNames(editor), Is.EqualTo(new[] { Mute, LockComputer, NextTrack }));
    }

    [Test]
    public void InsertStep_AnAction_RaisesChangedOnce()
    {
        // Arrange
        var editor = MultiActionEditor(Mute, NextTrack);
        var changes = 0;
        editor.Changed += (_, _) => changes++;

        // Act
        editor.InsertStep(Item(LockComputer).Action, 1);

        // Assert
        Assert.That(changes, Is.EqualTo(1));
    }

    [Test]
    public void CanInsertStep_AnActionThatFitsAsAStep_IsTrue()
    {
        // Arrange
        var editor = MultiActionEditor(Mute);

        // Act
        var canInsert = editor.CanInsertStep(Item(LockComputer).Action);

        // Assert
        Assert.That(canInsert, Is.True);
    }

    [Test]
    public void InsertStep_AnActionThatCantBeAStep_IsRefusedAndNothingChanges()
    {
        // Arrange
        var editor = MultiActionEditor(Mute);

        // Act
        var canInsert = editor.CanInsertStep(Item(MultiAction).Action);
        var inserted = editor.InsertStep(Item(MultiAction).Action, 0);

        // Assert
        Assert.That(canInsert, Is.False);
        Assert.That(inserted, Is.False);
        Assert.That(StepNames(editor), Is.EqualTo(new[] { Mute }));
    }

    // ---------------------------------------------------------------- reordering

    [TestCase(0, 3, new[] { "B", "C", "A" }, TestName = "MoveStepTo_FirstStepDraggedToTheEnd_MovesItLast")]
    [TestCase(2, 0, new[] { "C", "A", "B" }, TestName = "MoveStepTo_LastStepDraggedToTheTop_MovesItFirst")]
    [TestCase(0, 2, new[] { "B", "A", "C" }, TestName = "MoveStepTo_FirstStepDroppedAboveTheThird_MovesItBetween")]
    [TestCase(1, 1, new[] { "A", "B", "C" }, TestName = "MoveStepTo_StepDroppedWhereItIs_KeepsTheOrder")]
    [TestCase(1, 2, new[] { "A", "B", "C" }, TestName = "MoveStepTo_StepDroppedJustBelowItself_KeepsTheOrder")]
    public void MoveStepTo_Scenarios(int from, int to, string[] expected)
    {
        // Arrange
        var names = new Dictionary<string, string> { ["A"] = Mute, ["B"] = NextTrack, ["C"] = Stop };
        var editor = MultiActionEditor(names["A"], names["B"], names["C"]);

        // Act
        editor.MoveStepTo(from, to);

        // Assert
        Assert.That(StepNames(editor), Is.EqualTo(expected.Select(letter => names[letter])));
    }

    // ---------------------------------------------------------------- the drop marker

    [Test]
    public void DropIndex_BeforeTheSecondStep_MarksThatStep()
    {
        // Arrange
        var editor = MultiActionEditor(Mute, NextTrack);

        // Act
        editor.DropIndex = 1;

        // Assert
        Assert.That(editor.Steps.Select(step => step.IsDropBefore), Is.EqualTo(new[] { false, true }));
        Assert.That(editor.IsDropAtEnd, Is.False);
    }

    [Test]
    public void DropIndex_AfterTheLastStep_MarksTheEnd()
    {
        // Arrange
        var editor = MultiActionEditor(Mute, NextTrack);

        // Act
        editor.DropIndex = 2;

        // Assert
        Expect.All(editor.Steps, step => Assert.That(step.IsDropBefore, Is.False));
        Assert.That(editor.IsDropAtEnd, Is.True);
    }

    [Test]
    public void DropIndex_Cleared_MarksNothing()
    {
        // Arrange
        var editor = MultiActionEditor(Mute, NextTrack);
        editor.DropIndex = 2;

        // Act
        editor.DropIndex = null;

        // Assert
        Assert.That(editor.IsDropAtEnd, Is.False);
    }

    // ---------------------------------------------------------------- revert and remove

    [Test]
    public void RevertAndRemove_Commands_AreAskedOfTheMainWindow()
    {
        // Arrange
        var editor = MultiActionEditor(Mute);
        var asked = new List<string>();
        editor.RevertRequested += (_, _) => asked.Add(RevertAsked);
        editor.RemoveRequested += (_, _) => asked.Add(RemoveAsked);

        // Act
        editor.RevertCommand.Execute(null);
        editor.RemoveCommand.Execute(null);

        // Assert
        Assert.That(asked, Is.EqualTo(new[] { RevertAsked, RemoveAsked }));
        Assert.That(editor.HasAssignment, Is.True);
    }

    // ---------------------------------------------------------------- list icons

    [TestCase("Play / Pause", Icons.PlayPause)]
    [TestCase("Clock", Icons.Clock)]
    [TestCase("Counter", Icons.Counter)]
    [TestCase(MultiAction, Icons.Stack)]
    [TestCase("Track Swipe", Icons.Swipe)]
    public void ListIcon_OfAKnownAction_IsItsIcon(string name, string icon)
    {
        // Act
        var listIcon = Item(name).Action.ListIcon;

        // Assert
        Assert.That(listIcon, Is.EqualTo(icon));
    }

    [Test]
    public void ListIcon_OfEveryListedAction_IsNotEmpty()
    {
        // Act
        var withoutIcon = Actions.Where(action => action.ShowInPalette && string.IsNullOrEmpty(action.Action.ListIcon)).Select(static action => action.Name).ToList();

        // Assert
        Assert.That(withoutIcon, Is.Empty, "these actions have no list icon");
    }
}

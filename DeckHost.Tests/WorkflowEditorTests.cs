using DeckHost.App.SystemPlugins;
using DeckHost.App.ViewModels;
using DeckHost.Contracts.Enums;
using DeckHost.Contracts.Models;
using DeckHost.Core.Models;
using DeckHost.Core.Workflows;

namespace DeckHost.Tests;

public class WorkflowEditorTests
{
    private static readonly IReadOnlyList<ActionItemViewModel> Actions =
        [.. SystemPluginSet.Create().SelectMany(plugin => plugin.Actions.Select(action => ActionItemViewModel.Create(plugin.Id, plugin.Name, action)))];

    private static readonly PhysicalControl Key = new("key.0", "Key 1", ControlType.Key, new Rect(0, 0, 10, 10),
        InputCapabilities.Press | InputCapabilities.Release | InputCapabilities.LongPress, OutputCapabilities.ColorImage | OutputCapabilities.Text);

    private static ActionItemViewModel Item(string name) => Actions.First(action => action.Name == name);

    private static (WorkflowEditorViewModel Editor, List<Workflow> Stored) Editor(Workflow? workflow = null, bool accept = true)
    {
        var stored = new List<Workflow>();
        var editor = new WorkflowEditorViewModel("device", "layout", Key, "Key 1", workflow ?? WorkflowConverter.Starter(), Actions, next =>
        {
            stored.Add(next);
            return accept;
        });
        return (editor, stored);
    }

    private const string Wait = WorkflowNodeKinds.Wait;
    private const string DeviceName = "device";
    private const string KeyControl = "key.0";
    private const double RoomGap = 60;

    private static (WorkflowNode First, WorkflowNode Last, WorkflowLink Link, WorkflowNode Loose) TwoWaitsAndALooseOne(WorkflowEditorViewModel editor)
    {
        var first = editor.AddNode(Wait, 300, 40);
        var last = editor.AddNode(Wait, 600, 40);
        editor.Connect(first.Id, WorkflowPorts.Next, last.Id);
        var link = Expect.Single(editor.Workflow.Links);
        var loose = editor.AddNode(Wait, 450, 200);
        return (first, last, link, loose);
    }

    // ---------------------------------------------------------------- adding and linking nodes

    [Test]
    public void AddAction_WithANodeSelected_LinksItAfterThatNode()
    {
        // Arrange
        var (editor, _) = Editor();
        editor.SelectNode("n1");

        // Act
        var node = editor.AddAction(Item("Mute"))!;

        // Assert
        Assert.That(Expect.Single(editor.Workflow.Links), Is.EqualTo(new WorkflowLink("n1", WorkflowPorts.Next, node.Id)));
        Assert.That(editor.SelectedNode?.Id, Is.EqualTo(node.Id));
    }

    [Test]
    public void AddAction_WithANodeSelected_StoresTheWorkflowOnTheControl()
    {
        // Arrange
        var (editor, stored) = Editor();
        editor.SelectNode("n1");

        // Act
        editor.AddAction(Item("Mute"));

        // Assert
        Assert.That(stored[^1], Is.EqualTo(editor.Workflow));
        Assert.That(editor.SelectedIsAction, Is.True);
        Assert.That(editor.Overview, Does.Contain("1 action"));
    }

    [Test]
    public void InsertIntoLink_ALooseNode_SplitsTheLinkAroundIt()
    {
        // Arrange
        var (editor, _) = Editor();
        var (first, last, link, loose) = TwoWaitsAndALooseOne(editor);

        var couldInsert = editor.CanInsertIntoLink(loose.Id);

        // Act
        var problem = editor.InsertIntoLink(loose.Id, link);

        // Assert
        Assert.That(couldInsert, Is.True);
        Assert.That(problem, Is.Null);
        Assert.That(editor.Workflow.Links, Is.EquivalentTo(new[]
        {
            new WorkflowLink(first.Id, WorkflowPorts.Next, loose.Id),
            new WorkflowLink(loose.Id, WorkflowPorts.Next, last.Id),
        }));
    }

    [Test]
    public void Undo_AfterInsertingIntoALink_BringsTheOldLinkBackInOneStepAndKeepsTheNode()
    {
        // Arrange
        var (editor, _) = Editor();
        var (_, _, link, loose) = TwoWaitsAndALooseOne(editor);
        editor.InsertIntoLink(loose.Id, link);

        // Act
        editor.UndoCommand.Execute(null);

        // Assert
        Assert.That(Expect.Single(editor.Workflow.Links), Is.EqualTo(link), "one step brings the old link back");
        Assert.That(editor.Workflow.Nodes.Any(node => node.Id == loose.Id), Is.True, "the node itself stays");
    }

    [Test]
    public void InsertIntoLink_WithAPosition_MovesTheNodeThere()
    {
        // Arrange
        var (editor, _) = Editor();
        var (_, _, link, loose) = TwoWaitsAndALooseOne(editor);

        // Act
        var problem = editor.InsertIntoLink(loose.Id, link, 440, 60);

        // Assert
        var placed = editor.Workflow.Find(loose.Id)!;
        Assert.That(problem, Is.Null);
        Assert.That((placed.X, placed.Y), Is.EqualTo((440d, 60d)));
    }

    [Test]
    public void Undo_AfterInsertingIntoALinkWithAPosition_PutsTheNodeBackWhereItWas()
    {
        // Arrange
        var (editor, _) = Editor();
        var (_, _, link, loose) = TwoWaitsAndALooseOne(editor);
        editor.InsertIntoLink(loose.Id, link, 440, 60);

        // Act
        editor.UndoCommand.Execute(null);

        // Assert
        var back = editor.Workflow.Find(loose.Id)!;
        Assert.That((back.X, back.Y), Is.EqualTo((450d, 200d)), "the same step puts it back where it was");
        Assert.That(Expect.Single(editor.Workflow.Links), Is.EqualTo(link));
    }

    private static (WorkflowEditorViewModel Editor, WorkflowNode First, WorkflowNode Middle, WorkflowNode Last) ChainOfThree()
    {
        var (editor, _) = Editor();
        var first = editor.AddNode(Wait, 300, 40);
        var middle = editor.AddNode(Wait, 450, 40);
        var last = editor.AddNode(Wait, 600, 40);
        editor.Connect(first.Id, WorkflowPorts.Next, middle.Id);
        editor.Connect(middle.Id, WorkflowPorts.Next, last.Id);
        editor.SelectNode(middle.Id);
        return (editor, first, middle, last);
    }

    [Test]
    public void DisconnectSelection_TheMiddleNodeOfAChain_RemovesItsLinksButKeepsTheNodes()
    {
        // Arrange
        var (editor, first, middle, last) = ChainOfThree();

        // Act
        editor.DisconnectSelection();

        // Assert
        Assert.That(editor.Workflow.Links, Is.Empty);
        Assert.That(editor.Workflow.Nodes.Select(static node => node.Id), Does.Contain(middle.Id).And.Contain(first.Id).And.Contain(last.Id));
        Assert.That(editor.SelectedHasLinks, Is.False);
    }

    [Test]
    public void Undo_AfterDisconnectingANode_BringsBothLinksBack()
    {
        // Arrange
        var (editor, _, _, _) = ChainOfThree();
        editor.DisconnectSelection();

        // Act
        editor.UndoCommand.Execute(null);

        // Assert
        Assert.That(editor.Workflow.Links, Has.Count.EqualTo(2));
    }

    // ---------------------------------------------------------------- the action list

    [Test]
    public void ActionGroups_Initially_AreGroupedByPluginAndCollapsed()
    {
        // Act
        var (editor, _) = Editor();

        // Assert
        Assert.That(editor.ActionGroups, Is.Not.Empty);
        Assert.That(editor.ActionGroups.Select(static group => group.PluginId), Is.Unique);
        Assert.That(editor.ActionGroups.Sum(static group => group.Actions.Count), Is.EqualTo(editor.AvailableActions.Count));
        Assert.That(editor.ActionGroups.Any(static group => group.IsExpanded), Is.False, "collapsed like the main window until opened");
    }

    [Test]
    public void ActionSearch_AnActionsName_OpensTheMatchingGroups()
    {
        // Arrange
        var (editor, _) = Editor();
        var name = editor.AvailableActions[0].Name;

        // Act
        editor.ActionSearch = name;

        // Assert
        Assert.That(editor.ActionGroups, Is.Not.Empty);
        Assert.That(editor.ActionGroups.All(static group => group.IsExpanded), Is.True);
        Assert.That(editor.ActionGroups.SelectMany(static group => group.Actions).Any(action => action.Name == name), Is.True);
    }

    [Test]
    public void ActionSearch_NoActionHasThatName_ListsNoGroups()
    {
        // Arrange
        var (editor, _) = Editor();

        // Act
        editor.ActionSearch = "no action is called this";

        // Assert
        Assert.That(editor.ActionGroups, Is.Empty);
    }

    [Test]
    public void AddAction_AfterAnAction_LinksFromNextNeverFromTheErrorOutput()
    {
        // Arrange
        var (editor, _) = Editor();
        var first = Expect.IsType<WorkflowNode>(editor.AddAction(Item("Play / Pause")));
        editor.SelectNode(first.Id);
        editor.AddAction(Item("Mute"));
        editor.SelectNode(first.Id);

        // Act
        editor.AddAction(Item("Play / Pause"));

        // Assert
        Assert.That(editor.Workflow.LinksFrom(first.Id, WorkflowPorts.Error), Is.Empty, "the error output is linked by hand only");
        Assert.That(editor.Workflow.LinksFrom(first.Id, WorkflowPorts.Next).Count(), Is.EqualTo(1));
    }

    [Test]
    public void FailureOptions_AnActionNodeWithTries_KeepsThemOnTheNodeAndTheActionUntouched()
    {
        // Arrange
        var (editor, stored) = Editor();
        var node = Expect.IsType<WorkflowNode>(editor.AddAction(Item("Play / Pause")));
        editor.SelectNode(node.Id);
        var retries = Expect.IsType<NumberSettingField>(editor.FailureOptions!.Fields.First());

        // Act
        retries.Value = "3";

        // Assert
        Assert.That(editor.HasFailureOptions, Is.True);
        Assert.That(editor.Workflow.Find(node.Id)!.EffectiveSettings.GetInt(WorkflowKeys.Retries, 0), Is.EqualTo(3));
        Assert.That(stored.Last().Find(node.Id)!.EffectiveSettings.GetInt(WorkflowKeys.Retries, 0), Is.EqualTo(3));
        Assert.That(editor.Workflow.Find(node.Id)!.Action, Is.EqualTo(node.Action), "the action's own settings are untouched");
    }

    [Test]
    public void HasFailureOptions_AWaitNode_IsFalseBecauseItIsOnlyForActionNodes()
    {
        // Arrange
        var (editor, _) = Editor();
        var wait = editor.AddNode(Wait, 0, 0);

        // Act
        editor.SelectNode(wait.Id);

        // Assert
        Assert.That(editor.HasFailureOptions, Is.False);
    }

    // ---------------------------------------------------------------- making room

    private static (WorkflowEditorViewModel Editor, WorkflowNode First, WorkflowNode Middle, WorkflowNode Last, WorkflowNode Elsewhere, WorkflowNode Loose) CrowdedChain()
    {
        var (editor, _) = Editor();
        var first = editor.AddNode(Wait, 300, 40);
        var middle = editor.AddNode(Wait, 360, 40);
        var last = editor.AddNode(Wait, 420, 40);
        var elsewhere = editor.AddNode(Wait, 420, 300);
        editor.Connect(first.Id, WorkflowPorts.Next, middle.Id);
        editor.Connect(middle.Id, WorkflowPorts.Next, last.Id);
        var loose = editor.AddNode(Wait, 300, 200);
        return (editor, first, middle, last, elsewhere, loose);
    }

    [Test]
    public void InsertIntoLink_ACrowdedChain_MovesWhatFollowsClearOfTheInsertedNode()
    {
        // Arrange
        var (editor, first, middle, last, elsewhere, loose) = CrowdedChain();
        var link = new WorkflowLink(first.Id, WorkflowPorts.Next, middle.Id);

        // Act
        var problem = editor.InsertIntoLink(loose.Id, link, 300, 40);

        // Assert
        double X(string id) => editor.Workflow.Find(id)!.X;
        Assert.That(problem, Is.Null);
        Assert.That(X(middle.Id), Is.GreaterThanOrEqualTo(X(loose.Id) + WorkflowEditorViewModel.NodeWidth + WorkflowSpacing.Gap), "the target moved clear of the inserted node");
        Assert.That(X(last.Id) - X(middle.Id), Is.EqualTo(RoomGap), "what follows moved the same distance");
    }

    [Test]
    public void InsertIntoLink_ACrowdedChain_LeavesWhatComesBeforeAndUnrelatedBranchesWhereTheyAre()
    {
        // Arrange
        var (editor, first, middle, _, elsewhere, loose) = CrowdedChain();
        var link = new WorkflowLink(first.Id, WorkflowPorts.Next, middle.Id);

        // Act
        editor.InsertIntoLink(loose.Id, link, 300, 40);

        // Assert
        Assert.That(editor.Workflow.Find(first.Id)!.X, Is.EqualTo(300), "what comes before stays");
        Assert.That(editor.Workflow.Find(elsewhere.Id)!.X, Is.EqualTo(420), "an unrelated branch stays");
    }

    [Test]
    public void Undo_AfterMakingRoom_PutsEverythingBackInOneStep()
    {
        // Arrange
        var (editor, first, middle, last, _, loose) = CrowdedChain();
        editor.InsertIntoLink(loose.Id, new WorkflowLink(first.Id, WorkflowPorts.Next, middle.Id), 300, 40);

        // Act
        editor.UndoCommand.Execute(null);

        // Assert
        double X(string id) => editor.Workflow.Find(id)!.X;
        Assert.That((X(middle.Id), X(last.Id)), Is.EqualTo((360d, 420d)), "one Undo puts everything back");
        Assert.That(editor.Workflow.Links, Has.Count.EqualTo(2));
    }

    [Test]
    public void AddNode_DroppedOntoALinkWithTooLittleRoom_PushesTheNextNodeRightOfIt()
    {
        // Arrange
        var (editor, _) = Editor();
        var first = editor.AddNode(Wait, 100, 40);
        var last = editor.AddNode(Wait, 160, 40);
        editor.Connect(first.Id, WorkflowPorts.Next, last.Id);
        var link = Expect.Single(editor.Workflow.Links);

        // Act
        var added = editor.AddNode(Wait, 100, 40, link);

        // Assert
        Assert.That(editor.Workflow.Find(last.Id)!.X, Is.GreaterThanOrEqualTo(added.X + WorkflowEditorViewModel.NodeWidth + WorkflowSpacing.Gap));
    }

    [Test]
    public void AddAction_DroppedOntoASecondLink_NeverMovesTheRestOfThePathLeft()
    {
        // Arrange
        var (editor, _) = Editor();
        var first = editor.AddNode(Wait, 100, 40);
        var last = editor.AddNode(Wait, 160, 40);
        editor.Connect(first.Id, WorkflowPorts.Next, last.Id);
        editor.AddNode(Wait, 100, 40, Expect.Single(editor.Workflow.Links));
        var second = editor.Workflow.Links.First(candidate => candidate.To == last.Id);
        var before = editor.Workflow.Find(last.Id)!.X;

        // Act
        var action = editor.AddAction(Item("Mute"), editor.Workflow.Find(second.From)!.X, 200, second);

        // Assert
        Assert.That(action, Is.Not.Null);
        Assert.That(editor.Workflow.Find(last.Id)!.X, Is.GreaterThanOrEqualTo(before), "never moves left");
    }

    [Test]
    public void MakeRoom_WhenThereIsRoomAlready_ChangesNothing()
    {
        // Arrange
        var roomy = new Workflow
        {
            Nodes = [new WorkflowNode("a", Wait, 0, 0), new WorkflowNode("b", Wait, 1000, 0), new WorkflowNode("n", Wait, 300, 0)],
            Links = [new("a", WorkflowPorts.Next, "n"), new("n", WorkflowPorts.Next, "b")],
        };

        // Act
        var result = WorkflowSpacing.MakeRoom(roomy, "n", "b", WorkflowEditorViewModel.NodeWidth);

        // Assert
        Assert.That(result, Is.SameAs(roomy));
    }

    // ---------------------------------------------------------------- shared workflows

    private static Workflow SharedOf(string id, string name) => new() { Id = id, Name = name, Nodes = [new WorkflowNode("t", WorkflowNodeKinds.Trigger)] };

    private static (WorkflowEditorViewModel Editor, WorkflowNode Call) EditorWithACallNode(Action<string>? onOpen = null)
    {
        var (editor, _) = Editor();
        editor.SharedSource = () => [SharedOf("wf.1", "Streaming on"), SharedOf("wf.2", "Streaming off")];
        editor.OpenShared = id => onOpen?.Invoke(id);
        var call = editor.AddNode(WorkflowNodeKinds.Call, 300, 40);
        editor.SelectNode(call.Id);
        return (editor, call);
    }

    [Test]
    public void NodeSettings_ACallNode_OffersTheSharedWorkflowsByName()
    {
        // Act
        var (editor, _) = EditorWithACallNode();

        // Assert
        var choice = Expect.IsType<ChoiceSettingField>(editor.NodeSettings!.Fields.Single());
        Assert.That(choice.Options.Select(static option => option.Label), Is.EqualTo(new[] { "Streaming on", "Streaming off" }));
    }

    [Test]
    public void SelectedOption_ChoosingASharedWorkflow_StoresItsIdOnTheCallNode()
    {
        // Arrange
        var (editor, call) = EditorWithACallNode();
        var choice = Expect.IsType<ChoiceSettingField>(editor.NodeSettings!.Fields.Single());

        // Act
        choice.SelectedOption = choice.Options[1];

        // Assert
        Assert.That(editor.Workflow.Find(call.Id)!.EffectiveSettings.Get(WorkflowKeys.Workflow), Is.EqualTo("wf.2"));
        Assert.That(editor.SelectedIsCall, Is.True);
    }

    [Test]
    public void OpenCall_ACallNodeThatNamesAWorkflow_OpensThatWorkflow()
    {
        // Arrange
        string? opened = null;
        var (editor, _) = EditorWithACallNode(id => opened = id);
        var choice = Expect.IsType<ChoiceSettingField>(editor.NodeSettings!.Fields.Single());
        choice.SelectedOption = choice.Options[1];

        // Act
        editor.OpenCallCommand.Execute(null);

        // Assert
        Assert.That(opened, Is.EqualTo("wf.2"));
    }

    private static (WorkflowEditorViewModel Editor, Func<Workflow?> Saved) EditorWithASharedStore()
    {
        var (editor, _) = Editor();
        Workflow? saved = null;
        editor.SharedSource = () => saved is null ? [] : [saved];
        editor.SaveShared = workflow =>
        {
            if (string.IsNullOrWhiteSpace(workflow.Name))
            {
                return (null, "name it first");
            }

            saved = workflow with { Id = "wf.new" };
            return ("wf.new", null);
        };
        editor.RefreshShared();
        return (editor, () => saved);
    }

    [Test]
    public void SaveAsShared_WithoutAName_SaysToNameItFirstAndSavesNothing()
    {
        // Arrange
        var (editor, saved) = EditorWithASharedStore();

        // Act
        editor.SaveAsSharedCommand.Execute(null);

        // Assert
        Assert.That(editor.CanSaveAsShared, Is.True);
        Assert.That(editor.Status, Is.EqualTo("name it first"));
        Assert.That(saved(), Is.Null);
    }

    [Test]
    public void SaveAsShared_WithAName_SavesItAndListsIt()
    {
        // Arrange
        var (editor, saved) = EditorWithASharedStore();
        editor.WorkflowName = "Mute everything";

        // Act
        editor.SaveAsSharedCommand.Execute(null);

        // Assert
        Assert.That(saved()?.Name, Is.EqualTo("Mute everything"));
        Assert.That(editor.SharedList.Select(static row => row.Id), Is.EqualTo(new[] { "wf.new" }));
    }

    [Test]
    public void CanSaveAsShared_ASharedEditor_IsFalse()
    {
        // Arrange
        var (editor, _) = EditorWithASharedStore();

        // Act
        using var sharedEditor = new WorkflowEditorViewModel("device", "layout", Key, "Key 1", SharedOf("wf.new", "Mute everything"), Actions, _ => true) { SaveShared = editor.SaveShared };

        // Assert
        Assert.That(sharedEditor.IsShared, Is.True);
        Assert.That(sharedEditor.CanSaveAsShared, Is.False);
    }

    [Test]
    public void DeleteSharedRow_AWorkflowStillInUse_ShowsWhyNotAndKeepsIt()
    {
        // Arrange
        var (editor, _) = Editor();
        editor.SharedSource = () => [SharedOf("wf.1", "In use")];
        editor.CallersOf = _ => ["Studio: key.3"];
        editor.DeleteShared = _ => "It is still called by Studio: key.3.";
        editor.RefreshShared();
        var usedBy = editor.SharedList.Single().UsedBy;

        // Act
        editor.DeleteSharedRowCommand.Execute(editor.SharedList.Single());

        // Assert
        Assert.That(usedBy, Is.EqualTo("used by Studio: key.3"));
        Assert.That(editor.Status, Does.Contain("Can't delete In use").And.Contain("still called"));
        Assert.That(editor.SharedList, Has.Count.EqualTo(1));
    }

    // ---------------------------------------------------------------- breakpoints and traces

    private static DeckHost.Core.Models.WorkflowTrace TraceOf(string nodeId, DeckHost.Core.Models.WorkflowTraceKind kind, int run = 7, string? shared = null, string detail = "Paused at a breakpoint") =>
        new(DeviceName, "layout", KeyControl, run, nodeId, kind, detail, DateTimeOffset.Now) { WorkflowId = shared };

    [Test]
    public void ToggleBreakpoint_ANode_SetsAndRemovesItsBreakpoint()
    {
        // Arrange
        var (editor, _) = Editor();
        var wait = editor.AddNode(Wait, 300, 40);

        // Act
        editor.ToggleBreakpoint(wait.Id);
        var setOn = editor.HasBreakpoint(wait);
        editor.ToggleBreakpoint(wait.Id);

        // Assert
        Assert.That(setOn, Is.True);
        Assert.That(editor.HasBreakpoint(wait), Is.False);
    }

    [Test]
    public void Breakpoints_AToggledNode_IsListedForTheControl()
    {
        // Arrange
        var (editor, _) = Editor();
        var wait = editor.AddNode(Wait, 300, 40);

        // Act
        editor.ToggleBreakpoint(wait.Id);

        // Assert
        Assert.That(editor.Breakpoints.Of(DeviceName, KeyControl), Is.EqualTo(new[] { wait.Id }));
    }

    [Test]
    public void ShowTrace_ARunPausedAtANode_IsFollowedUntilItGoesOn()
    {
        // Arrange
        var (editor, _) = Editor();
        var wait = editor.AddNode(Wait, 300, 40);

        // Act
        editor.ShowTrace(TraceOf(wait.Id, DeckHost.Core.Models.WorkflowTraceKind.Paused));

        // Assert
        Assert.That(editor.IsPaused, Is.True);
        Assert.That(editor.IsPausedAt(wait), Is.True);
        Assert.That(editor.ContinueCommand.CanExecute(null), Is.True);
    }

    [Test]
    public void ShowTrace_AnotherRunEnding_DoesNotReleaseThePausedRun()
    {
        // Arrange
        var (editor, _) = Editor();
        var wait = editor.AddNode(Wait, 300, 40);
        editor.ShowTrace(TraceOf(wait.Id, DeckHost.Core.Models.WorkflowTraceKind.Paused));

        // Act
        editor.ShowTrace(TraceOf(wait.Id, DeckHost.Core.Models.WorkflowTraceKind.Finished, run: 8));

        // Assert
        Assert.That(editor.IsPaused, Is.True, "another run ending doesn't release it");
    }

    [Test]
    public void ShowTrace_ThePausedRunResuming_ReleasesIt()
    {
        // Arrange
        var (editor, _) = Editor();
        var wait = editor.AddNode(Wait, 300, 40);
        editor.ShowTrace(TraceOf(wait.Id, DeckHost.Core.Models.WorkflowTraceKind.Paused));

        // Act
        editor.ShowTrace(TraceOf(wait.Id, DeckHost.Core.Models.WorkflowTraceKind.Resumed));

        // Assert
        Assert.That(editor.IsPaused, Is.False);
        Assert.That(editor.ContinueCommand.CanExecute(null), Is.False);
    }

    [Test]
    public void ShowTrace_AStepInASharedWorkflow_DoesNotLightThisWorkflowsNodeWithTheSameId()
    {
        // Arrange
        var (editor, _) = Editor();
        var other = editor.AddNode(Wait, 600, 40);

        // Act
        editor.ShowTrace(TraceOf(other.Id, DeckHost.Core.Models.WorkflowTraceKind.Node, run: 9, shared: "wf.1", detail: "Waiting"));

        // Assert
        Assert.That(editor.IsActive(other), Is.False);
        Assert.That(editor.RunLog[0], Does.Contain("↳"));
    }

    [TestCase("5", "number")]
    [TestCase("true", "true / false")]
    [TestCase("hello", "text")]
    [TestCase("", "empty")]
    public void The_variable_view_says_what_kind_of_value_each_variable_holds(string value, string kind) =>
        Assert.That(new VariableRow("x", value).Kind, Is.EqualTo(kind));

    [Test]
    public void The_new_set_variable_and_if_settings_show_only_where_they_apply()
    {
        var (editor, _) = Editor();
        var set = editor.AddNode(WorkflowNodeKinds.SetVariable, 300, 40);
        editor.SelectNode(set.Id);
        var fields = editor.NodeSettings!.Fields.ToDictionary(static field => field.Definition.Key);

        Assert.That(fields[WorkflowKeys.Expression].IsVisible, Is.False, "Set to is the default operation");
        Assert.That(fields[WorkflowKeys.Value].IsVisible, Is.True);
        Assert.That(fields[WorkflowKeys.As].IsVisible, Is.True);

        var operation = Expect.IsType<ChoiceSettingField>(fields[WorkflowKeys.Operation]);
        operation.SelectedOption = operation.Options.Single(static option => option.Value == WorkflowKeys.Calculate);
        Assert.That(fields[WorkflowKeys.Expression].IsVisible, Is.True);
        Assert.That(fields[WorkflowKeys.Value].IsVisible, Is.False);

        var check = editor.AddNode(WorkflowNodeKinds.If, 600, 40);
        editor.SelectNode(check.Id);
        var ifFields = editor.NodeSettings!.Fields.ToDictionary(static field => field.Definition.Key);
        Assert.That(ifFields[WorkflowKeys.Expression].IsVisible, Is.False);
        var kind = Expect.IsType<ChoiceSettingField>(ifFields[WorkflowKeys.Check]);
        kind.SelectedOption = kind.Options.Single(static option => option.Value == WorkflowKeys.CheckExpression);
        Assert.That(ifFields[WorkflowKeys.Expression].IsVisible, Is.True);
        Assert.That(ifFields[WorkflowKeys.Name].IsVisible, Is.False);
    }

    private static WorkflowEditorViewModel SwitchEditor(out WorkflowNode sw, out WorkflowNode first, out WorkflowNode second, out WorkflowNode other)
    {
        var (editor, _) = Editor();
        sw = editor.AddNode(WorkflowNodeKinds.Switch, 300, 40);
        first = editor.AddNode(WorkflowNodeKinds.Wait, 700, 0);
        second = editor.AddNode(WorkflowNodeKinds.Wait, 700, 100);
        other = editor.AddNode(WorkflowNodeKinds.Wait, 700, 200);
        Assert.That(editor.Connect(sw.Id, "case:c1", first.Id), Is.Null);
        Assert.That(editor.Connect(sw.Id, "case:c2", second.Id), Is.Null);
        Assert.That(editor.Connect(sw.Id, WorkflowPorts.Default, other.Id), Is.Null);
        editor.SelectNode(sw.Id);
        return editor;
    }

    [Test]
    public void A_new_switch_has_two_cases_and_else_and_grows_taller_with_its_ports()
    {
        var (editor, _) = Editor();
        var sw = editor.AddNode(WorkflowNodeKinds.Switch, 300, 40);
        var wait = editor.AddNode(WorkflowNodeKinds.Wait, 600, 40);

        Assert.That(WorkflowNodeKinds.OutputsOf(editor.Workflow.Find(sw.Id)!), Is.EqualTo(new[] { "case:c1", "case:c2", WorkflowPorts.Default }));
        Assert.That(WorkflowEditorViewModel.HeightOf(editor.Workflow.Find(sw.Id)!), Is.GreaterThan(WorkflowEditorViewModel.HeightOf(wait)));
        var (_, y1) = WorkflowEditorViewModel.OutputPoint(editor.Workflow.Find(sw.Id)!, "case:c1");
        var (_, y3) = WorkflowEditorViewModel.OutputPoint(editor.Workflow.Find(sw.Id)!, WorkflowPorts.Default);
        Assert.That(y3 - y1, Is.EqualTo(2 * WorkflowEditorViewModel.PortRowHeight), "one row per output");
        Assert.That(editor.Connect(sw.Id, "case:c9", wait.Id), Is.EqualTo("That node has no such output."));
    }

    [Test]
    public void Cases_can_be_added_moved_renamed_and_removed_and_links_follow_their_case_not_its_place()
    {
        var editor = SwitchEditor(out var sw, out var first, out var second, out var other);
        editor.AddCaseCommand.Execute(null);
        Assert.That(editor.Cases.Select(static row => row.Id), Is.EqualTo(new[] { "c1", "c2", "c3" }));

        editor.MoveCase(editor.Cases[0], 1);
        Assert.That(editor.Cases.Select(static row => row.Id), Is.EqualTo(new[] { "c2", "c1", "c3" }));
        Assert.That(editor.Workflow.Links.Single(link => link.To == first.Id).Port, Is.EqualTo("case:c1"), "its link stayed with the case");

        editor.Cases.Single(static row => row.Id == "c2").Text = "night";
        Assert.That(CasesText(editor, sw), Does.Contain("c2=night"));
        Assert.That(editor.Workflow.Links.Single(link => link.To == second.Id).Port, Is.EqualTo("case:c2"), "renaming doesn't move links");

        editor.RemoveCase(editor.Cases.Single(static row => row.Id == "c1"));
        Assert.That(editor.Cases.Select(static row => row.Id), Is.EqualTo(new[] { "c2", "c3" }));
        Assert.That(editor.Workflow.Links.Any(link => link.To == first.Id), Is.False, "only the removed case's link is gone");
        Assert.That(editor.Workflow.Links.Any(link => link.To == second.Id), Is.True);
        Assert.That(editor.Workflow.Links.Any(link => link.To == other.Id && link.Port == WorkflowPorts.Default), Is.True);

        editor.UndoCommand.Execute(null);
        Assert.That(editor.Cases.Select(static row => row.Id), Is.EqualTo(new[] { "c2", "c1", "c3" }), "one Undo brings the case and its link back");
        Assert.That(editor.Workflow.Links.Any(link => link.To == first.Id && link.Port == "case:c1"), Is.True);
    }

    private static string CasesText(WorkflowEditorViewModel editor, WorkflowNode node) =>
        editor.Workflow.Find(node.Id)!.EffectiveSettings.Get(WorkflowKeys.Cases);

    [Test]
    public void Editing_the_switch_expression_keeps_its_cases_and_a_switch_put_into_a_link_continues_at_else()
    {
        var editor = SwitchEditor(out var sw, out _, out _, out _);
        var casesBefore = CasesText(editor, sw);
        var field = Expect.IsType<TextSettingField>(editor.NodeSettings!.Fields.Single());

        field.Value = "[mode]";

        Assert.That(editor.Workflow.Find(sw.Id)!.EffectiveSettings.Get(WorkflowKeys.Expression), Is.EqualTo("[mode]"));
        Assert.That(CasesText(editor, sw), Is.EqualTo(casesBefore), "the cases are not written over");

        var (second, _) = Editor();
        var a = second.AddNode(WorkflowNodeKinds.Wait, 100, 40);
        var b = second.AddNode(WorkflowNodeKinds.Wait, 600, 40);
        second.Connect(a.Id, WorkflowPorts.Next, b.Id);
        var link = Expect.Single(second.Workflow.Links);
        var inserted = second.AddNode(WorkflowNodeKinds.Switch, 300, 40, link);
        Assert.That(second.Workflow.Links.Any(candidate => candidate.From == inserted.Id && candidate.Port == WorkflowPorts.Default && candidate.To == b.Id), Is.True);
    }

    [Test]
    public void A_file_keeps_links_on_existing_case_ports_and_refuses_too_many_cases()
    {
        var editor = SwitchEditor(out var sw, out _, out _, out _);
        var text = editor.ExportText();
        var (other, _) = Editor();
        Assert.That(other.Import(text), Is.Null);
        Assert.That(other.Workflow.Links.Count(link => link.Port.StartsWith("case:", StringComparison.Ordinal)), Is.EqualTo(2));

        var removed = text.Replace("c1=1\\nc2=2", "c2=2", StringComparison.Ordinal);
        Assert.That(removed, Is.Not.EqualTo(text), "the test text was cut");
        var (third, _) = Editor();
        Assert.That(third.Import(removed), Is.Null);
        Assert.That(third.Workflow.Links.Any(link => link.Port == "case:c1"), Is.False, "a link on a port the node no longer has is dropped");

        var many = string.Join("\\n", Enumerable.Range(1, WorkflowCases.Max + 1).Select(static i => $"c{i}=x"));
        var tooMany = text.Replace("c1=1\\nc2=2", many, StringComparison.Ordinal);
        var (fourth, _) = Editor();
        Assert.That(fourth.Import(tooMany), Does.Contain("more than"));
        _ = sw;
    }

    [Test]
    public void A_node_with_links_or_a_node_without_a_way_through_cannot_be_put_into_a_link()
    {
        var (editor, _) = Editor();
        var a = editor.AddNode(WorkflowNodeKinds.Wait, 300, 40);
        var b = editor.AddNode(WorkflowNodeKinds.Wait, 600, 40);
        editor.Connect(a.Id, WorkflowPorts.Next, b.Id);
        var link = Expect.Single(editor.Workflow.Links);
        var stop = editor.AddNode(WorkflowNodeKinds.Stop, 450, 200);
        var linked = editor.AddNode(WorkflowNodeKinds.Wait, 450, 300);
        var other = editor.AddNode(WorkflowNodeKinds.Wait, 750, 300);
        editor.Connect(linked.Id, WorkflowPorts.Next, other.Id);

        Assert.That(editor.InsertIntoLink(stop.Id, link), Does.Contain("Stop"));
        Assert.That(editor.InsertIntoLink(linked.Id, link), Does.Contain("isn't linked"));
        Assert.That(editor.InsertIntoLink("n1", link), Does.Contain("When"));
        Assert.That(editor.InsertIntoLink(a.Id, link), Is.Not.Null, "a node already on the link");
        Assert.That(editor.Workflow.Links.Contains(link), Is.True);
    }

    [Test]
    public void A_new_node_or_action_dropped_onto_a_link_is_added_into_it_as_one_step()
    {
        var (editor, _) = Editor();
        var a = editor.AddNode(WorkflowNodeKinds.Wait, 300, 40);
        var b = editor.AddNode(WorkflowNodeKinds.Wait, 600, 40);
        editor.Connect(a.Id, WorkflowPorts.Next, b.Id);
        var link = Expect.Single(editor.Workflow.Links);

        var repeat = editor.AddNode(WorkflowNodeKinds.Repeat, 450, 200, link);

        Assert.That(editor.Workflow.Links, Is.EquivalentTo(new[]
        {
            new WorkflowLink(a.Id, WorkflowPorts.Next, repeat.Id),
            new WorkflowLink(repeat.Id, WorkflowPorts.Done, b.Id), // a Repeat carries on from Done; Each is its body
        }));
        editor.UndoCommand.Execute(null);
        Assert.That(Expect.Single(editor.Workflow.Links), Is.EqualTo(link));
        Assert.That(editor.Workflow.Nodes.Any(node => node.Id == repeat.Id), Is.False, "added and linked in one step");

        var mute = editor.AddAction(Item("Mute"), 450, 200, link)!;
        Assert.That(editor.Workflow.Links.Select(static l => (l.From, l.To)), Is.EquivalentTo(new[] { (a.Id, mute.Id), (mute.Id, b.Id) }));
    }

    [Test]
    public void The_workflow_can_be_named_and_the_name_goes_to_the_control_and_the_title()
    {
        var (editor, stored) = Editor();
        Assert.That(editor.Title, Is.EqualTo("Workflow · Key 1"));

        editor.WorkflowName = "  Mute the mic  ";

        Assert.That(editor.WorkflowName, Is.EqualTo("Mute the mic"));
        Assert.That(stored[^1].Name, Is.EqualTo("Mute the mic"));
        Assert.That(editor.Title, Is.EqualTo("Mute the mic · Key 1"));
        editor.WorkflowName = string.Empty;
        Assert.That(stored[^1].Name, Is.Null);
    }

    [Test]
    public void A_workflow_exports_to_a_file_and_comes_back_through_import_with_one_undo_step()
    {
        var (editor, _) = Editor();
        editor.WorkflowName = "Scene switch";
        var wait = editor.AddNode(WorkflowNodeKinds.Wait, 300, 40);
        editor.Connect("n1", WorkflowPorts.Next, wait.Id);
        editor.AddAction(Item("Mute"), 600, 40);
        var text = editor.ExportText();
        Assert.That(editor.ExportFileName, Is.EqualTo("Scene switch.deckhostworkflow"));

        var (other, stored) = Editor();
        Assert.That(other.Import(text), Is.Null);

        Assert.That(other.Workflow, Is.EqualTo(editor.Workflow));
        Assert.That(stored[^1], Is.EqualTo(editor.Workflow));
        Assert.That(other.WorkflowName, Is.EqualTo("Scene switch"));
        other.UndoCommand.Execute(null);
        Assert.That(other.Workflow.Name, Is.Null, "the old workflow is back");
    }

    [TestCase("")]
    [TestCase("not json")]
    [TestCase("[1,2]")]
    [TestCase("""{ "format": "something-else", "workflow": { "nodes": [] } }""")]
    [TestCase("""{ "format": "deckhost-workflow", "version": 99, "workflow": { "nodes": [] } }""")]
    [TestCase("""{ "nodes": [ { "id": "a", "kind": "teleport" } ] }""")]
    [TestCase("""{ "nodes": [ { "id": "a", "kind": "wait" }, { "id": "a", "kind": "wait" } ] }""")]
    public void A_file_that_is_not_a_usable_workflow_is_refused_and_changes_nothing(string text)
    {
        var (editor, stored) = Editor();
        var before = editor.Workflow;
        var count = stored.Count;

        Assert.That(editor.Import(text), Is.Not.Null.And.Not.Empty);

        Assert.That(editor.Workflow, Is.EqualTo(before));
        Assert.That(stored.Count, Is.EqualTo(count));
    }

    [Test]
    public void Importing_drops_links_that_lead_nowhere_or_close_a_loop()
    {
        var (editor, _) = Editor();
        const string text = """
            { "nodes": [ { "id": "t", "kind": "trigger" }, { "id": "a", "kind": "wait" }, { "id": "b", "kind": "wait" } ],
              "links": [ { "from": "t", "port": "next", "to": "a" }, { "from": "a", "port": "next", "to": "b" },
                         { "from": "b", "port": "next", "to": "a" },
                         { "from": "a", "port": "next", "to": "gone" },
                         { "from": "a", "port": "yes", "to": "b" },
                         { "from": "a", "port": "next", "to": "t" } ] }
            """;

        Assert.That(editor.Import(text), Is.Null);

        Assert.That(editor.Workflow.Links.Select(static link => $"{link.From}>{link.To}"), Is.EqualTo(new[] { "t>a", "a>b" }));
    }

    [Test]
    public void A_wait_fills_its_node_over_the_time_it_takes()
    {
        using var editor = new WorkflowEditorViewModel("device", "layout", Key, "Key 1", WorkflowConverter.Starter(), Actions, _ => true);
        var wait = editor.AddNode(WorkflowNodeKinds.Wait, 300, 40);
        Assert.That(editor.WaitProgress(wait), Is.Null);

        editor.ShowTrace(new DeckHost.Core.Models.WorkflowTrace("device", "layout", "key.0", 1, wait.Id, DeckHost.Core.Models.WorkflowTraceKind.Node, "Waiting 1 s", DateTimeOffset.Now) { Wait = TimeSpan.FromSeconds(60) });

        var progress = editor.WaitProgress(wait);
        Assert.That(progress, Is.Not.Null.And.InRange(0.0, 0.1));
        Assert.That(editor.IsActive(wait), Is.True);
        // Nothing waits at a node that only ran.
        var other = editor.AddNode(WorkflowNodeKinds.Wait, 600, 40);
        editor.ShowTrace(new DeckHost.Core.Models.WorkflowTrace("device", "layout", "key.0", 1, other.Id, DeckHost.Core.Models.WorkflowTraceKind.Node, "Waiting", DateTimeOffset.Now));
        Assert.That(editor.WaitProgress(other), Is.Null);
    }

    [Test]
    public void Links_into_a_trigger_loops_and_duplicates_are_refused()
    {
        var (editor, _) = Editor();
        var a = editor.AddNode(WorkflowNodeKinds.Wait, 300, 40);
        var b = editor.AddNode(WorkflowNodeKinds.Wait, 600, 40);

        Assert.That(editor.Connect("n1", WorkflowPorts.Next, a.Id), Is.Null);
        Assert.That(editor.Connect(a.Id, WorkflowPorts.Next, b.Id), Is.Null);
        Assert.That(editor.Connect(a.Id, WorkflowPorts.Next, b.Id), Is.Not.Null);          // already linked
        Assert.That(editor.Connect(b.Id, WorkflowPorts.Next, a.Id), Is.Not.Null);          // a loop
        Assert.That(editor.Connect(b.Id, WorkflowPorts.Next, "n1"), Is.Not.Null);          // into a When node
        Assert.That(editor.Connect(a.Id, WorkflowPorts.Yes, b.Id), Is.Not.Null);           // a Wait has no Yes
        Assert.That(editor.Workflow.Links.Count, Is.EqualTo(2));
    }

    [Test]
    public void Undo_and_redo_step_through_edits_and_quick_setting_edits_merge()
    {
        var (editor, stored) = Editor();
        var wait = editor.AddNode(WorkflowNodeKinds.Wait, 300, 40);
        editor.SelectNode(wait.Id);
        var field = Expect.IsType<NumberSettingField>(editor.NodeSettings!.Fields.Single());
        field.Value = "700";
        field.Value = "800";   // merges with the edit before

        Assert.That(editor.Workflow.Find(wait.Id)!.EffectiveSettings.Get(WorkflowKeys.Milliseconds), Is.EqualTo("800"));
        editor.UndoCommand.Execute(null);
        Assert.That(editor.Workflow.Find(wait.Id)!.EffectiveSettings.Get(WorkflowKeys.Milliseconds), Is.EqualTo("500"));
        editor.UndoCommand.Execute(null);
        Assert.That(editor.Workflow.Find(wait.Id), Is.Null);
        Assert.That(editor.CanUndo, Is.False);

        editor.RedoCommand.Execute(null);
        editor.RedoCommand.Execute(null);
        Assert.That(editor.Workflow.Find(wait.Id)!.EffectiveSettings.Get(WorkflowKeys.Milliseconds), Is.EqualTo("800"));
        Assert.That(stored[^1], Is.EqualTo(editor.Workflow));
    }

    [Test]
    public void Removing_a_node_takes_its_links_and_revert_brings_back_the_start()
    {
        var start = WorkflowConverter.FromBinding(new ControlBinding("key.0", ProfileActionKind.MultiAction, Item("Multi Action").ToActionReference(), new ControlAppearance(),
            [new MultiActionStep(0, Item("Mute").ToActionReference()), new MultiActionStep(1, Item("Next Track").ToActionReference())]));
        var (editor, _) = Editor(start);
        var mute = editor.Workflow.Nodes.First(node => node.Action?.ActionName == "Mute");

        editor.SelectNode(mute.Id);
        editor.DeleteSelection();

        Assert.That(editor.Workflow.Links.Any(link => link.From == mute.Id || link.To == mute.Id), Is.False);
        editor.RevertAllCommand.Execute(null);
        Assert.That(editor.Workflow, Is.EqualTo(start));
    }

    [Test]
    public void Arrange_puts_nodes_in_columns_by_their_distance_from_the_trigger()
    {
        var (editor, _) = Editor();
        var a = editor.AddNode(WorkflowNodeKinds.Toggle, 900, 400);
        var b = editor.AddNode(WorkflowNodeKinds.Wait, 10, 900);
        editor.Connect("n1", WorkflowPorts.Next, a.Id);
        editor.Connect(a.Id, WorkflowPorts.First, b.Id);

        editor.ArrangeCommand.Execute(null);

        var x = editor.Workflow.Nodes.ToDictionary(node => node.Id, node => node.X);
        Assert.That(x["n1"] < x[a.Id] && x[a.Id] < x[b.Id], Is.True);
    }

    [Test]
    public void A_trigger_only_offers_inputs_the_control_has_and_a_lost_control_is_reported()
    {
        var knobless = new PhysicalControl("touch.0", "Touch 1", ControlType.TouchSurface, new Rect(0, 0, 10, 10), InputCapabilities.Press, OutputCapabilities.ColorImage);
        var editor = new WorkflowEditorViewModel("device", "layout", knobless, "Touch 1", WorkflowConverter.Starter(), Actions, _ => false);
        editor.SelectNode("n1");

        var choice = Expect.IsType<ChoiceSettingField>(editor.NodeSettings!.Fields.First());
        Assert.That(choice.Options.Select(option => option.Value), Is.EqualTo(new[]
        {
            WorkflowTriggers.Down,
            WorkflowTriggers.ProfileShown,
            WorkflowTriggers.AppInFront,
            WorkflowTriggers.VariableChanged,
            WorkflowTriggers.Timer,
        }), "the control's own input, and the events that need no input");

        editor.AddNode(WorkflowNodeKinds.Wait, 0, 0);
        Assert.That(editor.Status, Does.Contain("no longer has this workflow"));
    }

    private const string TheFirstNode = "n1";

    private static (WorkflowEditorViewModel Editor, WorkflowNode Wait, WorkflowNode After) ChainTriggerWaitWait()
    {
        var (editor, _) = Editor();
        var wait = editor.AddNode(WorkflowNodeKinds.Wait, 300, 40);
        var after = editor.AddNode(WorkflowNodeKinds.Wait, 600, 40);
        editor.Connect(TheFirstNode, WorkflowPorts.Next, wait.Id);
        editor.Connect(wait.Id, WorkflowPorts.Next, after.Id);
        return (editor, wait, after);
    }

    [Test]
    public void DeleteSelection_MiddleNodeOfAChain_LinksTheNodesAroundIt()
    {
        // Arrange
        var (editor, wait, after) = ChainTriggerWaitWait();
        editor.SelectNode(wait.Id);

        // Act
        editor.DeleteSelection();

        // Assert
        Assert.That(editor.Workflow.Links, Is.EqualTo(new[] { new WorkflowLink(TheFirstNode, WorkflowPorts.Next, after.Id) }));
    }

    [Test]
    public void DeleteSelectionAndDisconnect_MiddleNodeOfAChain_LeavesTheGap()
    {
        // Arrange
        var (editor, wait, _) = ChainTriggerWaitWait();
        editor.SelectNode(wait.Id);

        // Act
        editor.DeleteSelectionAndDisconnect();

        // Assert
        Assert.That(editor.Workflow.Links, Is.Empty);
    }

    [Test]
    public void DeleteSelection_MiddleNodeOfAChain_UndoesInOneStep()
    {
        // Arrange
        var (editor, wait, _) = ChainTriggerWaitWait();
        editor.SelectNode(wait.Id);
        editor.DeleteSelection();

        // Act
        editor.UndoCommand.Execute(null);

        // Assert
        Assert.That(editor.Workflow.Links, Has.Count.EqualTo(2));
    }

    private static (WorkflowEditorViewModel Editor, WorkflowNode Branching, WorkflowNode Yes, WorkflowNode No) IfWithTwoBranchesAfterTheTrigger()
    {
        var (editor, _) = Editor();
        var branching = editor.AddNode(WorkflowNodeKinds.If, 300, 40);
        var yes = editor.AddNode(WorkflowNodeKinds.Wait, 600, 0);
        var no = editor.AddNode(WorkflowNodeKinds.Wait, 600, 100);
        editor.Connect(TheFirstNode, WorkflowPorts.Next, branching.Id);
        editor.Connect(branching.Id, WorkflowPorts.Yes, yes.Id);
        editor.Connect(branching.Id, WorkflowPorts.No, no.Id);
        return (editor, branching, yes, no);
    }

    [Test]
    public void DeleteSelection_NodeWithSeveralBranches_AsksInsteadOfRemovingIt()
    {
        // Arrange
        var (editor, branching, _, _) = IfWithTwoBranchesAfterTheTrigger();
        editor.SelectNode(branching.Id);

        // Act
        editor.DeleteSelection();

        // Assert
        Assert.That(editor.HasDeleteChoice, Is.True);
        Assert.That(editor.Workflow.Find(branching.Id), Is.Not.Null);
    }

    [Test]
    public void DeleteAndReconnectFirstPath_NodeWithSeveralBranches_ContinuesWithTheFirstBranch()
    {
        // Arrange
        var (editor, branching, yes, _) = IfWithTwoBranchesAfterTheTrigger();
        editor.SelectNode(branching.Id);
        editor.DeleteSelection();

        // Act
        editor.DeleteAndReconnectFirstPath();

        // Assert
        Assert.That(editor.Workflow.Links, Is.EqualTo(new[] { new WorkflowLink(TheFirstNode, WorkflowPorts.Next, yes.Id) }));
    }

    [Test]
    public void CancelDelete_NodeWithSeveralBranches_KeepsTheNodeAndStopsAsking()
    {
        // Arrange
        var (editor, branching, _, _) = IfWithTwoBranchesAfterTheTrigger();
        editor.SelectNode(branching.Id);
        editor.DeleteSelection();

        // Act
        editor.CancelDelete();

        // Assert
        Assert.That(editor.HasDeleteChoice, Is.False);
        Assert.That(editor.Workflow.Find(branching.Id), Is.Not.Null);
    }

    [Test]
    public void Arrange_SwitchStoredInAnotherOrder_StacksTheBranchesInPortOrder()
    {
        // Arrange
        var (editor, _) = Editor();
        var sw = editor.AddNode(WorkflowNodeKinds.Switch, 300, 40);
        var elseTarget = editor.AddNode(WorkflowNodeKinds.Wait, 700, 0);
        var secondTarget = editor.AddNode(WorkflowNodeKinds.Wait, 700, 50);
        var firstTarget = editor.AddNode(WorkflowNodeKinds.Wait, 700, 100);
        editor.Connect(sw.Id, WorkflowPorts.Default, elseTarget.Id);
        editor.Connect(sw.Id, "case:c2", secondTarget.Id);
        editor.Connect(sw.Id, "case:c1", firstTarget.Id);

        // Act
        editor.ArrangeCommand.Execute(null);

        // Assert
        var rows = new[] { firstTarget.Id, secondTarget.Id, elseTarget.Id }.Select(id => editor.Workflow.Find(id)!.Y).ToList();
        Assert.That(rows, Is.Ordered.Ascending);
    }

    [Test]
    public void NewSharedWorkflow_CallNode_CreatesANamedWorkflowAndPointsTheNodeAtIt()
    {
        // Arrange
        var (editor, _) = Editor();
        var shared = new List<Workflow>();
        editor.SharedSource = () => shared;
        editor.SaveShared = workflow =>
        {
            var saved = workflow with { Id = "wf.test" };
            shared.Add(saved);
            return (saved.Id, null);
        };
        var call = editor.AddNode(WorkflowNodeKinds.Call, 300, 40);
        editor.SelectNode(call.Id);

        // Act
        editor.NewSharedWorkflowCommand.Execute(null);

        // Assert
        Assert.That(Expect.Single(shared).Name, Is.EqualTo("Workflow 1"));
        Assert.That(editor.Workflow.Find(call.Id)!.EffectiveSettings.Get(WorkflowKeys.Workflow), Is.EqualTo("wf.test"));
    }

    [Test]
    public void NextSharedName_ExistingWorkflowOne_IsWorkflowTwo()
    {
        // Arrange
        var (editor, _) = Editor();
        editor.SharedSource = () => [new Workflow { Id = "wf.1", Name = "Workflow 1" }];

        // Act
        var name = editor.NextSharedName();

        // Assert
        Assert.That(name, Is.EqualTo("Workflow 2"));
    }

    [Test]
    public void Starter_NewWorkflow_RestartsWhenTriggeredAgain()
    {
        // Arrange (a new workflow)

        // Act
        var workflow = WorkflowConverter.Starter();

        // Assert
        Assert.That(workflow.WhileRunning, Is.EqualTo(WorkflowConcurrency.Restart));
    }

    private static WorkflowTrace WaitTrace(string nodeId, int runId) =>
        new("device", "layout", "key.0", runId, nodeId, WorkflowTraceKind.Node, "Waiting 10 s", DateTimeOffset.Now) { Wait = TimeSpan.FromSeconds(10) };

    [Test]
    public void WaitProgresses_TwoRunsWaitingAtTheSameNode_ShowsEachRun()
    {
        // Arrange
        var (editor, _) = Editor();
        var wait = editor.AddNode(WorkflowNodeKinds.Wait, 300, 40);
        editor.ShowTrace(WaitTrace(wait.Id, 1));
        editor.ShowTrace(WaitTrace(wait.Id, 2));

        // Act
        var progresses = editor.WaitProgresses(wait);

        // Assert
        Assert.That(progresses, Has.Count.EqualTo(2));
    }

    [Test]
    public void WaitProgresses_RunThatWasRestarted_NoLongerWaits()
    {
        // Arrange
        var (editor, _) = Editor();
        var wait = editor.AddNode(WorkflowNodeKinds.Wait, 300, 40);
        editor.ShowTrace(WaitTrace(wait.Id, 1));
        editor.ShowTrace(new WorkflowTrace("device", "layout", "key.0", 1, null, WorkflowTraceKind.Stopped, "Restarted.", DateTimeOffset.Now));

        // Act
        var progresses = editor.WaitProgresses(wait);

        // Assert
        Assert.That(progresses, Is.Empty);
    }
}

using DeckHost.App.SystemPlugins;
using DeckHost.App.ViewModels;
using DeckHost.Contracts.Enums;
using DeckHost.Contracts.Models;
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

    [Fact]
    public void An_action_added_with_a_node_selected_is_linked_after_it_and_stored_on_the_control()
    {
        var (editor, stored) = Editor();
        editor.SelectNode("n1");

        var node = editor.AddAction(Item("Mute"))!;

        Assert.Equal(new WorkflowLink("n1", WorkflowPorts.Next, node.Id), Assert.Single(editor.Workflow.Links));
        Assert.Equal(editor.Workflow, stored[^1]);
        Assert.Equal(node.Id, editor.SelectedNode?.Id);
        Assert.True(editor.SelectedIsAction);
        Assert.Contains("1 action", editor.Overview);
    }

    [Fact]
    public void Links_into_a_trigger_loops_and_duplicates_are_refused()
    {
        var (editor, _) = Editor();
        var a = editor.AddNode(WorkflowNodeKinds.Wait, 300, 40);
        var b = editor.AddNode(WorkflowNodeKinds.Wait, 600, 40);

        Assert.Null(editor.Connect("n1", WorkflowPorts.Next, a.Id));
        Assert.Null(editor.Connect(a.Id, WorkflowPorts.Next, b.Id));
        Assert.NotNull(editor.Connect(a.Id, WorkflowPorts.Next, b.Id));          // already linked
        Assert.NotNull(editor.Connect(b.Id, WorkflowPorts.Next, a.Id));          // a loop
        Assert.NotNull(editor.Connect(b.Id, WorkflowPorts.Next, "n1"));          // into a When node
        Assert.NotNull(editor.Connect(a.Id, WorkflowPorts.Yes, b.Id));           // a Wait has no Yes
        Assert.Equal(2, editor.Workflow.Links.Count);
    }

    [Fact]
    public void Undo_and_redo_step_through_edits_and_quick_setting_edits_merge()
    {
        var (editor, stored) = Editor();
        var wait = editor.AddNode(WorkflowNodeKinds.Wait, 300, 40);
        editor.SelectNode(wait.Id);
        var field = Assert.IsType<NumberSettingField>(editor.NodeSettings!.Fields.Single());
        field.Value = "700";
        field.Value = "800";   // merges with the edit before

        Assert.Equal("800", editor.Workflow.Find(wait.Id)!.EffectiveSettings.Get(WorkflowKeys.Milliseconds));
        editor.UndoCommand.Execute(null);
        Assert.Equal("500", editor.Workflow.Find(wait.Id)!.EffectiveSettings.Get(WorkflowKeys.Milliseconds));
        editor.UndoCommand.Execute(null);
        Assert.Null(editor.Workflow.Find(wait.Id));
        Assert.False(editor.CanUndo);

        editor.RedoCommand.Execute(null);
        editor.RedoCommand.Execute(null);
        Assert.Equal("800", editor.Workflow.Find(wait.Id)!.EffectiveSettings.Get(WorkflowKeys.Milliseconds));
        Assert.Equal(editor.Workflow, stored[^1]);
    }

    [Fact]
    public void Removing_a_node_takes_its_links_and_revert_brings_back_the_start()
    {
        var start = WorkflowConverter.FromBinding(new ControlBinding("key.0", ProfileActionKind.MultiAction, Item("Multi Action").ToActionReference(), new ControlAppearance(),
            [new MultiActionStep(0, Item("Mute").ToActionReference()), new MultiActionStep(1, Item("Next Track").ToActionReference())]));
        var (editor, _) = Editor(start);
        var mute = editor.Workflow.Nodes.First(node => node.Action?.ActionName == "Mute");

        editor.SelectNode(mute.Id);
        editor.DeleteSelection();

        Assert.DoesNotContain(editor.Workflow.Links, link => link.From == mute.Id || link.To == mute.Id);
        editor.RevertAllCommand.Execute(null);
        Assert.Equal(start, editor.Workflow);
    }

    [Fact]
    public void Arrange_puts_nodes_in_columns_by_their_distance_from_the_trigger()
    {
        var (editor, _) = Editor();
        var a = editor.AddNode(WorkflowNodeKinds.Toggle, 900, 400);
        var b = editor.AddNode(WorkflowNodeKinds.Wait, 10, 900);
        editor.Connect("n1", WorkflowPorts.Next, a.Id);
        editor.Connect(a.Id, WorkflowPorts.First, b.Id);

        editor.ArrangeCommand.Execute(null);

        var x = editor.Workflow.Nodes.ToDictionary(node => node.Id, node => node.X);
        Assert.True(x["n1"] < x[a.Id] && x[a.Id] < x[b.Id]);
    }

    [Fact]
    public void A_trigger_only_offers_inputs_the_control_has_and_a_lost_control_is_reported()
    {
        var knobless = new PhysicalControl("touch.0", "Touch 1", ControlType.TouchSurface, new Rect(0, 0, 10, 10), InputCapabilities.Press, OutputCapabilities.ColorImage);
        var editor = new WorkflowEditorViewModel("device", "layout", knobless, "Touch 1", WorkflowConverter.Starter(), Actions, _ => false);
        editor.SelectNode("n1");

        var choice = Assert.IsType<ChoiceSettingField>(editor.NodeSettings!.Fields.Single());
        Assert.Equal([WorkflowTriggers.Down], choice.Options.Select(option => option.Value));

        editor.AddNode(WorkflowNodeKinds.Wait, 0, 0);
        Assert.Contains("no longer has this workflow", editor.Status);
    }
}

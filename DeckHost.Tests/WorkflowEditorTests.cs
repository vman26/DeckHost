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

    [Test]
    public void An_action_added_with_a_node_selected_is_linked_after_it_and_stored_on_the_control()
    {
        var (editor, stored) = Editor();
        editor.SelectNode("n1");

        var node = editor.AddAction(Item("Mute"))!;

        Assert.That(Expect.Single(editor.Workflow.Links), Is.EqualTo(new WorkflowLink("n1", WorkflowPorts.Next, node.Id)));
        Assert.That(stored[^1], Is.EqualTo(editor.Workflow));
        Assert.That(editor.SelectedNode?.Id, Is.EqualTo(node.Id));
        Assert.That(editor.SelectedIsAction, Is.True);
        Assert.That(editor.Overview, Does.Contain("1 action"));
    }

    [Test]
    public void A_node_dropped_onto_a_link_splits_it_in_one_undo_step()
    {
        var (editor, _) = Editor();
        var first = editor.AddNode(WorkflowNodeKinds.Wait, 300, 40);
        var last = editor.AddNode(WorkflowNodeKinds.Wait, 600, 40);
        editor.Connect(first.Id, WorkflowPorts.Next, last.Id);
        var link = Expect.Single(editor.Workflow.Links);
        var loose = editor.AddNode(WorkflowNodeKinds.Wait, 450, 200);
        Assert.That(editor.CanInsertIntoLink(loose.Id), Is.True);

        Assert.That(editor.InsertIntoLink(loose.Id, link), Is.Null);

        Assert.That(editor.Workflow.Links, Is.EquivalentTo(new[]
        {
            new WorkflowLink(first.Id, WorkflowPorts.Next, loose.Id),
            new WorkflowLink(loose.Id, WorkflowPorts.Next, last.Id),
        }));
        editor.UndoCommand.Execute(null);
        Assert.That(Expect.Single(editor.Workflow.Links), Is.EqualTo(link), "one step brings the old link back");
        Assert.That(editor.Workflow.Nodes.Any(node => node.Id == loose.Id), Is.True, "the node itself stays");
    }

    [Test]
    public void A_node_can_be_moved_and_put_into_a_link_as_one_undo_step()
    {
        var (editor, _) = Editor();
        var first = editor.AddNode(WorkflowNodeKinds.Wait, 300, 40);
        var last = editor.AddNode(WorkflowNodeKinds.Wait, 600, 40);
        editor.Connect(first.Id, WorkflowPorts.Next, last.Id);
        var link = Expect.Single(editor.Workflow.Links);
        var loose = editor.AddNode(WorkflowNodeKinds.Wait, 450, 200);

        Assert.That(editor.InsertIntoLink(loose.Id, link, 440, 60), Is.Null);

        var placed = editor.Workflow.Find(loose.Id)!;
        Assert.That((placed.X, placed.Y), Is.EqualTo((440d, 60d)));
        editor.UndoCommand.Execute(null);
        var back = editor.Workflow.Find(loose.Id)!;
        Assert.That((back.X, back.Y), Is.EqualTo((450d, 200d)), "the same step puts it back where it was");
        Assert.That(Expect.Single(editor.Workflow.Links), Is.EqualTo(link));
    }

    [Test]
    public void Taking_the_links_off_a_node_keeps_the_node_and_its_neighbours()
    {
        var (editor, _) = Editor();
        var first = editor.AddNode(WorkflowNodeKinds.Wait, 300, 40);
        var middle = editor.AddNode(WorkflowNodeKinds.Wait, 450, 40);
        var last = editor.AddNode(WorkflowNodeKinds.Wait, 600, 40);
        editor.Connect(first.Id, WorkflowPorts.Next, middle.Id);
        editor.Connect(middle.Id, WorkflowPorts.Next, last.Id);
        editor.SelectNode(middle.Id);
        Assert.That(editor.SelectedHasLinks, Is.True);

        editor.DisconnectSelection();

        Assert.That(editor.Workflow.Links, Is.Empty);
        Assert.That(editor.Workflow.Nodes.Select(static node => node.Id), Does.Contain(middle.Id).And.Contain(first.Id).And.Contain(last.Id));
        Assert.That(editor.SelectedHasLinks, Is.False);
        editor.UndoCommand.Execute(null);
        Assert.That(editor.Workflow.Links, Has.Count.EqualTo(2));
    }

    [Test]
    public void Actions_are_grouped_by_plugin_and_a_search_opens_the_matching_groups()
    {
        var (editor, _) = Editor();
        Assert.That(editor.ActionGroups, Is.Not.Empty);
        Assert.That(editor.ActionGroups.Select(static group => group.PluginId), Is.Unique);
        Assert.That(editor.ActionGroups.Sum(static group => group.Actions.Count), Is.EqualTo(editor.AvailableActions.Count));
        Assert.That(editor.ActionGroups.Any(static group => group.IsExpanded), Is.False, "collapsed like the main window until opened");

        var name = editor.AvailableActions[0].Name;
        editor.ActionSearch = name;

        Assert.That(editor.ActionGroups, Is.Not.Empty);
        Assert.That(editor.ActionGroups.All(static group => group.IsExpanded), Is.True);
        Assert.That(editor.ActionGroups.SelectMany(static group => group.Actions).Any(action => action.Name == name), Is.True);

        editor.ActionSearch = "no action is called this";
        Assert.That(editor.ActionGroups, Is.Empty);
    }

    [Test]
    public void An_action_added_after_an_action_is_linked_from_then_never_from_the_error_output()
    {
        var (editor, _) = Editor();
        var first = Expect.IsType<WorkflowNode>(editor.AddAction(Item("Play / Pause")));
        editor.SelectNode(first.Id);
        editor.AddAction(Item("Mute"));
        editor.SelectNode(first.Id);
        editor.AddAction(Item("Play / Pause"));

        Assert.That(editor.Workflow.LinksFrom(first.Id, WorkflowPorts.Error), Is.Empty, "the error output is linked by hand only");
        Assert.That(editor.Workflow.LinksFrom(first.Id, WorkflowPorts.Next).Count(), Is.EqualTo(1));
    }

    [Test]
    public void An_action_node_offers_tries_and_a_wait_for_when_it_did_not_run_and_keeps_them_on_the_node()
    {
        var (editor, stored) = Editor();
        var node = Expect.IsType<WorkflowNode>(editor.AddAction(Item("Play / Pause")));
        editor.SelectNode(node.Id);
        Assert.That(editor.HasFailureOptions, Is.True);

        var retries = Expect.IsType<NumberSettingField>(editor.FailureOptions!.Fields.First());
        retries.Value = "3";

        Assert.That(editor.Workflow.Find(node.Id)!.EffectiveSettings.GetInt(WorkflowKeys.Retries, 0), Is.EqualTo(3));
        Assert.That(stored.Last().Find(node.Id)!.EffectiveSettings.GetInt(WorkflowKeys.Retries, 0), Is.EqualTo(3));
        Assert.That(editor.Workflow.Find(node.Id)!.Action, Is.EqualTo(node.Action), "the action's own settings are untouched");

        var wait = editor.AddNode(WorkflowNodeKinds.Wait, 0, 0);
        editor.SelectNode(wait.Id);
        Assert.That(editor.HasFailureOptions, Is.False, "only for action nodes");
    }

    [Test]
    public void Putting_a_node_into_a_link_makes_room_by_moving_what_follows_it_in_one_undo_step()
    {
        var (editor, _) = Editor();
        var first = editor.AddNode(WorkflowNodeKinds.Wait, 300, 40);
        var middle = editor.AddNode(WorkflowNodeKinds.Wait, 360, 40);
        var last = editor.AddNode(WorkflowNodeKinds.Wait, 420, 40);
        var elsewhere = editor.AddNode(WorkflowNodeKinds.Wait, 420, 300);
        editor.Connect(first.Id, WorkflowPorts.Next, middle.Id);
        editor.Connect(middle.Id, WorkflowPorts.Next, last.Id);
        var link = new WorkflowLink(first.Id, WorkflowPorts.Next, middle.Id);
        var loose = editor.AddNode(WorkflowNodeKinds.Wait, 300, 200);

        Assert.That(editor.InsertIntoLink(loose.Id, link, 300, 40), Is.Null);

        double X(string id) => editor.Workflow.Find(id)!.X;
        Assert.That(X(middle.Id), Is.GreaterThanOrEqualTo(X(loose.Id) + WorkflowEditorViewModel.NodeWidth + WorkflowSpacing.Gap), "the target moved clear of the inserted node");
        Assert.That(X(last.Id) - X(middle.Id), Is.EqualTo(60), "what follows moved the same distance");
        Assert.That(X(first.Id), Is.EqualTo(300), "what comes before stays");
        Assert.That(X(elsewhere.Id), Is.EqualTo(420), "an unrelated branch stays");

        editor.UndoCommand.Execute(null);
        Assert.That((X(middle.Id), X(last.Id)), Is.EqualTo((360d, 420d)), "one Undo puts everything back");
        Assert.That(editor.Workflow.Links, Has.Count.EqualTo(2));
    }

    [Test]
    public void A_dropped_node_or_action_pushes_the_rest_of_the_path_too_and_nothing_moves_when_there_is_room()
    {
        var (editor, _) = Editor();
        var first = editor.AddNode(WorkflowNodeKinds.Wait, 100, 40);
        var last = editor.AddNode(WorkflowNodeKinds.Wait, 160, 40);
        editor.Connect(first.Id, WorkflowPorts.Next, last.Id);
        var link = Expect.Single(editor.Workflow.Links);

        var added = editor.AddNode(WorkflowNodeKinds.Wait, 100, 40, link);
        Assert.That(editor.Workflow.Find(last.Id)!.X, Is.GreaterThanOrEqualTo(added.X + WorkflowEditorViewModel.NodeWidth + WorkflowSpacing.Gap));

        var second = editor.Workflow.Links.First(candidate => candidate.To == last.Id);
        var before = editor.Workflow.Find(last.Id)!.X;
        var action = editor.AddAction(Item("Mute"), editor.Workflow.Find(second.From)!.X, 200, second);
        Assert.That(action, Is.Not.Null);
        Assert.That(editor.Workflow.Find(last.Id)!.X, Is.GreaterThanOrEqualTo(before), "never moves left");

        // With room already there nothing moves.
        var roomy = new Workflow
        {
            Nodes = [new WorkflowNode("a", WorkflowNodeKinds.Wait, 0, 0), new WorkflowNode("b", WorkflowNodeKinds.Wait, 1000, 0), new WorkflowNode("n", WorkflowNodeKinds.Wait, 300, 0)],
            Links = [new("a", WorkflowPorts.Next, "n"), new("n", WorkflowPorts.Next, "b")],
        };
        Assert.That(WorkflowSpacing.MakeRoom(roomy, "n", "b", WorkflowEditorViewModel.NodeWidth), Is.SameAs(roomy));
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
}

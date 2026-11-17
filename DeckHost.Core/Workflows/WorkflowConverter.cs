using DeckHost.Contracts.Models;

namespace DeckHost.Core.Workflows;

/// <summary>
/// Turns a Multi Action, Multi Action Switch or Key Logic binding into the same behavior as a
/// workflow, so it can be extended with conditions, loops and branches.
/// </summary>
public static class WorkflowConverter
{
    public const double ColumnWidth = 230;
    public const double RowHeight = 120;
    public const double Left = 40;
    public const double Top = 40;

    /// <summary>A new workflow: when pressed, nothing yet.</summary>
    public static Workflow Starter() => new()
    {
        Nodes = [Trigger("n1", WorkflowTriggers.Down, 0)],
        WhileRunning = WorkflowConcurrency.Restart,
    };

    public static Workflow FromBinding(ControlBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        if (binding.ActionKind == ProfileActionKind.Workflow && binding.Workflow is { } existing)
        {
            return existing;
        }

        var builder = new Builder();
        switch (binding.ActionKind)
        {
            case ProfileActionKind.MultiActionSwitch:
            {
                var trigger = builder.Add(Trigger(builder.NextId(), WorkflowTriggers.Down, 0));
                var toggle = builder.Add(new WorkflowNode(builder.NextId(), WorkflowNodeKinds.Toggle, X(1), Y(0)));
                builder.Link(trigger.Id, WorkflowPorts.Next, toggle.Id);
                builder.Chain(toggle.Id, WorkflowPorts.First, StepsOf(binding, StepGroups.First), column: 2, row: 0);
                builder.Chain(toggle.Id, WorkflowPorts.Second, StepsOf(binding, StepGroups.Second), column: 2, row: 1);
                break;
            }

            case ProfileActionKind.KeyLogic:
            {
                var row = 0;
                foreach (var (group, trigger) in new[]
                         {
                             (StepGroups.Press, WorkflowTriggers.Tap),
                             (StepGroups.DoublePress, WorkflowTriggers.DoublePress),
                             (StepGroups.Hold, WorkflowTriggers.Hold),
                         })
                {
                    var steps = StepsOf(binding, group);
                    if (steps.Count == 0 && group != StepGroups.Press)
                    {
                        continue;
                    }

                    var start = builder.Add(Trigger(builder.NextId(), trigger, row));
                    builder.Chain(start.Id, WorkflowPorts.Next, steps, column: 1, row);
                    row++;
                }

                break;
            }

            case ProfileActionKind.MultiAction:
            {
                var start = builder.Add(Trigger(builder.NextId(), WorkflowTriggers.Down, 0));
                builder.Chain(start.Id, WorkflowPorts.Next, [.. binding.Steps.OrderBy(static step => step.Order)], column: 1, row: 0);
                break;
            }

            default:
            {
                // A plain action becomes: when pressed → the action.
                var start = builder.Add(Trigger(builder.NextId(), WorkflowTriggers.Down, 0));
                builder.Chain(start.Id, WorkflowPorts.Next, [new MultiActionStep(0, binding.Action)], column: 1, row: 0);
                break;
            }
        }

        return builder.Build();
    }

    /// <summary>
    /// Adds an action at the end of the first trigger's chain (following each node's first output),
    /// as dropping an action on a workflow control does. A workflow without a trigger gets one.
    /// </summary>
    public static Workflow Append(Workflow workflow, ActionReference action)
    {
        ArgumentNullException.ThrowIfNull(workflow);
        ArgumentNullException.ThrowIfNull(action);
        if (workflow.Nodes.FirstOrDefault(static node => node.Kind == WorkflowNodeKinds.Trigger) is not { } trigger)
        {
            trigger = Trigger(NextId(workflow), WorkflowTriggers.Down, 0) with
            {
                Y = workflow.Nodes.Count == 0 ? Top : workflow.Nodes.Max(static node => node.Y) + RowHeight,
            };
            workflow = workflow with { Nodes = [.. workflow.Nodes, trigger] };
        }

        var end = trigger;
        var seen = new HashSet<string>(StringComparer.Ordinal) { end.Id };
        while (WorkflowNodeKinds.OutputsOf(end.Kind).FirstOrDefault() is { } port &&
               workflow.LinksFrom(end.Id, port).Select(link => workflow.Find(link.To)).FirstOrDefault() is { } next &&
               seen.Add(next.Id))
        {
            end = next;
        }

        var node = new WorkflowNode(NextId(workflow), WorkflowNodeKinds.Action, end.X + ColumnWidth, end.Y) { Action = action };
        var links = WorkflowNodeKinds.OutputsOf(end.Kind).FirstOrDefault() is { } from
            ? [.. workflow.Links, new WorkflowLink(end.Id, from, node.Id)]
            : workflow.Links;
        return workflow with { Nodes = [.. workflow.Nodes, node], Links = links };
    }

    /// <summary>An id not used by any node yet ("n1", "n2", …).</summary>
    public static string NextId(Workflow workflow)
    {
        var highest = workflow.Nodes
            .Select(static node => node.Id.StartsWith('n') && int.TryParse(node.Id.AsSpan(1), out var number) ? number : 0)
            .DefaultIfEmpty(0)
            .Max();
        return $"n{highest + 1}";
    }

    private static List<MultiActionStep> StepsOf(ControlBinding binding, string group) =>
        [.. binding.Steps.Where(step => StepGroups.Of(step, binding.ActionKind) == group).OrderBy(static step => step.Order)];

    private static WorkflowNode Trigger(string id, string on, int row) =>
        new(id, WorkflowNodeKinds.Trigger, X(0), Y(row)) { Settings = new ActionSettings([new(WorkflowKeys.On, on)]) };

    private static double X(int column) => Left + (column * ColumnWidth);

    private static double Y(int row) => Top + (row * RowHeight);

    private sealed class Builder
    {
        private readonly List<WorkflowNode> _nodes = [];
        private readonly List<WorkflowLink> _links = [];

        public string NextId() => $"n{_nodes.Count + 1}";

        public WorkflowNode Add(WorkflowNode node)
        {
            _nodes.Add(node);
            return node;
        }

        public void Link(string from, string port, string to) => _links.Add(new WorkflowLink(from, port, to));

        /// <summary>Steps in a row: a Wait before each step that had one, then its action.</summary>
        public void Chain(string from, string port, IReadOnlyList<MultiActionStep> steps, int column, int row)
        {
            var previous = from;
            var previousPort = port;
            foreach (var step in steps)
            {
                if (step.DelayMs > 0)
                {
                    var wait = Add(new WorkflowNode(NextId(), WorkflowNodeKinds.Wait, X(column++), Y(row))
                    {
                        Settings = new ActionSettings([new(WorkflowKeys.Milliseconds, Math.Clamp(step.DelayMs, 0, WorkflowKeys.MaxWaitMs).ToString(System.Globalization.CultureInfo.InvariantCulture))]),
                    });
                    Link(previous, previousPort, wait.Id);
                    previous = wait.Id;
                    previousPort = WorkflowPorts.Next;
                }

                var action = Add(new WorkflowNode(NextId(), WorkflowNodeKinds.Action, X(column++), Y(row)) { Action = step.Action });
                Link(previous, previousPort, action.Id);
                previous = action.Id;
                previousPort = WorkflowPorts.Next;
            }
        }

        public Workflow Build() => new() { Nodes = [.. _nodes], Links = [.. _links], WhileRunning = WorkflowConcurrency.Restart };
    }
}

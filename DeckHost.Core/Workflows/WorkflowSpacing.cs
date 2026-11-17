using DeckHost.Contracts.Models;

namespace DeckHost.Core.Workflows;

/// <summary>Keeps a workflow's nodes from piling up when one is put between two others.</summary>
public static class WorkflowSpacing
{
    /// <summary>The space left between a node's right edge and the next node.</summary>
    public const double Gap = 50;

    /// <summary>The grid nodes snap to.</summary>
    private const double Grid = 10;

    /// <summary>The node and everything its links lead to, directly or not.</summary>
    public static IReadOnlySet<string> Downstream(Workflow workflow, string nodeId)
    {
        ArgumentNullException.ThrowIfNull(workflow);
        var seen = new HashSet<string>(StringComparer.Ordinal) { nodeId };
        var queue = new Queue<string>([nodeId]);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            foreach (var link in workflow.Links)
            {
                if (link.From == current && seen.Add(link.To))
                {
                    queue.Enqueue(link.To);
                }
            }
        }

        return seen;
    }

    /// <summary>
    /// After <paramref name="insertedId"/> was put in front of <paramref name="targetId"/>: moves the
    /// target and every node after it to the right until the inserted node (<paramref name="nodeWidth"/>
    /// wide) and the target have <see cref="Gap"/> between them. Nothing moves when there is room.
    /// </summary>
    public static Workflow MakeRoom(Workflow workflow, string insertedId, string targetId, double nodeWidth)
    {
        ArgumentNullException.ThrowIfNull(workflow);
        if (workflow.Find(insertedId) is not { } inserted || workflow.Find(targetId) is not { } target)
        {
            return workflow;
        }

        var shortBy = inserted.X + nodeWidth + Gap - target.X;
        if (shortBy <= 0)
        {
            return workflow;
        }

        var dx = Math.Ceiling(shortBy / Grid) * Grid;
        var moving = Downstream(workflow, targetId);
        return workflow with
        {
            Nodes = [.. workflow.Nodes.Select(node => node.Id != insertedId && moving.Contains(node.Id) ? node with { X = node.X + dx } : node)],
        };
    }
}

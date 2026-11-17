using DeckHost.Contracts.Models;

namespace DeckHost.Core.Workflows;

/// <summary>
/// The order the editor's Arrange lays nodes out in: columns by distance from a trigger, and inside a column the order
/// the flow reads in: each node's branches follow it in the order of its ports (a Switch's cases as the user ordered
/// them, then Else), not in the order the nodes happen to be stored.
/// </summary>
public static class WorkflowFlowOrder
{
    /// <summary>The column of each node reachable from a trigger (the longest distance from one; workflows have no loops).</summary>
    public static IReadOnlyDictionary<string, int> Depths(Workflow workflow)
    {
        ArgumentNullException.ThrowIfNull(workflow);
        var depth = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var trigger in workflow.Nodes.Where(static node => node.Kind == WorkflowNodeKinds.Trigger))
        {
            depth[trigger.Id] = 0;
        }

        var queue = new Queue<string>(depth.Keys);
        var guard = 0;
        while (queue.Count > 0 && guard++ < 100_000)
        {
            var id = queue.Dequeue();
            foreach (var next in Successors(workflow, id))
            {
                var column = depth[id] + 1;
                if (!depth.TryGetValue(next, out var known) || known < column)
                {
                    depth[next] = column;
                    queue.Enqueue(next);
                }
            }
        }

        return depth;
    }

    /// <summary>Every node once, triggers first (top to bottom as they are), each followed by what it leads to in port order.</summary>
    public static IReadOnlyList<string> Order(Workflow workflow)
    {
        ArgumentNullException.ThrowIfNull(workflow);
        var order = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        void Visit(string id)
        {
            if (!seen.Add(id))
            {
                return;
            }

            order.Add(id);
            foreach (var next in Successors(workflow, id))
            {
                Visit(next);
            }
        }

        foreach (var trigger in workflow.Nodes.Where(static node => node.Kind == WorkflowNodeKinds.Trigger).OrderBy(static node => node.Y))
        {
            Visit(trigger.Id);
        }

        // Pieces no trigger leads to: start with the nodes nothing leads into, then whatever is left.
        var hasInput = workflow.Links.Select(static link => link.To).ToHashSet(StringComparer.Ordinal);
        foreach (var rest in workflow.Nodes.Where(node => !seen.Contains(node.Id)).OrderBy(node => hasInput.Contains(node.Id)).ThenBy(static node => node.Y))
        {
            Visit(rest.Id);
        }

        return order;
    }

    /// <summary>What a node leads to: its output ports in order, and for each port its links in the order they were made.</summary>
    private static IEnumerable<string> Successors(Workflow workflow, string id)
    {
        if (workflow.Find(id) is not { } node)
        {
            yield break;
        }

        foreach (var port in WorkflowNodeKinds.OutputsOf(node))
        {
            foreach (var link in workflow.Links.Where(link => link.From == id && link.Port == port))
            {
                yield return link.To;
            }
        }
    }
}

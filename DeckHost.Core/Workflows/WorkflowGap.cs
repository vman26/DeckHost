using DeckHost.Contracts.Models;

namespace DeckHost.Core.Workflows;

/// <summary>What happens to the links around a node that is removed from a workflow.</summary>
public enum GapMode
{
    /// <summary>The nodes before it are linked to the nodes after it, so the flow still runs (the first path when several).</summary>
    Reconnect,

    /// <summary>Only the first path (the first output port that has links) is bridged.</summary>
    FirstPath,

    /// <summary>Its links are removed and nothing takes its place.</summary>
    Disconnect,
}

/// <summary>Removing a node without leaving a hole in the flow.</summary>
public static class WorkflowGap
{
    /// <summary>
    /// Whether removing the node needs the user's choice: something leads into it and it continues on more than one
    /// output port (a Switch or If with several branches), so there is no single place to link the previous nodes to.
    /// </summary>
    public static bool NeedsChoice(Workflow workflow, string nodeId)
    {
        ArgumentNullException.ThrowIfNull(workflow);
        return workflow.Find(nodeId) is { } node && workflow.Links.Any(link => link.To == nodeId) && PortsWithLinks(workflow, node).Count > 1;
    }

    /// <summary>The workflow without the node. With <see cref="GapMode.Reconnect"/> or <see cref="GapMode.FirstPath"/> what led into it now leads to what it led to.</summary>
    public static Workflow Remove(Workflow workflow, string nodeId, GapMode mode)
    {
        ArgumentNullException.ThrowIfNull(workflow);
        if (workflow.Find(nodeId) is not { } node)
        {
            return workflow;
        }

        var without = workflow with
        {
            Nodes = [.. workflow.Nodes.Where(candidate => candidate.Id != nodeId)],
            Links = [.. workflow.Links.Where(link => link.From != nodeId && link.To != nodeId)],
        };
        if (mode == GapMode.Disconnect)
        {
            return without;
        }

        var ports = PortsWithLinks(workflow, node);
        var targets = workflow.Links
            .Where(link => link.From == nodeId && (ports.Count == 0 || link.Port == ports[0]))
            .Select(static link => link.To)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var bridged = without;
        foreach (var incoming in workflow.Links.Where(link => link.To == nodeId))
        {
            foreach (var target in targets)
            {
                var bridge = new WorkflowLink(incoming.From, incoming.Port, target);
                if (incoming.From != target && !bridged.Links.Contains(bridge) && !bridged.WouldCycle(incoming.From, target))
                {
                    bridged = bridged with { Links = [.. bridged.Links, bridge] };
                }
            }
        }

        return bridged;
    }

    private static IReadOnlyList<string> PortsWithLinks(Workflow workflow, WorkflowNode node) =>
        [.. WorkflowNodeKinds.OutputsOf(node).Where(port => workflow.Links.Any(link => link.From == node.Id && link.Port == port))];
}

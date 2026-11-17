using DeckHost.Contracts.Models;
using DeckHost.Core.Workflows;

namespace DeckHost.Tests;

/// <summary>The order Arrange lays nodes out in: columns by distance, rows in the order of the flow.</summary>
public sealed class WorkflowFlowOrderTests
{
    private const string Trigger = "t";
    private const string Switch = "s";
    private const string FirstCase = "first";
    private const string SecondCase = "second";
    private const string ElseTarget = "else";
    private const string FirstTarget = "f";
    private const string SecondTarget = "g";

    private static WorkflowNode Node(string id, string kind = WorkflowNodeKinds.Wait, double y = 0) => new(id, kind, 0, y);

    // Stored in the opposite order of the flow on purpose: Else first, then the cases back to front.
    private static Workflow SwitchWithTwoCasesAndElse() => new()
    {
        Nodes =
        [
            Node(Trigger, WorkflowNodeKinds.Trigger),
            new WorkflowNode(Switch, WorkflowNodeKinds.Switch) { Settings = new ActionSettings([new(WorkflowKeys.Cases, $"{FirstCase}=one\n{SecondCase}=two")]) },
            Node(ElseTarget, y: 10),
            Node(SecondTarget, y: 20),
            Node(FirstTarget, y: 30),
        ],
        Links =
        [
            new WorkflowLink(Trigger, WorkflowPorts.Next, Switch),
            new WorkflowLink(Switch, WorkflowPorts.Default, ElseTarget),
            new WorkflowLink(Switch, WorkflowCases.PortOf(SecondCase), SecondTarget),
            new WorkflowLink(Switch, WorkflowCases.PortOf(FirstCase), FirstTarget),
        ],
    };

    [Test]
    public void Order_SwitchWithCasesAndElse_FollowsTheCasesInOrderAndElseLast()
    {
        // Arrange
        var workflow = SwitchWithTwoCasesAndElse();

        // Act
        var order = WorkflowFlowOrder.Order(workflow);

        // Assert
        Assert.That(order, Is.EqualTo(new[] { Trigger, Switch, FirstTarget, SecondTarget, ElseTarget }));
    }

    [Test]
    public void Depths_SwitchWithCasesAndElse_PutsTheBranchesInTheColumnAfterTheSwitch()
    {
        // Arrange
        var workflow = SwitchWithTwoCasesAndElse();

        // Act
        var depths = WorkflowFlowOrder.Depths(workflow);

        // Assert
        Assert.That((depths[Trigger], depths[Switch], depths[FirstTarget], depths[ElseTarget]), Is.EqualTo((0, 1, 2, 2)));
    }

    [Test]
    public void Order_NodeWithoutLinks_IsListedAfterTheFlow()
    {
        // Arrange
        var workflow = SwitchWithTwoCasesAndElse() with { Nodes = [.. SwitchWithTwoCasesAndElse().Nodes, Node("loose", y: 5)] };

        // Act
        var order = WorkflowFlowOrder.Order(workflow);

        // Assert
        Assert.That(order[^1], Is.EqualTo("loose"));
    }
}

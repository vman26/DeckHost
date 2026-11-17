using DeckHost.Contracts.Models;
using DeckHost.Core.Workflows;

namespace DeckHost.Tests;

/// <summary>Removing a node from a workflow without leaving a hole in the flow.</summary>
public sealed class WorkflowGapTests
{
    private const string Trigger = "t";
    private const string Before = "a";
    private const string Middle = "m";
    private const string After = "b";
    private const string Other = "c";
    private const string OtherBefore = "x";

    private static WorkflowNode Node(string id, string kind = WorkflowNodeKinds.Wait) => new(id, kind);

    private static WorkflowLink Next(string from, string to) => new(from, WorkflowPorts.Next, to);

    private static Workflow Chain() => new()
    {
        Nodes = [Node(Trigger, WorkflowNodeKinds.Trigger), Node(Middle), Node(After)],
        Links = [Next(Trigger, Middle), Next(Middle, After)],
    };

    private static Workflow WithAnIfInTheMiddle() => new()
    {
        Nodes = [Node(Trigger, WorkflowNodeKinds.Trigger), Node(Middle, WorkflowNodeKinds.If), Node(After), Node(Other)],
        Links = [Next(Trigger, Middle), new WorkflowLink(Middle, WorkflowPorts.Yes, After), new WorkflowLink(Middle, WorkflowPorts.No, Other)],
    };

    [Test]
    public void Remove_MiddleOfAChain_LinksItsNeighbours()
    {
        // Arrange
        var workflow = Chain();

        // Act
        var result = WorkflowGap.Remove(workflow, Middle, GapMode.Reconnect);

        // Assert
        Assert.That(result.Links, Is.EqualTo(new[] { Next(Trigger, After) }));
    }

    [Test]
    public void Remove_MiddleOfAChain_RemovesTheNode()
    {
        // Arrange
        var workflow = Chain();

        // Act
        var result = WorkflowGap.Remove(workflow, Middle, GapMode.Reconnect);

        // Assert
        Assert.That(result.Nodes.Select(static node => node.Id), Is.EqualTo(new[] { Trigger, After }));
    }

    [Test]
    public void Remove_NodeWithTwoPredecessors_LinksEachOfThemToTheSuccessor()
    {
        // Arrange
        var workflow = new Workflow
        {
            Nodes = [Node(Trigger, WorkflowNodeKinds.Trigger), Node(OtherBefore), Node(Middle), Node(After)],
            Links = [Next(Trigger, Middle), Next(OtherBefore, Middle), Next(Middle, After)],
        };

        // Act
        var result = WorkflowGap.Remove(workflow, Middle, GapMode.Reconnect);

        // Assert
        Assert.That(result.Links, Is.EquivalentTo(new[] { Next(Trigger, After), Next(OtherBefore, After) }));
    }

    [Test]
    public void Remove_NodeWithoutASuccessor_OnlyRemovesItsLinks()
    {
        // Arrange
        var workflow = new Workflow { Nodes = [Node(Trigger, WorkflowNodeKinds.Trigger), Node(Middle)], Links = [Next(Trigger, Middle)] };

        // Act
        var result = WorkflowGap.Remove(workflow, Middle, GapMode.Reconnect);

        // Assert
        Assert.That(result.Links, Is.Empty);
    }

    [Test]
    public void Remove_DisconnectMode_LeavesNoBridge()
    {
        // Arrange
        var workflow = Chain();

        // Act
        var result = WorkflowGap.Remove(workflow, Middle, GapMode.Disconnect);

        // Assert
        Assert.That(result.Links, Is.Empty);
    }

    [Test]
    public void Remove_FirstPathMode_BridgesOnlyTheFirstBranch()
    {
        // Arrange
        var workflow = WithAnIfInTheMiddle();

        // Act
        var result = WorkflowGap.Remove(workflow, Middle, GapMode.FirstPath);

        // Assert
        Assert.That(result.Links, Is.EqualTo(new[] { Next(Trigger, After) }));
    }

    [Test]
    public void Remove_UnknownNode_ReturnsTheWorkflowUnchanged()
    {
        // Arrange
        var workflow = Chain();

        // Act
        var result = WorkflowGap.Remove(workflow, "missing", GapMode.Reconnect);

        // Assert
        Assert.That(result, Is.SameAs(workflow));
    }

    [Test]
    public void NeedsChoice_NodeWithOneBranchAndAPredecessor_IsFalse()
    {
        // Arrange
        var workflow = Chain();

        // Act
        var needs = WorkflowGap.NeedsChoice(workflow, Middle);

        // Assert
        Assert.That(needs, Is.False);
    }

    [Test]
    public void NeedsChoice_NodeWithTwoBranchesAndAPredecessor_IsTrue()
    {
        // Arrange
        var workflow = WithAnIfInTheMiddle();

        // Act
        var needs = WorkflowGap.NeedsChoice(workflow, Middle);

        // Assert
        Assert.That(needs, Is.True);
    }

    [Test]
    public void NeedsChoice_NodeWithTwoBranchesButNothingLeadingIntoIt_IsFalse()
    {
        // Arrange
        var workflow = WithAnIfInTheMiddle() with { Links = [new WorkflowLink(Middle, WorkflowPorts.Yes, After), new WorkflowLink(Middle, WorkflowPorts.No, Other)] };

        // Act
        var needs = WorkflowGap.NeedsChoice(workflow, Middle);

        // Assert
        Assert.That(needs, Is.False);
    }
}

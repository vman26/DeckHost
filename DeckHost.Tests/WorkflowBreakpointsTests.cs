using DeckHost.App.ViewModels;
using DeckHost.Core.Devices;
using DeckHost.Core.State;

namespace DeckHost.Tests;

/// <summary>Debugging only works while a workflow editor is open; the points are kept for the next time.</summary>
public sealed class WorkflowBreakpointsTests
{
    private const string Device = "fake.device";
    private const string Control = "key.0";
    private const string Node = "2";

    private sealed record Setup(WorkflowBreakpoints Points, DeviceHostService Host, VariableStore Store);

    private static Setup Create()
    {
        var store = new VariableStore();
        var host = new DeviceHostService([new FakeDevicePlugin()], [], store);
        return new Setup(new WorkflowBreakpoints(host), host, store);
    }

    [Test]
    public void IsDebugging_NoEditorOpen_IsFalse()
    {
        // Arrange
        var setup = Create();

        // Act
        var debugging = setup.Points.IsDebugging;

        // Assert
        Assert.That(debugging, Is.False);
    }

    [Test]
    public void IsDebugging_AnEditorOpened_IsTrue()
    {
        // Arrange
        var setup = Create();

        // Act
        setup.Points.Attach();

        // Assert
        Assert.That(setup.Points.IsDebugging, Is.True);
    }

    [Test]
    public void IsDebugging_TheLastEditorClosed_IsFalse()
    {
        // Arrange
        var setup = Create();
        setup.Points.Attach();
        setup.Points.Attach();
        setup.Points.Detach();

        // Act
        setup.Points.Detach();

        // Assert
        Assert.That(setup.Points.IsDebugging, Is.False);
    }

    [Test]
    public void Of_PointsSetWhileAnEditorWasOpenAndThenClosed_AreKept()
    {
        // Arrange
        var setup = Create();
        setup.Points.Attach();
        setup.Points.Toggle(Device, Control, Node);

        // Act
        setup.Points.Detach();

        // Assert
        Assert.That(setup.Points.Of(Device, Control), Is.EqualTo(new[] { Node }));
    }

    [Test]
    public void Has_PointSetWithoutAnEditorOpen_IsKept()
    {
        // Arrange
        var setup = Create();

        // Act
        setup.Points.Toggle(Device, Control, Node);

        // Assert
        Assert.That(setup.Points.Has(Device, Control, Node), Is.True);
    }
}

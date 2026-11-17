using DeckHost.Core.Devices;

namespace DeckHost.Tests;

/// <summary>The marks the host keeps between two draws.</summary>
public sealed class DirtySetTests
{
    private const string Device = "device";
    private const string FirstControl = "key.0";
    private const string SecondControl = "key.1";

    [Test]
    public void MarkControl_DeviceAlreadyMarkedWhole_StaysWhole()
    {
        // Arrange
        var dirty = new DirtySet();
        dirty.MarkDevice(Device);

        // Act
        dirty.MarkControl(Device, FirstControl);

        // Assert
        Assert.That(dirty.IsWhole(Device), Is.True);
    }

    [Test]
    public void MarkDevice_AfterAControl_BecomesWhole()
    {
        // Arrange
        var dirty = new DirtySet();
        dirty.MarkControl(Device, FirstControl);

        // Act
        dirty.MarkDevice(Device);

        // Assert
        Assert.That(dirty.IsWhole(Device), Is.True);
    }

    [Test]
    public void Drain_TwoControlsMarked_ReturnsBoth()
    {
        // Arrange
        var dirty = new DirtySet();
        dirty.MarkControl(Device, FirstControl);
        dirty.MarkControl(Device, SecondControl);

        // Act
        var marked = Expect.Single(dirty.Drain());

        // Assert
        Assert.That(marked.Controls, Is.EquivalentTo(new[] { FirstControl, SecondControl }));
    }

    [Test]
    public void Drain_AfterMarks_LeavesTheSetEmpty()
    {
        // Arrange
        var dirty = new DirtySet();
        dirty.MarkControl(Device, FirstControl);

        // Act
        dirty.Drain();

        // Assert
        Assert.That(dirty.IsEmpty, Is.True);
    }

    [Test]
    public void MarkControl_IdsInAnotherCase_AreTheSameControl()
    {
        // Arrange
        var dirty = new DirtySet();
        dirty.MarkControl(Device, FirstControl);

        // Act
        dirty.MarkControl(Device.ToUpperInvariant(), FirstControl.ToUpperInvariant());

        // Assert
        Assert.That(Expect.Single(dirty.Drain()).Controls, Has.Count.EqualTo(1));
    }
}

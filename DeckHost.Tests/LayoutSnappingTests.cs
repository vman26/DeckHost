using DeckHost.App.ViewModels;

namespace DeckHost.Tests;

/// <summary>The device creator's snapping, aligning and distributing.</summary>
public sealed class LayoutSnappingTests
{
    private const double Size = 72;
    private const double Gap = 14;
    private const double Near = 3;

    private static SnapRect Box(double x, double y) => new(x, y, Size, Size);

    [Test]
    public void Snap_LeftEdgeNearAnotherBoxsLeftEdge_LinesTheEdgesUp()
    {
        // Arrange
        var others = new[] { Box(100, 0) };

        // Act
        var result = LayoutSnapping.Snap(Box(100 + Near, 200), others);

        // Assert
        Assert.That(result.X, Is.EqualTo(100));
        Assert.That(result.Guides.Any(static guide => guide.Vertical && guide.Position == 100), Is.True);
    }

    [Test]
    public void Snap_CenterNearAnotherBoxsCenter_LinesTheCentersUp()
    {
        // Arrange
        var others = new[] { new SnapRect(100, 0, 100, 100) };

        // Act
        var result = LayoutSnapping.Snap(new SnapRect(120 + Near, 200, 60, 60), others);

        // Assert
        Assert.That(result.X, Is.EqualTo(120));
    }

    [Test]
    public void Snap_FarFromEverything_StaysWhereItIs()
    {
        // Arrange
        var others = new[] { Box(0, 0) };

        // Act
        var result = LayoutSnapping.Snap(Box(300, 300), others);

        // Assert
        Assert.That((result.X, result.Y), Is.EqualTo((300d, 300d)));
        Assert.That(result.Guides, Is.Empty);
    }

    [Test]
    public void Snap_BothAxesNearAnotherBox_SnapsBoth()
    {
        // Arrange
        var others = new[] { Box(100, 100) };

        // Act
        var result = LayoutSnapping.Snap(Box(100 + Near, 100 - Near), others);

        // Assert
        Assert.That((result.X, result.Y), Is.EqualTo((100d, 100d)));
    }

    [Test]
    public void Snap_BetweenTwoBoxesInARow_CentersItInTheGap()
    {
        // Arrange
        // Two boxes with a 100-unit hole between them; the moving box is 72 wide, so 14 is left on each side.
        var others = new[] { Box(0, 0), Box(Size + 100, 0) };

        // Act
        var result = LayoutSnapping.Snap(Box(Size + 14 + Near, 0), others);

        // Assert
        Assert.That(result.X, Is.EqualTo(Size + 14));
    }

    [Test]
    public void Snap_NextToARowWithEqualGaps_KeepsTheSameGap()
    {
        // Arrange
        var others = new[] { Box(0, 0), Box(Size + Gap, 0) };
        var nextSlot = (2 * (Size + Gap));

        // Act
        var result = LayoutSnapping.Snap(Box(nextSlot + Near, 0), others);

        // Assert
        Assert.That(result.X, Is.EqualTo(nextSlot));
    }

    [Test]
    public void Snap_TheSameGapOnTheLeftOfARow_KeepsTheSameGapToo()
    {
        // Arrange
        var others = new[] { Box(300, 0), Box(300 + Size + Gap, 0) };
        var slot = 300 - Size - Gap;

        // Act
        var result = LayoutSnapping.Snap(Box(slot - Near, 0), others);

        // Assert
        Assert.That(result.X, Is.EqualTo(slot));
    }

    [Test]
    public void Snap_TheSameGapInAColumn_SnapsVertically()
    {
        // Arrange
        var others = new[] { Box(0, 0), Box(0, Size + Gap) };
        var slot = 2 * (Size + Gap);

        // Act
        var result = LayoutSnapping.Snap(Box(0, slot + Near), others);

        // Assert
        Assert.That(result.Y, Is.EqualTo(slot));
    }

    [TestCase(LayoutAlignment.Left, 10, 10)]
    [TestCase(LayoutAlignment.Right, 138, 138)]
    public void Align_TwoBoxesOfEqualSize_MovesBothToTheSameVerticalEdge(LayoutAlignment alignment, double firstX, double secondX)
    {
        // Arrange
        var boxes = new[] { Box(10, 0), Box(138, 50) };

        // Act
        var moved = LayoutSnapping.Align(boxes, alignment);

        // Assert
        Assert.That(moved.Select(static position => position.X), Is.EqualTo(new[] { firstX, secondX }));
    }

    [Test]
    public void Align_CenterVertically_PutsTheCentersOnTheSameLine()
    {
        // Arrange
        var boxes = new[] { Box(0, 0), new SnapRect(100, 100, Size, 40) };

        // Act
        var moved = LayoutSnapping.Align(boxes, LayoutAlignment.CenterVertically);

        // Assert
        var centers = new[] { moved[0].Y + (Size / 2), moved[1].Y + 20 };
        Assert.That(centers[0], Is.EqualTo(centers[1]));
    }

    [Test]
    public void Align_NoBoxes_ReturnsNothing()
    {
        // Act
        var moved = LayoutSnapping.Align([], LayoutAlignment.Top);

        // Assert
        Assert.That(moved, Is.Empty);
    }

    [Test]
    public void Distribute_ThreeBoxesHorizontally_MakesTheGapsEqualAndKeepsTheOuterOnes()
    {
        // Arrange
        var boxes = new[] { Box(0, 0), Box(100, 0), Box(400, 0) };

        // Act
        var moved = LayoutSnapping.Distribute(boxes, horizontally: true);

        // Assert
        var expectedMiddle = (400 + Size - (3 * Size)) / 2 + Size;
        Assert.That(moved[0].X, Is.EqualTo(0));
        Assert.That(moved[2].X, Is.EqualTo(400));
        Assert.That(moved[1].X, Is.EqualTo(expectedMiddle));
    }

    [Test]
    public void Distribute_BoxesGivenOutOfOrder_ReturnsPositionsInTheGivenOrder()
    {
        // Arrange
        var boxes = new[] { Box(400, 0), Box(0, 0), Box(100, 0) };

        // Act
        var moved = LayoutSnapping.Distribute(boxes, horizontally: true);

        // Assert
        Assert.That(moved[0].X, Is.EqualTo(400));
        Assert.That(moved[1].X, Is.EqualTo(0));
        Assert.That(moved[2].X, Is.EqualTo((400 + Size - (3 * Size)) / 2 + Size));
    }

    [Test]
    public void Distribute_TwoBoxes_ChangesNothing()
    {
        // Arrange
        var boxes = new[] { Box(0, 0), Box(300, 40) };

        // Act
        var moved = LayoutSnapping.Distribute(boxes, horizontally: false);

        // Assert
        Assert.That(moved, Is.EqualTo(new[] { (0d, 0d), (300d, 40d) }));
    }
}

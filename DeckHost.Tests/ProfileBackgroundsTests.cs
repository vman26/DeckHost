using DeckHost.App.Profiles;
using DeckHost.Contracts.Models;

namespace DeckHost.Tests;

/// <summary>Which background a profile, a page or a folder is drawn with.</summary>
public sealed class ProfileBackgroundsTests
{
    private const string OwnColor = "#112233";
    private const string RootColor = "#445566";
    private const string DefaultColor = "#778899";

    private static ProfileBackground Of(string color) => new() { Color = color };

    [Test]
    public void Effective_ProfileWithoutABackground_UsesTheDefault()
    {
        // Arrange
        var fallback = Of(DefaultColor);

        // Act
        var background = ProfileBackgrounds.Effective(new ProfileBackground(), null, fallback);

        // Assert
        Assert.That(background, Is.SameAs(fallback));
    }

    [Test]
    public void Effective_PageWithoutABackground_UsesItsRootsBackground()
    {
        // Arrange
        var root = Of(RootColor);

        // Act
        var background = ProfileBackgrounds.Effective(null, root, Of(DefaultColor));

        // Assert
        Assert.That(background, Is.SameAs(root));
    }

    [Test]
    public void Effective_ProfileWithItsOwnColor_IgnoresTheDefault()
    {
        // Arrange
        var own = Of(OwnColor);

        // Act
        var background = ProfileBackgrounds.Effective(own, Of(RootColor), Of(DefaultColor));

        // Assert
        Assert.That(background, Is.SameAs(own));
    }

    [Test]
    public void Effective_NothingSet_IsNull()
    {
        // Arrange
        ProfileBackground? none = null;

        // Act
        var background = ProfileBackgrounds.Effective(none, none, none);

        // Assert
        Assert.That(background, Is.Null);
    }
}

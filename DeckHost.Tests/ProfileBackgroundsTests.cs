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

    [Test]
    public void Effective_OverrideTurnedOff_UsesTheDefaultEvenThoughTheProfileHasABackground()
    {
        // Arrange
        var fallback = Of(DefaultColor);

        // Act
        var background = ProfileBackgrounds.Effective(Of(OwnColor), null, fallback, overridesDefault: false);

        // Assert
        Assert.That(background, Is.SameAs(fallback));
    }

    [Test]
    public void Effective_OverrideTurnedOffAndNoDefault_IsNull()
    {
        // Arrange
        var own = Of(OwnColor);

        // Act
        var background = ProfileBackgrounds.Effective(own, null, null, overridesDefault: false);

        // Assert
        Assert.That(background, Is.Null);
    }

    [Test]
    public void OverridesBackground_ProfileWithABackgroundFromAnOlderFile_IsOn()
    {
        // Arrange
        var background = Of(OwnColor);

        // Act
        var profile = new DeckHost.App.ViewModels.ProfileItemViewModel("p", "P", "dev", null, background);

        // Assert
        Assert.That(profile.OverridesBackground, Is.True);
    }

    [Test]
    public void OverridesBackground_ProfileWithoutABackground_IsOff()
    {
        // Arrange (a profile without a background)

        // Act
        var profile = new DeckHost.App.ViewModels.ProfileItemViewModel("p", "P", "dev");

        // Assert
        Assert.That(profile.OverridesBackground, Is.False);
    }

    [Test]
    public void ToModel_OverrideTurnedOff_KeepsTheStoredBackgroundAndTheChoice()
    {
        // Arrange
        var profile = new DeckHost.App.ViewModels.ProfileItemViewModel("p", "P", "dev", null, Of(OwnColor));
        profile.OverridesBackground = false;

        // Act
        var model = profile.ToModel();

        // Assert
        Assert.That(model.OverridesBackground, Is.False);
        Assert.That(model.Background!.Color, Is.EqualTo(OwnColor));
    }

    [Test]
    public void Deserialize_ProfileSavedBeforeTheCheckbox_HasNoChoice()
    {
        // Arrange
        const string oldFile = """{ "Id": "p", "Name": "P", "DeviceId": "dev", "Bindings": [] }""";

        // Act
        var profile = System.Text.Json.JsonSerializer.Deserialize<DeckHost.Contracts.Models.DeviceProfile>(oldFile)!;

        // Assert
        Assert.That(profile.OverridesBackground, Is.Null);
    }
}

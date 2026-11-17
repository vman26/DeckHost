using DeckHost.Core.State;

namespace DeckHost.Tests;

/// <summary>How variables are classified and listed for the Variables page.</summary>
public sealed class VariableCatalogTests
{
    [TestCase("", VariableTypes.Empty)]
    [TestCase("42", VariableTypes.Number)]
    [TestCase("-3.5", VariableTypes.Number)]
    [TestCase("True", VariableTypes.Boolean)]
    [TestCase("#FF8800", VariableTypes.Color)]
    [TestCase("#80FF8800", VariableTypes.Color)]
    [TestCase("#GG8800", VariableTypes.Text)]
    [TestCase("2026-11-17", VariableTypes.Date)]
    [TestCase("hello", VariableTypes.Text)]
    public void Of_GivenAValue_NamesItsType(string value, string expected)
    {
        // Act
        var type = VariableTypes.Of(value);

        // Assert
        Assert.That(type, Is.EqualTo(expected));
    }

    [TestCase(VariableTypes.Number, "abc", false)]
    [TestCase(VariableTypes.Number, "12", true)]
    [TestCase(VariableTypes.Boolean, "yes", false)]
    [TestCase(VariableTypes.Color, "#112233", true)]
    [TestCase(VariableTypes.Text, "anything", true)]
    [TestCase(VariableTypes.Number, "", true)]
    public void Fits_GivenATypeAndAValue_SaysIfTheValueBelongs(string type, string value, bool expected)
    {
        // Act
        var fits = VariableTypes.Fits(type, value);

        // Assert
        Assert.That(fits, Is.EqualTo(expected));
    }

    [TestCase("system.cpu", VariableScope.System)]
    [TestCase("vars.score", VariableScope.Global)]
    [TestCase("vars.profile|p1|score", VariableScope.Profile)]
    [TestCase("vars.control|dev|key.0|score", VariableScope.Control)]
    public void ScopeOf_GivenAKey_FindsWhereItLives(string key, VariableScope expected)
    {
        // Act
        var scope = VariableCatalog.ScopeOf(key);

        // Assert
        Assert.That(scope, Is.EqualTo(expected));
    }

    [Test]
    public void Describe_AProfileVariable_ShowsItsNameWithTheProfile()
    {
        // Act
        var info = VariableCatalog.Describe("vars.profile|p1|score", "7");

        // Assert
        Assert.That(info.Name, Is.EqualTo("score (profile p1)"));
        Assert.That(info.Type, Is.EqualTo(VariableTypes.Number));
        Assert.That(info.IsUser, Is.True);
    }

    [Test]
    public void Remove_APublishedVariable_DropsItFromTheSnapshotAndTellsObservers()
    {
        // Arrange
        using var store = new VariableStore();
        store.Publish("vars.score", "5");
        var changes = new List<KeyValuePair<string, object?>>();
        using var subscription = store.Changes.Subscribe(changes.Add);

        // Act
        var removed = store.Remove("vars.score");

        // Assert
        Assert.That(removed, Is.True);
        Assert.That(store.Snapshot("vars."), Is.Empty);
        Assert.That(changes.Single().Value, Is.Null);
    }

    [Test]
    public void Remove_AnUnknownVariable_ReturnsFalse()
    {
        // Arrange
        using var store = new VariableStore();

        // Act
        var removed = store.Remove("vars.nothing");

        // Assert
        Assert.That(removed, Is.False);
    }
}

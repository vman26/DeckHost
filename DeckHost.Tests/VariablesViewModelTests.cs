using DeckHost.App.ViewModels;
using DeckHost.Contracts;
using DeckHost.Core.Devices;
using DeckHost.Core.State;

namespace DeckHost.Tests;

/// <summary>The Variables page: what it lists and how it creates, changes and removes variables.</summary>
public sealed class VariablesViewModelTests : IAsyncDisposable
{
    private const string Name = "score";
    private readonly VariableStore _store = new();
    private readonly DeviceHostService _host;

    public VariablesViewModelTests()
    {
        _host = new DeviceHostService([new FakeDevicePlugin()], [], _store);
    }

    public async ValueTask DisposeAsync()
    {
        await _host.DisposeAsync();
        _store.Dispose();
    }

    [Test]
    public void Constructor_WithNothingPublished_ListsDeckHostsOwnVariables()
    {
        // Act
        var page = new VariablesViewModel(_host);

        // Assert
        Assert.That(page.Rows.Select(static row => row.Key), Is.EquivalentTo(VariableCatalog.SystemKeys));
        Assert.That(page.Rows.All(static row => !row.IsUser), Is.True);
    }

    [Test]
    public async Task Create_WithANameAndAValue_AddsAGlobalUserVariable()
    {
        // Arrange
        var page = new VariablesViewModel(_host) { NewName = Name, NewValue = "5", NewType = VariableTypes.Number };

        // Act
        page.Create();
        await _host.WhenIdleAsync();
        page.Refresh();

        // Assert
        var row = page.Rows.Single(static row => row.IsUser);
        Assert.That(row.Key, Is.EqualTo(VariableKeys.UserVariable(Name)));
        Assert.That(row.Type, Is.EqualTo(VariableTypes.Number));
        Assert.That(row.Value, Is.EqualTo("5"));
    }

    [Test]
    public void Create_WithAValueThatIsNotOfTheChosenType_ExplainsAndAddsNothing()
    {
        // Arrange
        var page = new VariablesViewModel(_host) { NewName = Name, NewValue = "many", NewType = VariableTypes.Number };

        // Act
        page.Create();

        // Assert
        Assert.That(page.Status, Does.Contain("isn't a valid number"));
        Assert.That(page.Rows.Any(static row => row.IsUser), Is.False);
    }

    [TestCase("")]
    [TestCase("system.cpu")]
    [TestCase("a|b")]
    public void Create_WithAnUnusableName_AddsNothing(string name)
    {
        // Arrange
        var page = new VariablesViewModel(_host) { NewName = name, NewValue = "1" };

        // Act
        page.Create();

        // Assert
        Assert.That(page.Rows.Any(static row => row.IsUser), Is.False);
        Assert.That(page.Status, Is.Not.Empty);
    }

    [Test]
    public void StartAndStop_ThePageShownAndClosed_AskForTheSystemValuesAndGiveThemUp()
    {
        // Arrange
        var asked = new List<bool>();
        var page = new VariablesViewModel(_host, asked.Add);

        // Act
        page.Start();
        page.Stop();

        // Assert
        Assert.That(asked, Is.EqualTo(new[] { true, false }));
    }

    [Test]
    public async Task Delete_AUserVariable_RemovesItFromThePageAndTheStore()
    {
        // Arrange
        _store.Publish(VariableKeys.UserVariable(Name), "1");
        var page = new VariablesViewModel(_host);
        var row = page.Rows.Single(static row => row.IsUser);

        // Act
        row.DeleteCommand.Execute(null);
        await _host.WhenIdleAsync();

        // Assert
        Assert.That(page.Rows.Any(static row => row.IsUser), Is.False);
        Assert.That(_store.Snapshot("vars."), Is.Empty);
    }

    [Test]
    public async Task Refresh_WhileAValueIsBeingTyped_KeepsWhatWasTyped()
    {
        // Arrange
        _store.Publish(VariableKeys.UserVariable(Name), "1");
        var page = new VariablesViewModel(_host);
        var row = page.Rows.Single(static row => row.IsUser);
        row.EditValue = "typed";

        // Act
        _store.Publish(VariableKeys.UserVariable(Name), "2");
        await _host.WhenIdleAsync();
        page.Refresh();

        // Assert
        Assert.That(row.EditValue, Is.EqualTo("typed"));
        Assert.That(row.Value, Is.EqualTo("2"));
    }
}

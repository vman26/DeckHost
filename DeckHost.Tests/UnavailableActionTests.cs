using DeckHost.App.ViewModels;
using DeckHost.Contracts.Enums;
using DeckHost.Contracts.Models;
using DeckHost.Core.Plugins;

namespace DeckHost.Tests;

/// <summary>A control whose action's plugin is missing or off: how it's marked and what the card offers.</summary>
public class UnavailableActionTests
{
    private const string PluginId = "com.elgato.discord";
    private const string PluginName = "Discord";
    private const string ActionId = "com.elgato.discord.voice.mute";
    private const string ActionName = "Mute";

    private static ControlBinding Missing(string? pluginName = PluginName) => new(
        "key.0",
        ProfileActionKind.Single,
        new ActionReference(PluginId, pluginName ?? string.Empty, ActionId, ActionName, InputCapabilities.None, OutputCapabilities.None, ActionSettings.Empty),
        new ControlAppearance(),
        []);

    private static PluginEntry Installed(string id, bool enabled) =>
        new(id, id, "1.0", null, null, PluginKind.Application, false, enabled, true, true, 1, null);

    private static ControlItemViewModel Key() => new(new PhysicalControl("key.0", "Key 1", ControlType.Key, new Rect(0, 0, 10, 10),
        InputCapabilities.Press | InputCapabilities.Release, OutputCapabilities.ColorImage | OutputCapabilities.Text, 112, 112));

    [Test]
    public void ReasonFor_NoSuchPluginInstalled_IsNotInstalled()
    {
        // Act
        var reason = UnavailableAction.ReasonFor(Missing(), [Installed("com.example.other", enabled: true)]);

        // Assert
        Assert.That(reason, Is.EqualTo(UnavailableReason.NotInstalled));
    }

    [Test]
    public void ReasonFor_ThePluginIsTurnedOff_IsTurnedOff()
    {
        // Act
        var reason = UnavailableAction.ReasonFor(Missing(), [Installed(PluginId, enabled: false)]);

        // Assert
        Assert.That(reason, Is.EqualTo(UnavailableReason.TurnedOff));
    }

    [Test]
    public void ReasonFor_ThePluginIsOnWithoutTheAction_IsActionMissing()
    {
        // Act
        var reason = UnavailableAction.ReasonFor(Missing(), [Installed(PluginId.ToUpperInvariant(), enabled: true)]);

        // Assert
        Assert.That(reason, Is.EqualTo(UnavailableReason.ActionMissing));
    }

    [Test]
    public void Describe_ABindingWithoutAPluginName_NamesThePluginById()
    {
        // Act
        var text = UnavailableAction.Describe(Missing(pluginName: null));

        // Assert
        Assert.That(text, Is.EqualTo($"Needs the {PluginId} plugin"));
    }

    [Test]
    public void AsMissingPlugin_ABinding_AsksTheStoreForItsPluginAndAction()
    {
        // Act
        var plugin = UnavailableAction.AsMissingPlugin(Missing());

        // Assert
        Assert.That((plugin.Id, plugin.Name, plugin.ActionUuids.Single()), Is.EqualTo((PluginId, PluginName, ActionId)));
    }

    [Test]
    public void UnavailableBinding_Set_MarksTheControlAndSaysWhichPlugin()
    {
        // Arrange
        var key = Key();

        // Act
        key.UnavailableBinding = Missing();

        // Assert
        Assert.That((key.IsUnavailable, key.ToolTipText), Is.EqualTo((true, $"Key 1: Needs the {PluginName} plugin. Select it to install the plugin, or drop another action here.")));
    }

    [Test]
    public void IsEmptyDeck_OnlyAnUnavailableAction_IsFalse()
    {
        // Arrange
        var key = Key();
        key.UnavailableBinding = Missing();

        // Act
        var empty = MainViewModel.IsEmptyDeck([key]);

        // Assert
        Assert.That(empty, Is.False);
    }

    [Test]
    public void Card_APluginThatIsntInstalled_OffersTheStoreButNotTurnOn()
    {
        // Act
        var card = new UnavailableActionViewModel(Missing(), catalog: null);

        // Assert
        Assert.That((card.IsNotInstalled, card.TurnOnCommand.CanExecute(null), card.Title), Is.EqualTo((true, false, $"“{ActionName}” needs the {PluginName} plugin")));
    }

    [Test]
    public void Remove_OnTheCard_AsksToClearTheControl()
    {
        // Arrange
        var card = new UnavailableActionViewModel(Missing(), catalog: null);
        string? removed = null;
        card.RemoveRequested += (sender, _) => removed = ((UnavailableActionViewModel)sender!).ControlId;

        // Act
        card.RemoveCommand.Execute(null);

        // Assert
        Assert.That(removed, Is.EqualTo("key.0"));
    }
}

using DeckHost.App.SystemPlugins;
using DeckHost.App.ViewModels;
using DeckHost.Contracts.Enums;
using DeckHost.Contracts.Models;

namespace DeckHost.Tests;

/// <summary>Opening a control's editor and doing nothing builds the binding it was given.</summary>
public class EditorRoundTripTests
{
    private static readonly IReadOnlyList<ActionItemViewModel> Actions =
        [.. SystemPluginSet.Create().SelectMany(plugin => plugin.Actions.Select(action => ActionItemViewModel.Create(plugin.Id, plugin.Name, action)))];

    private static ControlItemViewModel Key() => new(new PhysicalControl("key.3", "Key 4", ControlType.Key, new Rect(0, 0, 10, 10),
        InputCapabilities.Press | InputCapabilities.Release | InputCapabilities.LongPress, OutputCapabilities.ColorImage | OutputCapabilities.Text, 112, 112));

    public static IEnumerable<TestCaseData> Bindings()
    {
        var play = Actions.First(action => action.Name == "Play / Pause");
        yield return new TestCaseData(new ControlBinding("key.3", ProfileActionKind.Single, play.ToActionReference(), new ControlAppearance() { ShowTile = true }, [])).SetName("Tile");
        yield return new TestCaseData(new ControlBinding("key.3", ProfileActionKind.Single, play.ToActionReference(), new ControlAppearance(), [])).SetName("Plain");
        var clock = Actions.First(action => action.Name == "Clock");
        yield return new TestCaseData(new ControlBinding("key.3", ProfileActionKind.Single, clock.ToActionReference(), new ControlAppearance(Text: "Time"), [])).SetName("Clock with text");
    }

    [TestCaseSource(nameof(Bindings))]
    public void An_untouched_editor_builds_the_same_binding(ControlBinding model)
    {
        var item = Actions.First(action => action.ToActionReference().ActionId == model.Action.ActionId);
        var editor = new ControlCustomizationViewModel(Key(), new ControlBindingViewModel(model, item, []), Actions, null);

        var built = editor.BuildBinding();

        var options = new System.Text.Json.JsonSerializerOptions { WriteIndented = true, Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };
        Assert.That(System.Text.Json.JsonSerializer.Serialize(built, options), Is.EqualTo(System.Text.Json.JsonSerializer.Serialize(model, options)));
        Assert.That(MainViewModel.SameBinding(model, built), Is.True);
    }

    [Test]
    public void No_settings_and_empty_settings_are_the_same_binding()
    {
        var play = Actions.First(action => action.Name == "Play / Pause").ToActionReference();
        var fromFile = new ControlBinding("key.3", ProfileActionKind.Single, play with { Settings = null }, new ControlAppearance(), []);
        var fromEditor = fromFile with { Action = play with { Settings = new ActionSettings([]) } };

        Assert.That(MainViewModel.SameBinding(fromFile, fromEditor), Is.True);
        Assert.That(MainViewModel.SameBinding(fromFile, fromEditor with { Action = play with { Settings = new ActionSettings([new("trigger", "release")]) } }), Is.False);
    }
}

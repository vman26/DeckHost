using DeckHost.App.ViewModels;
using DeckHost.Contracts;
using DeckHost.Contracts.Models;
using DeckHost.Core.State;

namespace DeckHost.Tests;

/// <summary>What the variable pickers offer. They share one static source, so these run one after the other.</summary>
[NonParallelizable]
public sealed class VariableSuggestionsTests : IDisposable
{
    public VariableSuggestionsTests()
    {
        VariableSuggestions.Source = () =>
        [
            VariableCatalog.Describe("system.cpu", "12"),
            VariableCatalog.Describe(VariableKeys.UserVariable("Mode"), "on"),
            VariableCatalog.Describe(VariableKeys.ProfileVariable("p1", "round"), "3"),
            VariableCatalog.Describe(VariableKeys.UserVariable("mode2"), "x"),
        ];
    }

    public void Dispose() => VariableSuggestions.Source = null;

    [Test]
    public void Names_WithUserAndSystemVariables_ListsOnlyUserNamesGlobalFirst()
    {
        var names = VariableSuggestions.Names();

        Assert.That(names, Is.EqualTo(new[] { "mode", "mode2", "round" }));
    }

    [Test]
    public void Keys_WithUserAndSystemVariables_ListsEveryKey()
    {
        var keys = VariableSuggestions.Keys();

        Assert.That(keys, Does.Contain("system.cpu").And.Contain("vars.mode"));
    }

    [Test]
    public void Names_WithoutASource_IsEmpty()
    {
        VariableSuggestions.Source = null;

        var names = VariableSuggestions.Names();

        Assert.That(names, Is.Empty);
    }

    [Test]
    public void Editor_ForAVariableSetting_OffersTheNamesAndKeepsATypedOne()
    {
        var definition = new ActionSettingDefinition("name", "Variable", ActionSettingKind.Variable);
        var editor = new SettingsEditorViewModel([definition], new ActionSettings([new("name", "brand-new")]));

        var field = (VariableSettingField)editor.Fields.Single();

        Assert.That(field.Suggestions, Does.Contain("mode"));
        Assert.That(field.Value, Is.EqualTo("brand-new"));
        Assert.That(editor.ToSettings().Get("name", string.Empty), Is.EqualTo("brand-new"));
    }
}

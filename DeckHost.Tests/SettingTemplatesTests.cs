using DeckHost.App.ViewModels;

namespace DeckHost.Tests;

/// <summary>Every page that shows generated setting fields has a template for every kind of field.</summary>
public sealed class SettingTemplatesTests
{
    [Fact]
    public void Every_settings_page_has_a_template_for_every_field_kind()
    {
        // Without a template a field shows "Not found: DeckHost.App.Views.<Field>".
        var kinds = typeof(SettingFieldViewModel).Assembly.GetTypes()
            .Where(static type => type.IsSubclassOf(typeof(SettingFieldViewModel)) && !type.IsAbstract)
            .Select(static type => type.Name)
            .ToList();
        var views = Directory.EnumerateFiles(Path.Combine(RepositoryRoot(), "DeckHost.App", "Views"), "*.axaml")
            .Where(static path => File.ReadAllText(path).Contains("DataType=\"vm:TextSettingField\"", StringComparison.Ordinal))
            .ToList();

        Assert.NotEmpty(views);
        Assert.Contains(kinds, static kind => kind == "SecretSettingField");
        foreach (var view in views)
        {
            var text = File.ReadAllText(view);
            var missing = kinds.Where(kind => !text.Contains($"DataType=\"vm:{kind}\"", StringComparison.Ordinal)).ToList();
            Assert.True(missing.Count == 0, $"{Path.GetFileName(view)} has no template for {string.Join(", ", missing)}.");
        }
    }

    private static string RepositoryRoot([System.Runtime.CompilerServices.CallerFilePath] string path = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path)!, ".."));
}

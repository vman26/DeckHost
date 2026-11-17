using DeckHost.App.ViewModels;

namespace DeckHost.Tests;

/// <summary>Every page that shows generated setting fields has a template for every kind of field.</summary>
public sealed class SettingTemplatesTests
{
    private const string TextFieldMarker = "DataType=\"vm:TextSettingField\"";
    private const string SecretField = "SecretSettingField";

    private static IReadOnlyList<string> FieldKinds() =>
    [
        .. typeof(SettingFieldViewModel).Assembly.GetTypes()
            .Where(static type => type.IsSubclassOf(typeof(SettingFieldViewModel)) && !type.IsAbstract)
            .Select(static type => type.Name),
    ];

    private static IReadOnlyList<string> ViewsWithFieldTemplates() =>
    [
        .. Directory.EnumerateFiles(Path.Combine(RepositoryRoot(), "DeckHost.App", "Views"), "*.axaml")
            .Where(static path => File.ReadAllText(path).Contains(TextFieldMarker, StringComparison.Ordinal)),
    ];

    // Without a template a field shows "Not found: DeckHost.App.Views.<Field>".
    private static IReadOnlyList<string> MissingTemplates() =>
    [
        .. ViewsWithFieldTemplates().SelectMany(view =>
        {
            var text = File.ReadAllText(view);
            return FieldKinds().Where(kind => !text.Contains($"DataType=\"vm:{kind}\"", StringComparison.Ordinal)).Select(kind => $"{Path.GetFileName(view)} has no template for {kind}");
        }),
    ];

    [Test]
    public void ViewsWithFieldTemplates_Repository_ListsSomePages()
    {
        // Arrange (the repository's views)

        // Act
        var views = ViewsWithFieldTemplates();

        // Assert
        Assert.That(views, Is.Not.Empty);
    }

    [Test]
    public void FieldKinds_Assembly_IncludesTheSecretField()
    {
        // Arrange (the app assembly)

        // Act
        var kinds = FieldKinds();

        // Assert
        Assert.That(kinds, Does.Contain(SecretField));
    }

    [Test]
    public void MissingTemplates_EverySettingsPage_HasATemplateForEveryFieldKind()
    {
        // Arrange (the repository's views)

        // Act
        var missing = MissingTemplates();

        // Assert
        Assert.That(missing, Is.Empty);
    }

    private static string RepositoryRoot([System.Runtime.CompilerServices.CallerFilePath] string path = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path)!, ".."));
}

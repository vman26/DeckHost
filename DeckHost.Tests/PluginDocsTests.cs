using DeckHost.App.ViewModels;
using DeckHost.App.Views;
using DeckHost.Core.Plugins;

namespace DeckHost.Tests;

/// <summary>A plugin's documentation page: its links, its README and local pictures.</summary>
public sealed class PluginDocsTests : IDisposable
{
    private const string Website = "https://example.com/docs";
    private const string Repository = "https://github.com/you/plugin";
    private const string OtherSite = "https://example.com";

    private readonly string _folder = Directory.CreateTempSubdirectory("deckhost-docs").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private PluginEntry Entry(string? url = null, string? repository = null, bool system = false) =>
        new("you.plugin", "You", "1.0.0", "You", null, PluginKind.Application, system, true, true, !system, 1, null,
            InstallDirectory: system ? null : _folder, Url: url, Repository: repository);

    [Test]
    public void BuildLinks_InstalledPluginWithAWebsiteAndASource_LinksTheWebsiteTheSourceAndTheFolder()
    {
        // Arrange
        var entry = Entry(Website, Repository);

        // Act
        var links = PluginDocsViewModel.BuildLinks(entry);

        // Assert
        Assert.That(links.Select(static link => link.Label), Is.EqualTo(new[] { "Website", "Source code", "Plugin folder" }));
    }

    [Test]
    public void RepositoryOf_GithubWebsite_IsTheSource()
    {
        // Arrange
        var entry = Entry(Repository);

        // Act
        var repository = PluginDocsViewModel.RepositoryOf(entry);

        // Assert
        Assert.That(repository, Is.EqualTo(Repository));
    }

    [Test]
    public void RepositoryOf_OtherWebsite_IsNull()
    {
        // Arrange
        var entry = Entry(OtherSite);

        // Act
        var repository = PluginDocsViewModel.RepositoryOf(entry);

        // Assert
        Assert.That(repository, Is.Null);
    }

    [Test]
    public void BuildLinks_BuiltInPlugin_LinksDeckHostsActionReference()
    {
        // Arrange
        var entry = Entry(system: true);

        // Act
        var link = Expect.Single(PluginDocsViewModel.BuildLinks(entry));

        // Assert
        Assert.That(link.Url, Does.EndWith("/reference/actions/"));
    }

    [Test]
    public void ReadmePath_FolderWithoutAReadme_IsNull()
    {
        // Arrange
        var entry = Entry();

        // Act
        var path = entry.ReadmePath;

        // Assert
        Assert.That(path, Is.Null);
    }

    [Test]
    public void ReadmePath_ReadmeInAnyCase_IsFound()
    {
        // Arrange
        File.WriteAllText(Path.Combine(_folder, "readme.MD"), "# Hi");

        // Act
        var path = Entry().ReadmePath;

        // Assert
        Assert.That(Path.GetFileName(path), Is.EqualTo("readme.MD"));
    }

    private Uri ReadmeFolder() => new(_folder + Path.DirectorySeparatorChar);

    [Test]
    public void Resolve_PictureInsideTheReadmesFolder_IsAFileUri()
    {
        // Arrange
        var folder = ReadmeFolder();

        // Act
        var resolved = MarkdownView.Resolve("img/a.png", folder);

        // Assert
        Assert.That(resolved, Is.EqualTo(new Uri(Path.Combine(_folder, "img", "a.png")).AbsoluteUri));
    }

    [TestCase("../secret.png")]
    [TestCase("/etc/passwd")]
    [TestCase("file:///etc/passwd")]
    public void Resolve_PictureOutsideTheReadmesFolder_IsNull(string source)
    {
        // Arrange
        var folder = ReadmeFolder();

        // Act
        var resolved = MarkdownView.Resolve(source, folder);

        // Assert
        Assert.That(resolved, Is.Null);
    }
}

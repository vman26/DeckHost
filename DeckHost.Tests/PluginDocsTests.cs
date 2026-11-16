using DeckHost.App.ViewModels;
using DeckHost.App.Views;
using DeckHost.Core.Plugins;

namespace DeckHost.Tests;

/// <summary>A plugin's documentation page: its links, its README and local pictures.</summary>
public sealed class PluginDocsTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("deckhost-docs").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private PluginEntry Entry(string? url = null, string? repository = null, bool system = false) =>
        new("you.plugin", "You", "1.0.0", "You", null, PluginKind.Application, system, true, true, !system, 1, null,
            InstallDirectory: system ? null : _folder, Url: url, Repository: repository);

    [Test]
    public void An_installed_plugin_links_its_website_its_source_and_its_folder()
    {
        var links = PluginDocsViewModel.BuildLinks(Entry("https://example.com/docs", "https://github.com/you/plugin"));

        Assert.That(links.Select(static link => link.Label), Is.EqualTo(new[] { "Website", "Source code", "Plugin folder" }));
    }

    [Test]
    public void A_github_website_is_the_source()
    {
        Assert.That(PluginDocsViewModel.RepositoryOf(Entry("https://github.com/you/plugin")), Is.EqualTo("https://github.com/you/plugin"));
        Assert.That(PluginDocsViewModel.RepositoryOf(Entry("https://example.com")), Is.Null);
    }

    [Test]
    public void A_built_in_plugin_links_DeckHosts_documentation()
    {
        var link = Expect.Single(PluginDocsViewModel.BuildLinks(Entry(system: true)));

        Assert.That(link.Url, Does.EndWith("/reference/actions/"));
    }

    [Test]
    public void The_readme_in_the_plugin_folder_is_found_whatever_its_case()
    {
        Assert.That(Entry().ReadmePath, Is.Null);
        File.WriteAllText(Path.Combine(_folder, "readme.MD"), "# Hi");

        Assert.That(Path.GetFileName(Entry().ReadmePath), Is.EqualTo("readme.MD"));
    }

    [Test]
    public void A_local_readme_shows_pictures_from_its_own_folder_only()
    {
        var folder = new Uri(_folder + Path.DirectorySeparatorChar);

        Assert.That(MarkdownView.Resolve("img/a.png", folder), Is.EqualTo(new Uri(Path.Combine(_folder, "img", "a.png")).AbsoluteUri));
        Assert.That(MarkdownView.Resolve("../secret.png", folder), Is.Null);
        Assert.That(MarkdownView.Resolve("/etc/passwd", folder), Is.Null);
        Assert.That(MarkdownView.Resolve("file:///etc/passwd", folder), Is.Null);
    }
}

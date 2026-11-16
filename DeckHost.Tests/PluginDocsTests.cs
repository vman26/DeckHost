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

    [Fact]
    public void An_installed_plugin_links_its_website_its_source_and_its_folder()
    {
        var links = PluginDocsViewModel.BuildLinks(Entry("https://example.com/docs", "https://github.com/you/plugin"));

        Assert.Equal(["Website", "Source code", "Plugin folder"], links.Select(static link => link.Label));
    }

    [Fact]
    public void A_github_website_is_the_source()
    {
        Assert.Equal("https://github.com/you/plugin", PluginDocsViewModel.RepositoryOf(Entry("https://github.com/you/plugin")));
        Assert.Null(PluginDocsViewModel.RepositoryOf(Entry("https://example.com")));
    }

    [Fact]
    public void A_built_in_plugin_links_DeckHosts_documentation()
    {
        var link = Assert.Single(PluginDocsViewModel.BuildLinks(Entry(system: true)));

        Assert.EndsWith("/reference/actions/", link.Url, StringComparison.Ordinal);
    }

    [Fact]
    public void The_readme_in_the_plugin_folder_is_found_whatever_its_case()
    {
        Assert.Null(Entry().ReadmePath);
        File.WriteAllText(Path.Combine(_folder, "readme.MD"), "# Hi");

        Assert.Equal("readme.MD", Path.GetFileName(Entry().ReadmePath));
    }

    [Fact]
    public void A_local_readme_shows_pictures_from_its_own_folder_only()
    {
        var folder = new Uri(_folder + Path.DirectorySeparatorChar);

        Assert.Equal(new Uri(Path.Combine(_folder, "img", "a.png")).AbsoluteUri, MarkdownView.Resolve("img/a.png", folder));
        Assert.Null(MarkdownView.Resolve("../secret.png", folder));
        Assert.Null(MarkdownView.Resolve("/etc/passwd", folder));
        Assert.Null(MarkdownView.Resolve("file:///etc/passwd", folder));
    }
}

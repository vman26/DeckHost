using DeckHost.App.Platform;
using DeckHost.App.ViewModels;
using DeckHost.Core.Updates;
using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace DeckHost.Tests;

public sealed class SemanticVersionTests
{
    [TestCase("1.4.0", "1.3.9")]
    [TestCase("1.4.0", "1.4.0-rc.1")]
    [TestCase("1.4.0-rc.1", "1.4.0-beta.5")]
    [TestCase("1.4.0-alpha.10", "1.4.0-alpha.9")]
    [TestCase("1.4.0-alpha.1", "1.4.0-alpha")]
    [TestCase("1.4.0-alpha.beta", "1.4.0-alpha.1")]
    [TestCase("2.0.0", "1.99.99")]
    [TestCase("v1.3.1", "1.3.0+4b09090")]
    public void Orders_versions_like_SemVer(string newer, string older)
    {
        Assert.That(SemanticVersion.Parse(newer) > SemanticVersion.Parse(older), Is.True);
        Assert.That(SemanticVersion.Parse(older) < SemanticVersion.Parse(newer), Is.True);
    }

    [Test]
    public void Build_metadata_and_the_v_prefix_are_ignored()
    {
        Assert.That(SemanticVersion.Parse("v1.3.0-alpha.3+4b09090"), Is.EqualTo(SemanticVersion.Parse("1.3.0-alpha.3")));
        Assert.That(SemanticVersion.Parse("1.3.0-alpha.3+4b09090").ToString(), Is.EqualTo("1.3.0-alpha.3"));
        Assert.That(SemanticVersion.Parse("1.3.0-alpha.3+abc").IsPrerelease, Is.True);
        Assert.That(SemanticVersion.Parse("1.2").ToString(), Is.EqualTo("1.2.0"));
    }

    [TestCase("")]
    [TestCase("latest")]
    [TestCase("1")]
    [TestCase("1.2.3.4")]
    [TestCase("1.2.x")]
    [TestCase("1.2.3-")]
    [TestCase("1.2.3-alpha..1")]
    public void Rejects_what_isnt_a_version(string text) => Assert.That(SemanticVersion.TryParse(text, out _), Is.False);
}

public sealed class UpdateCheckerTests : IDisposable
{
    private const string Releases = """
        [
          { "tag_name": "v1.5.0-alpha.2", "name": "DeckHost 1.5.0-alpha.2", "prerelease": true, "draft": false, "body": "Alpha notes", "html_url": "https://github.com/vman26/DeckHost/releases/tag/v1.5.0-alpha.2", "published_at": "2026-11-10T10:00:00Z", "assets": [] },
          { "tag_name": "v1.4.0", "name": "DeckHost 1.4.0", "prerelease": false, "draft": false, "body": "## What's Changed\n* Lights", "html_url": "https://github.com/vman26/DeckHost/releases/tag/v1.4.0", "published_at": "2026-11-01T10:00:00Z",
            "assets": [
              { "name": "DeckHost-1.4.0-win-x64-setup.exe", "browser_download_url": "https://example.com/setup.exe", "size": 10 },
              { "name": "DeckHost-1.4.0-win-x64.zip", "browser_download_url": "https://example.com/win.zip", "size": 10 },
              { "name": "DeckHost-1.4.0-linux-x64.tar.gz", "browser_download_url": "https://example.com/linux.tar.gz", "size": 10 },
              { "name": "DeckHost-1.4.0-osx-arm64.zip", "browser_download_url": "https://example.com/mac.zip", "size": 10 },
              { "name": "SHA256SUMS.txt", "browser_download_url": "https://example.com/sums", "size": 10 } ] },
          { "tag_name": "v1.3.1", "name": "", "prerelease": false, "draft": false, "body": "", "published_at": "2026-10-20T10:00:00Z", "assets": [] },
          { "tag_name": "v1.6.0", "name": "Draft", "prerelease": false, "draft": true, "body": "", "assets": [] },
          { "tag_name": "nightly", "name": "Nightly", "prerelease": true, "draft": false, "body": "", "assets": [] },
          { "tag_name": "v1.3.0", "name": "DeckHost 1.3.0", "prerelease": false, "draft": false, "body": "Old", "assets": [] }
        ]
        """;

    private readonly string _directory = Directory.CreateTempSubdirectory("deckhost-update-tests").FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Test]
    public void Release_notes_stop_at_the_end_marker()
    {
        var body = "### Added\n\n- Knob lights\n\n## Downloads\n\n| a | b |\n\n<!-- deckhost:end-of-notes -->\n\n### Checking the files\n\n<details>PRs</details>";

        var notes = UpdateChecker.UserNotes(body);

        Assert.That(notes, Does.StartWith("### Added"));
        Assert.That(notes, Does.EndWith("| a | b |"));
        Assert.That(notes, Does.Not.Contain("Checking the files"));
        Assert.That(UpdateChecker.UserNotes("  Only notes\n"), Is.EqualTo("Only notes"));
        Assert.That(UpdateChecker.UserNotes(null), Is.Empty);
    }

    [Test]
    public void The_release_notes_script_writes_the_same_marker()
    {
        var script = Path.Combine(RepositoryRoot(), "scripts", "release-notes.py");

        Assert.That(File.ReadAllText(script), Does.Contain($"END_MARKER = \"{UpdateChecker.EndOfNotesMarker}\""));
    }

    [Test]
    public async Task Finds_the_releases_since_the_running_version()
    {
        var checker = new UpdateChecker(new HttpClient(new FakeHandler(request =>
            request.RequestUri!.AbsoluteUri.StartsWith("https://api.github.com/repos/vman26/DeckHost/releases", StringComparison.Ordinal) ? Encoding.UTF8.GetBytes(Releases) : null)));

        var result = await checker.CheckAsync("1.3.0", includePrereleases: false);

        Assert.That(result.IsUpdateAvailable, Is.True);
        Assert.That(result.Newer.Select(release => release.Version.ToString()), Is.EqualTo(new[] { "1.4.0", "1.3.1" }));
        Assert.That(result.Latest!.Version.ToString(), Is.EqualTo("1.4.0"));

        // The changelog has every newer release, newest first, with a name for the unnamed one.
        var changelog = result.Changelog;
        Assert.That(changelog.IndexOf("## DeckHost 1.4.0", StringComparison.Ordinal) < changelog.IndexOf("## DeckHost 1.3.1", StringComparison.Ordinal), Is.True);
        Assert.That(changelog, Does.Contain("* Lights"));
        Assert.That(changelog, Does.Contain("No release notes."));
        Assert.That(changelog, Does.Not.Contain("Old"));
    }

    [Test]
    public void Prereleases_count_only_when_asked_and_drafts_never()
    {
        var releases = UpdateChecker.ParseReleases(Releases);
        Assert.That(releases.Any(release => release.Tag is "v1.6.0" or "nightly"), Is.False);

        var stable = UpdateChecker.Evaluate(SemanticVersion.Parse("1.4.0"), releases, includePrereleases: false);
        Assert.That(stable.IsUpdateAvailable, Is.False);

        var preview = UpdateChecker.Evaluate(SemanticVersion.Parse("1.4.0"), releases, includePrereleases: true);
        Assert.That(Expect.Single(preview.Newer).Version.ToString(), Is.EqualTo("1.5.0-alpha.2"));

        // A pre-release build is offered its release.
        var alpha = UpdateChecker.Evaluate(SemanticVersion.Parse("1.4.0-alpha.7+4b09090"), releases, includePrereleases: false);
        Assert.That(alpha.Newer[0].Version.ToString(), Is.EqualTo("1.4.0"));
    }

    [TestCase("win-x64", "DeckHost-1.4.0-win-x64-setup.exe")]
    [TestCase("linux-x64", "DeckHost-1.4.0-linux-x64.tar.gz")]
    [TestCase("osx-arm64", "DeckHost-1.4.0-osx-arm64.zip")]
    [TestCase("linux-arm64", null)]
    public void Picks_the_package_for_the_platform(string rid, string? expected)
    {
        var release = UpdateChecker.ParseReleases(Releases).Single(release => release.Tag == "v1.4.0");
        Assert.That(UpdateChecker.SelectAsset(release, rid)?.Name, Is.EqualTo(expected));
    }

    [Test]
    public void An_AppImage_is_offered_the_AppImage_and_only_when_the_release_has_one()
    {
        var release = Release(
            ("DeckHost-1.4.0-linux-x64.tar.gz", "https://example.com/linux.tar.gz"),
            ("DeckHost-1.4.0-linux-x86_64.AppImage", "https://example.com/linux.AppImage"));
        Assert.That(UpdateChecker.SelectAsset(release, "linux-x64", appImage: true)?.Name, Is.EqualTo("DeckHost-1.4.0-linux-x86_64.AppImage"));
        // The tar.gz stays the package for an unpacked install, and for an AppImage of an older release.
        Assert.That(UpdateChecker.SelectAsset(release, "linux-x64")?.Name, Is.EqualTo("DeckHost-1.4.0-linux-x64.tar.gz"));
        var older = Release(("DeckHost-1.4.0-linux-x64.tar.gz", "https://example.com/linux.tar.gz"));
        Assert.That(UpdateChecker.SelectAsset(older, "linux-x64", appImage: true)?.Name, Is.EqualTo("DeckHost-1.4.0-linux-x64.tar.gz"));
    }

    [Test]
    public async Task Reports_problems_plainly()
    {
        var offline = new UpdateChecker(new HttpClient(new FakeHandler(_ => throw new HttpRequestException("No such host"))));
        Assert.That((Assert.ThrowsAsync<UpdateException>(() => offline.CheckAsync("1.3.0", false))).Message, Does.Contain("Couldn't reach GitHub"));

        var limited = new UpdateChecker(new HttpClient(new FakeHandler(_ => null, HttpStatusCode.Forbidden, ("X-RateLimit-Remaining", "0"))));
        Assert.That((Assert.ThrowsAsync<UpdateException>(() => limited.CheckAsync("1.3.0", false))).Message, Does.Contain("try again in an hour"));

        Assert.ThrowsAsync<UpdateException>(() => offline.CheckAsync("dev build", false));
    }

    [Test]
    public async Task Downloads_and_verifies_the_package()
    {
        var package = Encoding.UTF8.GetBytes("the new DeckHost");
        var hash = Convert.ToHexStringLower(SHA256.HashData(package));
        var release = Release(("DeckHost-1.4.0-linux-x64.tar.gz", "https://example.com/linux.tar.gz"), ("SHA256SUMS.txt", "https://example.com/sums"));
        var checker = new UpdateChecker(new HttpClient(new FakeHandler(request => request.RequestUri!.AbsolutePath switch
        {
            "/sums" => Encoding.UTF8.GetBytes($"{new string('0', 64)}  other.zip\n{hash}  DeckHost-1.4.0-linux-x64.tar.gz\n"),
            "/linux.tar.gz" => package,
            _ => null,
        })));
        var reported = new List<double>();

        var path = await checker.DownloadAsync(release, release.Assets[0], _directory, new SyncProgress(reported.Add));

        Assert.That(await File.ReadAllBytesAsync(path), Is.EqualTo(package));
        Assert.That(reported[^1], Is.EqualTo(1d));
    }

    [Test]
    public async Task A_damaged_download_is_deleted()
    {
        var release = Release(("DeckHost-1.4.0-linux-x64.tar.gz", "https://example.com/linux.tar.gz"), ("SHA256SUMS.txt", "https://example.com/sums"));
        var checker = new UpdateChecker(new HttpClient(new FakeHandler(request => request.RequestUri!.AbsolutePath == "/sums"
            ? Encoding.UTF8.GetBytes($"{new string('a', 64)}  DeckHost-1.4.0-linux-x64.tar.gz\n")
            : "tampered"u8.ToArray())));

        var error = Assert.ThrowsAsync<UpdateException>(() => checker.DownloadAsync(release, release.Assets[0], _directory, null));

        Assert.That(error.Message, Does.Contain("damaged"));
        Assert.That(Directory.GetFiles(_directory), Is.Empty);
    }

    [Test]
    public void Reads_sha256sum_output()
    {
        var hash = new string('b', 64);
        Assert.That(UpdateChecker.FindChecksum($"{hash} *DeckHost-setup.exe\r\n", "DeckHost-setup.exe"), Is.EqualTo(hash));
        Assert.That(UpdateChecker.FindChecksum($"{hash}  DeckHost-setup.exe.sig\n", "DeckHost-setup.exe"), Is.Null);
    }

    private static ReleaseInfo Release(params (string Name, string Url)[] assets) => new(
        "v1.4.0", SemanticVersion.Parse("1.4.0"), "DeckHost 1.4.0", string.Empty, string.Empty, false, null,
        [.. assets.Select(asset => new ReleaseAsset(asset.Name, asset.Url, 0))]);

    private static string RepositoryRoot([System.Runtime.CompilerServices.CallerFilePath] string path = "") =>
        Path.GetDirectoryName(Path.GetDirectoryName(path))!;

    private sealed class SyncProgress(Action<double> report) : IProgress<double>
    {
        public void Report(double value) => report(value);
    }

    internal sealed class FakeHandler(Func<HttpRequestMessage, byte[]?> respond, HttpStatusCode failure = HttpStatusCode.NotFound, params (string Name, string Value)[] headers) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = respond(request);
            var response = body is null ? new HttpResponseMessage(failure) : new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) };
            foreach (var (name, value) in headers)
            {
                response.Headers.Add(name, value);
            }

            return Task.FromResult(response);
        }
    }
}

public sealed class UpdateViewModelTests
{
    private const string Releases = """
        [ { "tag_name": "v1.4.0", "name": "DeckHost 1.4.0", "prerelease": false, "draft": false, "body": "New", "assets": [] },
          { "tag_name": "v1.4.1-beta.1", "name": "DeckHost 1.4.1-beta.1", "prerelease": true, "draft": false, "body": "Beta", "assets": [] } ]
        """;

    private AppSettingsData _settings = new();

    [Test]
    public async Task Announces_an_update_until_it_is_skipped()
    {
        var updates = Create("1.3.0");

        await updates.CheckAsync(manual: false);

        Assert.That(updates.IsUpdateAvailable, Is.True);
        Assert.That(updates.ShowBanner, Is.True);
        Assert.That(updates.AvailableTitle, Is.EqualTo("DeckHost 1.4.0 is available"));
        Assert.That(_settings.LastUpdateCheck, Is.Not.Null);

        updates.SkipCommand.Execute(null);
        Assert.That(updates.ShowBanner, Is.False);
        Assert.That(_settings.SkippedVersion, Is.EqualTo("1.4.0"));

        // Next start: still skipped.
        var restarted = Create("1.3.0");
        await restarted.CheckAsync(manual: false);
        Assert.That(restarted.IsSkipped, Is.True);
        Assert.That(restarted.ShowBanner, Is.False);

        // Checking by hand shows it again.
        await restarted.CheckAsync(manual: true);
        Assert.That(restarted.ShowBanner, Is.True);
        Assert.That(_settings.SkippedVersion, Is.Null);
    }

    [Test]
    public async Task Later_hides_the_notice_for_this_session_only()
    {
        var updates = Create("1.3.0");
        await updates.CheckAsync(manual: false);

        updates.LaterCommand.Execute(null);

        Assert.That(updates.ShowBanner, Is.False);
        Assert.That(updates.IsUpdateAvailable, Is.True);
        Assert.That(_settings.SkippedVersion, Is.Null);
    }

    [Test]
    public async Task Prereleases_are_offered_to_prerelease_builds_by_default()
    {
        var stable = Create("1.4.0");
        await stable.CheckAsync(manual: true);
        Assert.That(stable.IncludePrereleases, Is.False);
        Assert.That(stable.IsUpdateAvailable, Is.False);
        Assert.That(stable.Status, Does.StartWith("DeckHost is up to date"));

        var beta = Create("1.4.0-alpha.3+4b09090");
        Assert.That(beta.IncludePrereleases, Is.True);
        await beta.CheckAsync(manual: true);
        Assert.That(beta.Latest!.Version.ToString(), Is.EqualTo("1.4.1-beta.1"));
        Assert.That(beta.AvailableSummary, Is.EqualTo("You have 1.4.0-alpha.3+4b09090. 2 releases since then."));
    }

    [Test]
    public async Task Settings_are_saved_and_errors_reported_only_when_asked()
    {
        var updates = new UpdateViewModel(
            new UpdateChecker(new HttpClient(new UpdateCheckerTests.FakeHandler(_ => throw new HttpRequestException("offline")))),
            "1.3.0",
            () => _settings,
            settings => _settings = settings);

        updates.CheckAutomatically = false;
        updates.IncludePrereleases = true;
        updates.InstallAutomatically = true;
        Assert.That(_settings.CheckForUpdates, Is.False);
        Assert.That(_settings.IncludePrereleases, Is.True);
        Assert.That(_settings.InstallUpdatesAutomatically, Is.True);
        Assert.That(updates.IsReadyToInstallOnExit, Is.False);

        var before = updates.Status;
        await updates.CheckAsync(manual: false);
        Assert.That(updates.Status, Is.EqualTo(before));

        await updates.CheckAsync(manual: true);
        Assert.That(updates.Status, Does.Contain("Couldn't reach GitHub"));
    }

    private UpdateViewModel Create(string version) => new(
        new UpdateChecker(new HttpClient(new UpdateCheckerTests.FakeHandler(_ => Encoding.UTF8.GetBytes(Releases)))),
        version,
        () => _settings,
        settings => _settings = settings);
}

public sealed class UpdateInstallerTests
{
    [Test]
    public void Quotes_paths_for_PowerShell() =>
        Assert.That(UpdateInstaller.Quote("C:\\Users\\O'Brien\\setup.exe"), Is.EqualTo("'C:\\Users\\O''Brien\\setup.exe'"));

    [TestCase("DeckHost.App", true)]
    [TestCase("none", false)]
    public void The_swap_script_replaces_the_install_and_starts_it_unless_told_not_to(string launch, bool startsAgain)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var root = Directory.CreateTempSubdirectory("deckhost-swap").FullName;
        try
        {
            var target = Path.Combine(root, "deckhost");
            var source = Path.Combine(root, "new", "DeckHost");
            Directory.CreateDirectory(target);
            Directory.CreateDirectory(source);
            File.WriteAllText(Path.Combine(target, "old-only.dll"), "old");
            File.WriteAllText(Path.Combine(target, "version.txt"), "1.3.0");
            File.WriteAllText(Path.Combine(source, "version.txt"), "1.4.0");
            var started = Path.Combine(root, "started.txt");
            File.WriteAllText(Path.Combine(source, "DeckHost.App"), $"#!/bin/sh\necho started > '{started}'\n");
            File.SetUnixFileMode(Path.Combine(source, "DeckHost.App"), UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            var script = Path.Combine(root, "update.sh");
            File.WriteAllText(script, UpdateInstaller.SwapScriptText.Replace("\r\n", "\n", StringComparison.Ordinal) + "\n");

            // A process id that has exited: the script doesn't wait.
            // (/bin/sh is everywhere; /bin/true isn't on macOS.)
            using var exited = Process.Start(new ProcessStartInfo("/bin/sh") { ArgumentList = { "-c", "exit 0" } })!;
            exited.WaitForExit();
            using var helper = Process.Start(new ProcessStartInfo("/bin/sh") { ArgumentList = { script, exited.Id.ToString(System.Globalization.CultureInfo.InvariantCulture), source, target, launch } })!;
            Assert.That(helper.WaitForExit(10_000), Is.True);

            Assert.That(File.ReadAllText(Path.Combine(target, "version.txt")), Is.EqualTo("1.4.0"));
            Assert.That(File.Exists(Path.Combine(target, "old-only.dll")), Is.False);
            Assert.That(Directory.Exists(target + ".update-backup"), Is.False);
            var end = DateTime.UtcNow.AddSeconds(startsAgain ? 10 : 1);
            while (!File.Exists(started) && DateTime.UtcNow < end)
            {
                Thread.Sleep(50);
            }

            Assert.That(File.Exists(started), Is.EqualTo(startsAgain));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}

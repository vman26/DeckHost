using DeckHost.App.Platform;
using DeckHost.App.ViewModels;
using DeckHost.Core.Updates;
using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Xunit;

namespace DeckHost.Tests;

public sealed class SemanticVersionTests
{
    [Theory]
    [InlineData("1.4.0", "1.3.9")]
    [InlineData("1.4.0", "1.4.0-rc.1")]
    [InlineData("1.4.0-rc.1", "1.4.0-beta.5")]
    [InlineData("1.4.0-alpha.10", "1.4.0-alpha.9")]
    [InlineData("1.4.0-alpha.1", "1.4.0-alpha")]
    [InlineData("1.4.0-alpha.beta", "1.4.0-alpha.1")]
    [InlineData("2.0.0", "1.99.99")]
    [InlineData("v1.3.1", "1.3.0+4b09090")]
    public void Orders_versions_like_SemVer(string newer, string older)
    {
        Assert.True(SemanticVersion.Parse(newer) > SemanticVersion.Parse(older));
        Assert.True(SemanticVersion.Parse(older) < SemanticVersion.Parse(newer));
    }

    [Fact]
    public void Build_metadata_and_the_v_prefix_are_ignored()
    {
        Assert.Equal(SemanticVersion.Parse("1.3.0-alpha.3"), SemanticVersion.Parse("v1.3.0-alpha.3+4b09090"));
        Assert.Equal("1.3.0-alpha.3", SemanticVersion.Parse("1.3.0-alpha.3+4b09090").ToString());
        Assert.True(SemanticVersion.Parse("1.3.0-alpha.3+abc").IsPrerelease);
        Assert.Equal("1.2.0", SemanticVersion.Parse("1.2").ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("latest")]
    [InlineData("1")]
    [InlineData("1.2.3.4")]
    [InlineData("1.2.x")]
    [InlineData("1.2.3-")]
    [InlineData("1.2.3-alpha..1")]
    public void Rejects_what_isnt_a_version(string text) => Assert.False(SemanticVersion.TryParse(text, out _));
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

    [Fact]
    public async Task Finds_the_releases_since_the_running_version()
    {
        var checker = new UpdateChecker(new HttpClient(new FakeHandler(request =>
            request.RequestUri!.AbsoluteUri.StartsWith("https://api.github.com/repos/vman26/DeckHost/releases", StringComparison.Ordinal) ? Encoding.UTF8.GetBytes(Releases) : null)));

        var result = await checker.CheckAsync("1.3.0", includePrereleases: false);

        Assert.True(result.IsUpdateAvailable);
        Assert.Equal(["1.4.0", "1.3.1"], result.Newer.Select(release => release.Version.ToString()));
        Assert.Equal("1.4.0", result.Latest!.Version.ToString());

        // The changelog has every newer release, newest first, with a name for the unnamed one.
        var changelog = result.Changelog;
        Assert.True(changelog.IndexOf("## DeckHost 1.4.0", StringComparison.Ordinal) < changelog.IndexOf("## DeckHost 1.3.1", StringComparison.Ordinal));
        Assert.Contains("* Lights", changelog);
        Assert.Contains("No release notes.", changelog);
        Assert.DoesNotContain("Old", changelog);
    }

    [Fact]
    public void Prereleases_count_only_when_asked_and_drafts_never()
    {
        var releases = UpdateChecker.ParseReleases(Releases);
        Assert.DoesNotContain(releases, release => release.Tag is "v1.6.0" or "nightly");

        var stable = UpdateChecker.Evaluate(SemanticVersion.Parse("1.4.0"), releases, includePrereleases: false);
        Assert.False(stable.IsUpdateAvailable);

        var preview = UpdateChecker.Evaluate(SemanticVersion.Parse("1.4.0"), releases, includePrereleases: true);
        Assert.Equal("1.5.0-alpha.2", Assert.Single(preview.Newer).Version.ToString());

        // A pre-release build is offered its release.
        var alpha = UpdateChecker.Evaluate(SemanticVersion.Parse("1.4.0-alpha.7+4b09090"), releases, includePrereleases: false);
        Assert.Equal("1.4.0", alpha.Newer[0].Version.ToString());
    }

    [Theory]
    [InlineData("win-x64", "DeckHost-1.4.0-win-x64-setup.exe")]
    [InlineData("linux-x64", "DeckHost-1.4.0-linux-x64.tar.gz")]
    [InlineData("osx-arm64", "DeckHost-1.4.0-osx-arm64.zip")]
    [InlineData("linux-arm64", null)]
    public void Picks_the_package_for_the_platform(string rid, string? expected)
    {
        var release = UpdateChecker.ParseReleases(Releases).Single(release => release.Tag == "v1.4.0");
        Assert.Equal(expected, UpdateChecker.SelectAsset(release, rid)?.Name);
    }

    [Fact]
    public async Task Reports_problems_plainly()
    {
        var offline = new UpdateChecker(new HttpClient(new FakeHandler(_ => throw new HttpRequestException("No such host"))));
        Assert.Contains("Couldn't reach GitHub", (await Assert.ThrowsAsync<UpdateException>(() => offline.CheckAsync("1.3.0", false))).Message);

        var limited = new UpdateChecker(new HttpClient(new FakeHandler(_ => null, HttpStatusCode.Forbidden, ("X-RateLimit-Remaining", "0"))));
        Assert.Contains("try again in an hour", (await Assert.ThrowsAsync<UpdateException>(() => limited.CheckAsync("1.3.0", false))).Message);

        await Assert.ThrowsAsync<UpdateException>(() => offline.CheckAsync("dev build", false));
    }

    [Fact]
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

        Assert.Equal(package, await File.ReadAllBytesAsync(path));
        Assert.Equal(1d, reported[^1]);
    }

    [Fact]
    public async Task A_damaged_download_is_deleted()
    {
        var release = Release(("DeckHost-1.4.0-linux-x64.tar.gz", "https://example.com/linux.tar.gz"), ("SHA256SUMS.txt", "https://example.com/sums"));
        var checker = new UpdateChecker(new HttpClient(new FakeHandler(request => request.RequestUri!.AbsolutePath == "/sums"
            ? Encoding.UTF8.GetBytes($"{new string('a', 64)}  DeckHost-1.4.0-linux-x64.tar.gz\n")
            : "tampered"u8.ToArray())));

        var error = await Assert.ThrowsAsync<UpdateException>(() => checker.DownloadAsync(release, release.Assets[0], _directory, null));

        Assert.Contains("damaged", error.Message);
        Assert.Empty(Directory.GetFiles(_directory));
    }

    [Fact]
    public void Reads_sha256sum_output()
    {
        var hash = new string('b', 64);
        Assert.Equal(hash, UpdateChecker.FindChecksum($"{hash} *DeckHost-setup.exe\r\n", "DeckHost-setup.exe"));
        Assert.Null(UpdateChecker.FindChecksum($"{hash}  DeckHost-setup.exe.sig\n", "DeckHost-setup.exe"));
    }

    private static ReleaseInfo Release(params (string Name, string Url)[] assets) => new(
        "v1.4.0", SemanticVersion.Parse("1.4.0"), "DeckHost 1.4.0", string.Empty, string.Empty, false, null,
        [.. assets.Select(asset => new ReleaseAsset(asset.Name, asset.Url, 0))]);

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

    [Fact]
    public async Task Announces_an_update_until_it_is_skipped()
    {
        var updates = Create("1.3.0");

        await updates.CheckAsync(manual: false);

        Assert.True(updates.IsUpdateAvailable);
        Assert.True(updates.ShowBanner);
        Assert.Equal("DeckHost 1.4.0 is available", updates.AvailableTitle);
        Assert.NotNull(_settings.LastUpdateCheck);

        updates.SkipCommand.Execute(null);
        Assert.False(updates.ShowBanner);
        Assert.Equal("1.4.0", _settings.SkippedVersion);

        // Next start: still skipped.
        var restarted = Create("1.3.0");
        await restarted.CheckAsync(manual: false);
        Assert.True(restarted.IsSkipped);
        Assert.False(restarted.ShowBanner);

        // Checking by hand shows it again.
        await restarted.CheckAsync(manual: true);
        Assert.True(restarted.ShowBanner);
        Assert.Null(_settings.SkippedVersion);
    }

    [Fact]
    public async Task Later_hides_the_notice_for_this_session_only()
    {
        var updates = Create("1.3.0");
        await updates.CheckAsync(manual: false);

        updates.LaterCommand.Execute(null);

        Assert.False(updates.ShowBanner);
        Assert.True(updates.IsUpdateAvailable);
        Assert.Null(_settings.SkippedVersion);
    }

    [Fact]
    public async Task Prereleases_are_offered_to_prerelease_builds_by_default()
    {
        var stable = Create("1.4.0");
        await stable.CheckAsync(manual: true);
        Assert.False(stable.IncludePrereleases);
        Assert.False(stable.IsUpdateAvailable);
        Assert.StartsWith("DeckHost is up to date", stable.Status);

        var beta = Create("1.4.0-alpha.3+4b09090");
        Assert.True(beta.IncludePrereleases);
        await beta.CheckAsync(manual: true);
        Assert.Equal("1.4.1-beta.1", beta.Latest!.Version.ToString());
        Assert.Equal("You have 1.4.0-alpha.3+4b09090. 2 releases since then.", beta.AvailableSummary);
    }

    [Fact]
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
        Assert.False(_settings.CheckForUpdates);
        Assert.True(_settings.IncludePrereleases);
        Assert.True(_settings.InstallUpdatesAutomatically);
        Assert.False(updates.IsReadyToInstallOnExit);

        var before = updates.Status;
        await updates.CheckAsync(manual: false);
        Assert.Equal(before, updates.Status);

        await updates.CheckAsync(manual: true);
        Assert.Contains("Couldn't reach GitHub", updates.Status);
    }

    private UpdateViewModel Create(string version) => new(
        new UpdateChecker(new HttpClient(new UpdateCheckerTests.FakeHandler(_ => Encoding.UTF8.GetBytes(Releases)))),
        version,
        () => _settings,
        settings => _settings = settings);
}

public sealed class UpdateInstallerTests
{
    [Fact]
    public void Quotes_paths_for_PowerShell() =>
        Assert.Equal("'C:\\Users\\O''Brien\\setup.exe'", UpdateInstaller.Quote("C:\\Users\\O'Brien\\setup.exe"));

    [Theory]
    [InlineData("DeckHost.App", true)]
    [InlineData("none", false)]
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
            Assert.True(helper.WaitForExit(10_000));

            Assert.Equal("1.4.0", File.ReadAllText(Path.Combine(target, "version.txt")));
            Assert.False(File.Exists(Path.Combine(target, "old-only.dll")));
            Assert.False(Directory.Exists(target + ".update-backup"));
            var end = DateTime.UtcNow.AddSeconds(startsAgain ? 10 : 1);
            while (!File.Exists(started) && DateTime.UtcNow < end)
            {
                Thread.Sleep(50);
            }

            Assert.Equal(startsAgain, File.Exists(started));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}

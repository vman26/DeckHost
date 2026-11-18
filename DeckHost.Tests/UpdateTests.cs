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
    public void CompareOperators_TwoVersions_OrderThemLikeSemVer(string newer, string older)
    {
        // Act
        var newerIsGreater = SemanticVersion.Parse(newer) > SemanticVersion.Parse(older);
        var olderIsLess = SemanticVersion.Parse(older) < SemanticVersion.Parse(newer);

        // Assert
        Assert.That(newerIsGreater, Is.True);
        Assert.That(olderIsLess, Is.True);
    }

    [Test]
    public void Parse_AVersionWithTheVPrefixAndBuildMetadata_EqualsTheBareVersion()
    {
        // Act
        var parsed = SemanticVersion.Parse("v1.3.0-alpha.3+4b09090");

        // Assert
        Assert.That(parsed, Is.EqualTo(SemanticVersion.Parse("1.3.0-alpha.3")));
    }

    [Test]
    public void ToString_AVersionWithBuildMetadata_LeavesTheMetadataOut()
    {
        // Act
        var text = SemanticVersion.Parse("1.3.0-alpha.3+4b09090").ToString();

        // Assert
        Assert.That(text, Is.EqualTo("1.3.0-alpha.3"));
    }

    [Test]
    public void IsPrerelease_AVersionWithAPrereleaseTag_IsTrue()
    {
        // Act
        var isPrerelease = SemanticVersion.Parse("1.3.0-alpha.3+abc").IsPrerelease;

        // Assert
        Assert.That(isPrerelease, Is.True);
    }

    [Test]
    public void Parse_ATwoPartVersion_GetsAZeroPatch()
    {
        // Act
        var text = SemanticVersion.Parse("1.2").ToString();

        // Assert
        Assert.That(text, Is.EqualTo("1.2.0"));
    }

    [TestCase("")]
    [TestCase("latest")]
    [TestCase("1")]
    [TestCase("1.2.3.4")]
    [TestCase("1.2.x")]
    [TestCase("1.2.3-")]
    [TestCase("1.2.3-alpha..1")]
    public void TryParse_TextThatIsNoVersion_IsRefused(string text)
    {
        // Act
        var parsed = SemanticVersion.TryParse(text, out _);

        // Assert
        Assert.That(parsed, Is.False);
    }
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

    private const string ApiReleasesUrl = "https://api.github.com/repos/vman26/DeckHost/releases";
    private const string LinuxTarName = "DeckHost-1.4.0-linux-x64.tar.gz";
    private const string LinuxTarUrl = "https://example.com/linux.tar.gz";
    private const string AppImageName = "DeckHost-1.4.0-linux-x86_64.AppImage";
    private const string SumsUrl = "https://example.com/sums";
    private const string SumsName = "SHA256SUMS.txt";
    private const string RunningVersion = "1.3.0";
    private const int DownloadWaitMilliseconds = 10_000;

    private static UpdateChecker CheckerAnswering(string releases) => new(new HttpClient(new FakeHandler(request =>
        request.RequestUri!.AbsoluteUri.StartsWith(ApiReleasesUrl, StringComparison.Ordinal) ? Encoding.UTF8.GetBytes(releases) : null)));

    // ---------------------------------------------------------------- release notes

    private const string NotesWithMarker = "### Added\n\n- Knob lights\n\n## Downloads\n\n| a | b |\n\n<!-- deckhost:end-of-notes -->\n\n### Checking the files\n\n<details>PRs</details>";

    [Test]
    public void UserNotes_NotesWithTheEndMarker_StopAtTheMarker()
    {
        // Act
        var notes = UpdateChecker.UserNotes(NotesWithMarker);

        // Assert
        Assert.That(notes, Does.StartWith("### Added"));
        Assert.That(notes, Does.EndWith("| a | b |"));
        Assert.That(notes, Does.Not.Contain("Checking the files"));
    }

    [Test]
    public void UserNotes_NotesWithoutAMarker_AreTrimmed()
    {
        // Act
        var notes = UpdateChecker.UserNotes("  Only notes\n");

        // Assert
        Assert.That(notes, Is.EqualTo("Only notes"));
    }

    [Test]
    public void UserNotes_NoBody_IsEmpty()
    {
        // Act
        var notes = UpdateChecker.UserNotes(null);

        // Assert
        Assert.That(notes, Is.Empty);
    }

    [Test]
    public void EndOfNotesMarker_TheReleaseNotesScript_WritesTheSameMarker()
    {
        // Arrange
        var script = Path.Combine(RepositoryRoot(), "scripts", "release-notes.py");

        // Act
        var text = File.ReadAllText(script);

        // Assert
        Assert.That(text, Does.Contain($"END_MARKER = \"{UpdateChecker.EndOfNotesMarker}\""));
    }

    // ---------------------------------------------------------------- finding releases

    [Test]
    public async Task CheckAsync_FromAnOlderVersion_FindsTheNewerStableReleasesNewestFirst()
    {
        // Arrange
        var checker = CheckerAnswering(Releases);

        // Act
        var result = await checker.CheckAsync(RunningVersion, includePrereleases: false);

        // Assert
        Assert.That(result.IsUpdateAvailable, Is.True);
        Assert.That(result.Newer.Select(release => release.Version.ToString()), Is.EqualTo(new[] { "1.4.0", "1.3.1" }));
        Assert.That(result.Latest!.Version.ToString(), Is.EqualTo("1.4.0"));
    }

    [Test]
    public async Task CheckAsync_FromAnOlderVersion_BuildsAChangelogOfEveryNewerReleaseNewestFirst()
    {
        // Arrange
        var checker = CheckerAnswering(Releases);

        // Act
        var result = await checker.CheckAsync(RunningVersion, includePrereleases: false);

        // Assert
        var changelog = result.Changelog;
        Assert.That(changelog.IndexOf("## DeckHost 1.4.0", StringComparison.Ordinal), Is.LessThan(changelog.IndexOf("## DeckHost 1.3.1", StringComparison.Ordinal)));
        Assert.That(changelog, Does.Contain("* Lights"));
        Assert.That(changelog, Does.Contain("No release notes."));
        Assert.That(changelog, Does.Not.Contain("Old"));
    }

    [Test]
    public void ParseReleases_DraftsAndNonVersionTags_AreIgnored()
    {
        // Act
        var releases = UpdateChecker.ParseReleases(Releases);

        // Assert
        Assert.That(releases.Any(release => release.Tag is "v1.6.0" or "nightly"), Is.False);
    }

    [Test]
    public void Evaluate_AStableBuildAndPrereleasesNotAsked_HasNoUpdate()
    {
        // Arrange
        var releases = UpdateChecker.ParseReleases(Releases);

        // Act
        var stable = UpdateChecker.Evaluate(SemanticVersion.Parse("1.4.0"), releases, includePrereleases: false);

        // Assert
        Assert.That(stable.IsUpdateAvailable, Is.False);
    }

    [Test]
    public void Evaluate_APrereleaseAsked_OffersThePrerelease()
    {
        // Arrange
        var releases = UpdateChecker.ParseReleases(Releases);

        // Act
        var preview = UpdateChecker.Evaluate(SemanticVersion.Parse("1.4.0"), releases, includePrereleases: true);

        // Assert
        Assert.That(Expect.Single(preview.Newer).Version.ToString(), Is.EqualTo("1.5.0-alpha.2"));
    }

    [Test]
    public void Evaluate_APrereleaseBuild_IsOfferedItsRelease()
    {
        // Arrange
        var releases = UpdateChecker.ParseReleases(Releases);

        // Act
        var alpha = UpdateChecker.Evaluate(SemanticVersion.Parse("1.4.0-alpha.7+4b09090"), releases, includePrereleases: false);

        // Assert
        Assert.That(alpha.Newer[0].Version.ToString(), Is.EqualTo("1.4.0"));
    }

    // ---------------------------------------------------------------- packages

    [TestCase("win-x64", "DeckHost-1.4.0-win-x64-setup.exe")]
    [TestCase("linux-x64", LinuxTarName)]
    [TestCase("osx-arm64", "DeckHost-1.4.0-osx-arm64.zip")]
    [TestCase("linux-arm64", null)]
    public void SelectAsset_ARuntime_PicksThePackageForThePlatform(string rid, string? expected)
    {
        // Arrange
        var release = UpdateChecker.ParseReleases(Releases).Single(release => release.Tag == "v1.4.0");

        // Act
        var asset = UpdateChecker.SelectAsset(release, rid);

        // Assert
        Assert.That(asset?.Name, Is.EqualTo(expected));
    }

    [Test]
    public void SelectAsset_AnAppImageRunningOnAReleaseWithOne_PicksTheAppImage()
    {
        // Arrange
        var release = Release((LinuxTarName, LinuxTarUrl), (AppImageName, "https://example.com/linux.AppImage"));

        // Act
        var asset = UpdateChecker.SelectAsset(release, "linux-x64", appImage: true);

        // Assert
        Assert.That(asset?.Name, Is.EqualTo(AppImageName));
    }

    [Test]
    public void SelectAsset_AnUnpackedInstallOnAReleaseWithAnAppImage_StillPicksTheTarball()
    {
        // Arrange
        var release = Release((LinuxTarName, LinuxTarUrl), (AppImageName, "https://example.com/linux.AppImage"));

        // Act
        var asset = UpdateChecker.SelectAsset(release, "linux-x64");

        // Assert
        Assert.That(asset?.Name, Is.EqualTo(LinuxTarName));
    }

    [Test]
    public void SelectAsset_AnAppImageOnAReleaseWithoutOne_FallsBackToTheTarball()
    {
        // Arrange
        var older = Release((LinuxTarName, LinuxTarUrl));

        // Act
        var asset = UpdateChecker.SelectAsset(older, "linux-x64", appImage: true);

        // Assert
        Assert.That(asset?.Name, Is.EqualTo(LinuxTarName));
    }

    // ---------------------------------------------------------------- problems

    [Test]
    public void CheckAsync_WhenGitHubCantBeReached_SaysSo()
    {
        // Arrange
        var offline = new UpdateChecker(new HttpClient(new FakeHandler(_ => throw new HttpRequestException("No such host"))));

        // Act
        var error = Assert.ThrowsAsync<UpdateException>(() => offline.CheckAsync(RunningVersion, false));

        // Assert
        Assert.That(error.Message, Does.Contain("Couldn't reach GitHub"));
    }

    [Test]
    public void CheckAsync_WhenTheRateLimitIsUsedUp_TellsTheUserToTryLater()
    {
        // Arrange
        var limited = new UpdateChecker(new HttpClient(new FakeHandler(_ => null, HttpStatusCode.Forbidden, ("X-RateLimit-Remaining", "0"))));

        // Act
        var error = Assert.ThrowsAsync<UpdateException>(() => limited.CheckAsync(RunningVersion, false));

        // Assert
        Assert.That(error.Message, Does.Contain("try again in an hour"));
    }

    [Test]
    public void CheckAsync_ARunningVersionThatIsNoVersion_IsAnError()
    {
        // Arrange
        var checker = CheckerAnswering(Releases);

        // Act
        TestDelegate check = () => checker.CheckAsync("dev build", false).GetAwaiter().GetResult();

        // Assert
        Assert.Throws<UpdateException>(check);
    }

    // ---------------------------------------------------------------- downloading

    private static byte[] ThePackage() => Encoding.UTF8.GetBytes("the new DeckHost");

    private static string HashOf(byte[] package) => Convert.ToHexStringLower(SHA256.HashData(package));

    private static UpdateChecker CheckerServing(byte[] package, string sums) => new(new HttpClient(new FakeHandler(request => request.RequestUri!.AbsolutePath switch
    {
        "/sums" => Encoding.UTF8.GetBytes(sums),
        "/linux.tar.gz" => package,
        _ => null,
    })));

    [Test]
    public async Task DownloadAsync_APackageWithAMatchingChecksum_IsSavedIntact()
    {
        // Arrange
        var package = ThePackage();
        var release = Release((LinuxTarName, LinuxTarUrl), (SumsName, SumsUrl));
        var checker = CheckerServing(package, $"{new string('0', 64)}  other.zip\n{HashOf(package)}  {LinuxTarName}\n");

        // Act
        var path = await checker.DownloadAsync(release, release.Assets[0], _directory, null);

        // Assert
        Assert.That(await File.ReadAllBytesAsync(path), Is.EqualTo(package));
    }

    [Test]
    public async Task DownloadAsync_AnyPackage_ReportsProgressUpToOne()
    {
        // Arrange
        var package = ThePackage();
        var release = Release((LinuxTarName, LinuxTarUrl), (SumsName, SumsUrl));
        var checker = CheckerServing(package, $"{HashOf(package)}  {LinuxTarName}\n");
        var reported = new List<double>();

        // Act
        await checker.DownloadAsync(release, release.Assets[0], _directory, new SyncProgress(reported.Add));

        // Assert
        Assert.That(reported[^1], Is.EqualTo(1d));
    }

    [Test]
    public void DownloadAsync_ADamagedPackage_IsReportedAsDamaged()
    {
        // Arrange
        var release = Release((LinuxTarName, LinuxTarUrl), (SumsName, SumsUrl));
        var checker = CheckerServing("tampered"u8.ToArray(), $"{new string('a', 64)}  {LinuxTarName}\n");

        // Act
        var error = Assert.ThrowsAsync<UpdateException>(() => checker.DownloadAsync(release, release.Assets[0], _directory, null));

        // Assert
        Assert.That(error.Message, Does.Contain("damaged"));
    }

    [Test]
    public void DownloadAsync_ADamagedPackage_IsDeleted()
    {
        // Arrange
        var release = Release((LinuxTarName, LinuxTarUrl), (SumsName, SumsUrl));
        var checker = CheckerServing("tampered"u8.ToArray(), $"{new string('a', 64)}  {LinuxTarName}\n");

        // Act
        Assert.ThrowsAsync<UpdateException>(() => checker.DownloadAsync(release, release.Assets[0], _directory, null));

        // Assert
        Assert.That(Directory.GetFiles(_directory), Is.Empty);
    }

    [Test]
    public void FindChecksum_Sha256sumOutput_ReadsTheHashOfTheNamedFile()
    {
        // Arrange
        var hash = new string('b', 64);

        // Act
        var found = UpdateChecker.FindChecksum($"{hash} *DeckHost-setup.exe\r\n", "DeckHost-setup.exe");

        // Assert
        Assert.That(found, Is.EqualTo(hash));
    }

    [Test]
    public void FindChecksum_AnotherFileWithTheSameStart_IsNotMatched()
    {
        // Arrange
        var hash = new string('b', 64);

        // Act
        var found = UpdateChecker.FindChecksum($"{hash}  DeckHost-setup.exe.sig\n", "DeckHost-setup.exe");

        // Assert
        Assert.That(found, Is.Null);
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

    private const string RunningVersion = "1.3.0";
    private const string LatestVersion = "1.4.0";

    [Test]
    public async Task CheckAsync_AnOlderBuild_AnnouncesTheUpdateAndRemembersTheCheck()
    {
        // Arrange
        var updates = Create(RunningVersion);

        // Act
        await updates.CheckAsync(manual: false);

        // Assert
        Assert.That(updates.IsUpdateAvailable, Is.True);
        Assert.That(updates.ShowBanner, Is.True);
        Assert.That(updates.AvailableTitle, Is.EqualTo("DeckHost 1.4.0 is available"));
        Assert.That(_settings.LastUpdateCheck, Is.Not.Null);
    }

    [Test]
    public async Task Skip_AnAnnouncedUpdate_HidesTheBannerAndRemembersTheVersion()
    {
        // Arrange
        var updates = Create(RunningVersion);
        await updates.CheckAsync(manual: false);

        // Act
        updates.SkipCommand.Execute(null);

        // Assert
        Assert.That(updates.ShowBanner, Is.False);
        Assert.That(_settings.SkippedVersion, Is.EqualTo(LatestVersion));
    }

    [Test]
    public async Task CheckAsync_AfterARestartWithTheVersionSkipped_StaysQuiet()
    {
        // Arrange
        var updates = Create(RunningVersion);
        await updates.CheckAsync(manual: false);
        updates.SkipCommand.Execute(null);
        var restarted = Create(RunningVersion);

        // Act
        await restarted.CheckAsync(manual: false);

        // Assert
        Assert.That(restarted.IsSkipped, Is.True);
        Assert.That(restarted.ShowBanner, Is.False);
    }

    [Test]
    public async Task CheckAsync_ByHandAfterSkipping_ShowsTheUpdateAgainAndForgetsTheSkip()
    {
        // Arrange
        var updates = Create(RunningVersion);
        await updates.CheckAsync(manual: false);
        updates.SkipCommand.Execute(null);
        var restarted = Create(RunningVersion);
        await restarted.CheckAsync(manual: false);

        // Act
        await restarted.CheckAsync(manual: true);

        // Assert
        Assert.That(restarted.ShowBanner, Is.True);
        Assert.That(_settings.SkippedVersion, Is.Null);
    }

    [Test]
    public async Task Later_AnAnnouncedUpdate_HidesTheNoticeForThisSessionOnly()
    {
        // Arrange
        var updates = Create(RunningVersion);
        await updates.CheckAsync(manual: false);

        // Act
        updates.LaterCommand.Execute(null);

        // Assert
        Assert.That(updates.ShowBanner, Is.False);
        Assert.That(updates.IsUpdateAvailable, Is.True);
        Assert.That(_settings.SkippedVersion, Is.Null);
    }

    [Test]
    public async Task CheckAsync_AStableBuildOnTheLatestRelease_IsUpToDateAndIgnoresPrereleases()
    {
        // Arrange
        var stable = Create(LatestVersion);

        // Act
        await stable.CheckAsync(manual: true);

        // Assert
        Assert.That(stable.IncludePrereleases, Is.False);
        Assert.That(stable.IsUpdateAvailable, Is.False);
        Assert.That(stable.Status, Does.StartWith("DeckHost is up to date"));
    }

    [Test]
    public async Task CheckAsync_APrereleaseBuild_IsOfferedPrereleasesByDefault()
    {
        // Arrange
        const string alphaBuild = "1.4.0-alpha.3+4b09090";
        var beta = Create(alphaBuild);

        // Act
        await beta.CheckAsync(manual: true);

        // Assert
        Assert.That(beta.IncludePrereleases, Is.True);
        Assert.That(beta.Latest!.Version.ToString(), Is.EqualTo("1.4.1-beta.1"));
        Assert.That(beta.AvailableSummary, Is.EqualTo($"You have {alphaBuild}. 2 releases since then."));
    }

    private UpdateViewModel Offline() => new(
        new UpdateChecker(new HttpClient(new UpdateCheckerTests.FakeHandler(_ => throw new HttpRequestException("offline")))),
        RunningVersion,
        () => _settings,
        settings => _settings = settings);

    [Test]
    public void Settings_ChangedInTheViewModel_AreSaved()
    {
        // Arrange
        var updates = Offline();

        // Act
        updates.CheckAutomatically = false;
        updates.IncludePrereleases = true;
        updates.InstallAutomatically = true;

        // Assert
        Assert.That(_settings.CheckForUpdates, Is.False);
        Assert.That(_settings.IncludePrereleases, Is.True);
        Assert.That(_settings.InstallUpdatesAutomatically, Is.True);
        Assert.That(updates.IsReadyToInstallOnExit, Is.False);
    }

    [Test]
    public async Task CheckAsync_AutomaticallyWhileOffline_ReportsNothing()
    {
        // Arrange
        var updates = Offline();
        var before = updates.Status;

        // Act
        await updates.CheckAsync(manual: false);

        // Assert
        Assert.That(updates.Status, Is.EqualTo(before));
    }

    [Test]
    public async Task CheckAsync_ByHandWhileOffline_ReportsTheProblem()
    {
        // Arrange
        var updates = Offline();

        // Act
        await updates.CheckAsync(manual: true);

        // Assert
        Assert.That(updates.Status, Does.Contain("Couldn't reach GitHub"));
    }

    private UpdateViewModel Create(string version, IAppUpdater? updater = null) => new(
        new UpdateChecker(new HttpClient(new UpdateCheckerTests.FakeHandler(_ => Encoding.UTF8.GetBytes(Releases)))),
        version,
        () => _settings,
        settings => _settings = settings,
        updater);

    [Test]
    public async Task Install_AVelopackInstall_DownloadsThenAsksToRestartIntoIt()
    {
        // Arrange
        UpdateViewModel.RestartNoticeDelay = TimeSpan.Zero;
        var updater = new FakeUpdater();
        var updates = Create(RunningVersion, updater);
        var restartRequested = false;
        updates.RestartRequested += () => restartRequested = true;
        await updates.CheckAsync(manual: false);

        // Act
        await updates.InstallCommand.ExecuteAsync(null);

        // Assert
        Assert.That(updater.Downloads, Is.EqualTo(1));
        Assert.That(updater.AppliedWithRestart, Is.True);
        Assert.That(restartRequested, Is.True);
    }

    [Test]
    public async Task Install_CancelledWhileDownloading_AppliesNothing()
    {
        // Arrange
        var updater = new FakeUpdater { WaitForCancel = true };
        var updates = Create(RunningVersion, updater);
        await updates.CheckAsync(manual: false);

        // Act
        var install = updates.InstallCommand.ExecuteAsync(null);
        await updater.Started.Task;
        updates.CancelCommand.Execute(null);
        await install;

        // Assert
        Assert.That(updater.AppliedWithRestart, Is.Null);
        Assert.That(updates.Status, Is.EqualTo("Update cancelled."));
        Assert.That(updates.IsInstalling, Is.False);
    }

    [Test]
    public void InstallLabel_AVelopackInstall_InstallsAndRestarts()
    {
        // Act
        var updates = Create(RunningVersion, new FakeUpdater());

        // Assert
        Assert.That(updates.InstallLabel, Is.EqualTo("Install and restart"));
        Assert.That(updates.CanInstallAutomatically, Is.True);
    }

    private sealed class FakeUpdater : IAppUpdater
    {
        public UpdateMode Mode => UpdateMode.Velopack;

        public bool WaitForCancel { get; init; }

        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int Downloads { get; private set; }

        public bool? AppliedWithRestart { get; private set; }

        public bool HasDownloaded => Downloads > 0;

        public async Task<string?> DownloadAsync(bool includePrereleases, IProgress<int> progress, CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            if (WaitForCancel)
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }

            progress.Report(100);
            Downloads++;
            return LatestVersion;
        }

        public void ApplyAfterExit(bool restart) => AppliedWithRestart = restart;
    }
}

public sealed class UpdateInstallerTests : IDisposable
{
    private const string RuntimeIdentifier = "win-x64";
    private const string InstallerName = "DeckHost-1.5.0-win-x64-install.exe";
    private const string MsiName = "DeckHost-1.5.0-win-x64.msi";

    private readonly string _directory = Directory.CreateTempSubdirectory("deckhost-migration").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Test]
    public void Quote_APathWithAnApostrophe_IsEscapedForPowerShell()
    {
        // Act
        var quoted = UpdateInstaller.Quote("C:\\Users\\O'Brien\\setup.exe");

        // Assert
        Assert.That(quoted, Is.EqualTo("'C:\\Users\\O''Brien\\setup.exe'"));
    }

    [TestCase(false, InstallerName)]
    [TestCase(true, MsiName)]
    public void SelectMigrationAsset_AnInstall_PicksTheInstallerForItsScope(bool forAllUsers, string expected)
    {
        // Arrange
        var release = WindowsRelease(InstallerName, MsiName, "DeckHost-1.5.0-win-x64-setup.exe");

        // Act
        var asset = UpdateInstaller.SelectMigrationAsset(release, RuntimeIdentifier, forAllUsers);

        // Assert
        Assert.That(asset?.Name, Is.EqualTo(expected));
    }

    [Test]
    public void SelectMigrationAsset_AReleaseWithOnlyTheOldSetup_FindsNothing()
    {
        // Arrange
        var release = WindowsRelease("DeckHost-1.4.0-win-x64-setup.exe");

        // Act
        var asset = UpdateInstaller.SelectMigrationAsset(release, RuntimeIdentifier, forAllUsers: false);

        // Assert
        Assert.That(asset, Is.Null);
    }

    [Test]
    public void Take_AWrittenNote_ReturnsItOnce()
    {
        // Arrange
        var path = Path.Combine(_directory, "migration.json");
        MigrationNote.Write(new MigrationNote(StartAtLogin: true), path);

        // Act
        var first = MigrationNote.Take(path);
        var second = MigrationNote.Take(path);

        // Assert
        Assert.That(first, Is.EqualTo(new MigrationNote(StartAtLogin: true)));
        Assert.That(second, Is.Null);
    }

    [Test]
    public void Take_ABrokenNote_ReturnsNothing()
    {
        // Arrange
        var path = Path.Combine(_directory, "migration.json");
        File.WriteAllText(path, "{ not json");

        // Act
        var note = MigrationNote.Take(path);

        // Assert
        Assert.That(note, Is.Null);
    }

    private static ReleaseInfo WindowsRelease(params string[] names) => new(
        Tag: "v1.5.0",
        Version: SemanticVersion.Parse("1.5.0"),
        Name: "DeckHost 1.5.0",
        Notes: string.Empty,
        PageUrl: string.Empty,
        IsPrerelease: false,
        PublishedAt: null,
        Assets: names.Select(name => new ReleaseAsset(name, "https://example.com/" + name, 1)).ToList());
}

public sealed class AppUpdaterTests
{
    [TestCase(true, true, true, UpdateMode.Velopack)]
    [TestCase(true, false, false, UpdateMode.Velopack)]
    [TestCase(false, true, true, UpdateMode.MigrateWindows)]
    [TestCase(false, true, false, UpdateMode.ManualDownload)]
    [TestCase(false, false, true, UpdateMode.ManualDownload)]
    public void Decide_AnInstall_PicksHowItUpdates(bool velopackInstall, bool windows, bool innoUninstaller, UpdateMode expected)
    {
        // Act
        var mode = AppUpdater.Decide(velopackInstall, windows, innoUninstaller);

        // Assert
        Assert.That(mode, Is.EqualTo(expected));
    }
}

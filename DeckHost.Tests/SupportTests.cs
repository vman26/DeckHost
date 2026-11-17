using System.IO.Compression;
using DeckHost.App.ViewModels;
using DeckHost.Core.Support;

namespace DeckHost.Tests;

public sealed class SupportTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("support").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static Exception Thrown()
    {
        try
        {
            throw new InvalidOperationException("Boom in " + Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "secret.txt"));
        }
        catch (InvalidOperationException ex)
        {
            return ex;
        }
    }

    private const string Version = "1.2.3";
    private const string Origin = "test";
    private const int ReportsKept = 1;
    private const int MaxIssueLinkLength = 12_000;
    private const string TodayLog = "logs/deckhost-today.log";

    private static string HomeFolder => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    private static DateTimeOffset FirstCrashTime() =>
        new(2026, 11, 1, 10, 0, 0, TimeZoneInfo.Local.GetUtcOffset(new DateTime(2026, 11, 1)));

    private string CrashFolder => Path.Combine(_root, "crashes");

    [Test]
    public void Write_ACrash_RecordsTheVersionAndTheException()
    {
        // Act
        var path = CrashReports.Write(CrashFolder, Thrown(), Version, Origin, FirstCrashTime());

        // Assert
        var text = File.ReadAllText(path!);
        Assert.That(text, Does.Contain($"DeckHost {Version} closed unexpectedly ({Origin})."));
        Assert.That(text, Does.Contain("InvalidOperationException"));
    }

    [Test]
    public void Write_ACrash_HidesTheHomeFolder()
    {
        // Act
        var path = CrashReports.Write(CrashFolder, Thrown(), Version, Origin, FirstCrashTime());

        // Assert
        Assert.That(File.ReadAllText(path!), Does.Not.Contain(HomeFolder));
    }

    [Test]
    public void Find_AfterTwoCrashes_ListsBoth()
    {
        // Arrange
        CrashReports.Write(CrashFolder, Thrown(), Version, Origin, FirstCrashTime());
        CrashReports.Write(CrashFolder, Thrown(), Version, Origin, FirstCrashTime().AddHours(1));

        // Act
        var reports = CrashReports.Find(CrashFolder);

        // Assert
        Assert.That(reports.Count, Is.EqualTo(2));
    }

    [Test]
    public void Find_WithTheTimeTheUserLastSaw_ListsOnlyTheNewerCrash()
    {
        // Arrange
        var first = FirstCrashTime();
        CrashReports.Write(CrashFolder, Thrown(), Version, Origin, first);
        CrashReports.Write(CrashFolder, Thrown(), Version, Origin, first.AddHours(1));

        // Act
        var unseen = CrashReports.Find(CrashFolder, seenUntil: first);

        // Assert
        Assert.That(Expect.Single(unseen).Time, Is.EqualTo(first.AddHours(1)));
    }

    [Test]
    public void Prune_KeepingOne_LeavesTheNewestReport()
    {
        // Arrange
        CrashReports.Write(CrashFolder, Thrown(), Version, Origin, FirstCrashTime());
        CrashReports.Write(CrashFolder, Thrown(), Version, Origin, FirstCrashTime().AddHours(1));

        // Act
        CrashReports.Prune(CrashFolder, keep: ReportsKept);

        // Assert
        Assert.That(CrashReports.Find(CrashFolder), Has.Exactly(ReportsKept).Items);
    }

    [Test]
    public void IssueUri_ALongReport_CarriesTheDeviceAndStaysShort()
    {
        // Arrange
        var body = CrashReports.IssueBody(Version, new string('x', 20_000), ["N4 Pro (connected)"]);

        // Act
        var uri = CrashReports.IssueUri("owner/releases", "Crash in DeckHost", body);

        // Assert
        Assert.That(uri.AbsoluteUri, Does.StartWith("https://github.com/owner/releases/issues/new?title=Crash%20in%20DeckHost&body="));
        Assert.That(uri.AbsoluteUri, Does.Contain("N4%20Pro"));
        Assert.That(uri.AbsoluteUri.Length, Is.LessThan(MaxIssueLinkLength));
        Assert.That(uri.AbsoluteUri, Does.Contain("cut%20short"));
    }

    private string MakeSupportFolder()
    {
        Directory.CreateDirectory(Path.Combine(_root, "logs", "plugins"));
        Directory.CreateDirectory(Path.Combine(_root, "devices"));
        File.WriteAllText(Path.Combine(_root, "logs", "deckhost-today.log"), "pressed key in " + HomeFolder);
        File.WriteAllText(Path.Combine(_root, "logs", "plugins", "x.log"), "plugin");
        var old = Path.Combine(_root, "logs", "deckhost-old.log");
        File.WriteAllText(old, "old");
        File.SetLastWriteTime(old, DateTime.Now.AddDays(-10));
        File.WriteAllText(Path.Combine(_root, "devices", "n4.json"), "{}");
        File.WriteAllText(Path.Combine(_root, "settings.json"), "{}");
        File.WriteAllText(Path.Combine(_root, "profiles.json"), "{\"password\":\"hunter2\"}");
        CrashReports.Write(CrashFolder, Thrown(), Version, Origin);
        return Path.Combine(_root, "bundle.zip");
    }

    private static List<string> EntryNames(string bundle)
    {
        using var zip = ZipFile.OpenRead(bundle);
        return [.. zip.Entries.Select(entry => entry.FullName)];
    }

    [Test]
    public void Create_ASupportBundle_HasTheSummaryRecentLogsDevicesSettingsAndCrashes()
    {
        // Arrange
        var bundle = MakeSupportFolder();

        // Act
        SupportBundle.Create(_root, bundle, "summary here");

        // Assert
        var names = EntryNames(bundle);
        Assert.That(names, Does.Contain("summary.txt"));
        Assert.That(names, Does.Contain(TodayLog));
        Assert.That(names, Does.Contain("logs/plugins/x.log"));
        Assert.That(names, Does.Contain("devices/n4.json"));
        Assert.That(names, Does.Contain("settings.json"));
        Assert.That(names.Any(name => name.StartsWith("crashes/crash-", StringComparison.Ordinal)), Is.True);
    }

    [Test]
    public void Create_ASupportBundle_LeavesOutOldLogsAndTheProfilesUnlessAsked()
    {
        // Arrange
        var bundle = MakeSupportFolder();

        // Act
        SupportBundle.Create(_root, bundle, "summary here");

        // Assert
        var names = EntryNames(bundle);
        Assert.That(names, Does.Not.Contain("logs/deckhost-old.log"));
        Assert.That(names, Does.Not.Contain("profiles.json"));
    }

    [Test]
    public void Create_ASupportBundle_HidesTheHomeFolderInLogs()
    {
        // Arrange
        var bundle = MakeSupportFolder();

        // Act
        SupportBundle.Create(_root, bundle, "summary here");

        // Assert
        using var zip = ZipFile.OpenRead(bundle);
        using var log = new StreamReader(zip.GetEntry(TodayLog)!.Open());
        Assert.That(log.ReadToEnd(), Is.EqualTo("pressed key in ~"));
    }

    [Test]
    public void Create_ASupportBundleWithProfilesAsked_IncludesThem()
    {
        // Arrange
        var bundle = MakeSupportFolder();

        // Act
        SupportBundle.Create(_root, bundle, "summary", includeProfiles: true);

        // Assert
        Assert.That(EntryNames(bundle), Does.Contain("profiles.json"));
    }

    [TestCase("Vman26/DeckHost", "https://vman26.github.io/DeckHost/")]
    [TestCase("odd", "https://github.com/odd")]
    public void DocumentationUrlFor_ARepository_LivesOnItsPagesOrItsGitHubPage(string repository, string expected)
    {
        // Act
        var url = SupportViewModel.DocumentationUrlFor(repository);

        // Assert
        Assert.That(url, Is.EqualTo(expected));
    }
}

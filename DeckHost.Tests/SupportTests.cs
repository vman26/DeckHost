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

    [Fact]
    public void A_crash_report_is_written_found_once_and_has_no_home_folder()
    {
        var crashes = Path.Combine(_root, "crashes");
        var first = new DateTimeOffset(2026, 11, 1, 10, 0, 0, TimeZoneInfo.Local.GetUtcOffset(new DateTime(2026, 11, 1)));
        var path = CrashReports.Write(crashes, Thrown(), "1.2.3", "test", first);
        CrashReports.Write(crashes, Thrown(), "1.2.3", "test", first.AddHours(1));

        Assert.NotNull(path);
        var text = File.ReadAllText(path!);
        Assert.Contains("DeckHost 1.2.3 closed unexpectedly (test).", text);
        Assert.Contains("InvalidOperationException", text);
        Assert.DoesNotContain(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), text);

        Assert.Equal(2, CrashReports.Find(crashes).Count);
        var newer = Assert.Single(CrashReports.Find(crashes, seenUntil: first));
        Assert.Equal(first.AddHours(1), newer.Time);

        CrashReports.Prune(crashes, keep: 1);
        Assert.Single(CrashReports.Find(crashes));
    }

    [Fact]
    public void The_issue_link_carries_the_report_and_stays_short()
    {
        var body = CrashReports.IssueBody("1.2.3", new string('x', 20_000), ["N4 Pro (connected)"]);
        var uri = CrashReports.IssueUri("owner/releases", "Crash in DeckHost", body);

        Assert.StartsWith("https://github.com/owner/releases/issues/new?title=Crash%20in%20DeckHost&body=", uri.AbsoluteUri);
        Assert.Contains("N4%20Pro", uri.AbsoluteUri);
        Assert.True(uri.AbsoluteUri.Length < 12_000);
        Assert.Contains("cut%20short", uri.AbsoluteUri);
    }

    [Fact]
    public void A_support_bundle_has_logs_crashes_and_settings_but_not_profiles_unless_asked()
    {
        Directory.CreateDirectory(Path.Combine(_root, "logs", "plugins"));
        Directory.CreateDirectory(Path.Combine(_root, "devices"));
        File.WriteAllText(Path.Combine(_root, "logs", "deckhost-today.log"), "pressed key in " + Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        File.WriteAllText(Path.Combine(_root, "logs", "plugins", "x.log"), "plugin");
        var old = Path.Combine(_root, "logs", "deckhost-old.log");
        File.WriteAllText(old, "old");
        File.SetLastWriteTime(old, DateTime.Now.AddDays(-10));
        File.WriteAllText(Path.Combine(_root, "devices", "n4.json"), "{}");
        File.WriteAllText(Path.Combine(_root, "settings.json"), "{}");
        File.WriteAllText(Path.Combine(_root, "profiles.json"), "{\"password\":\"hunter2\"}");
        CrashReports.Write(Path.Combine(_root, "crashes"), Thrown(), "1.2.3", "test");

        var bundle = Path.Combine(_root, "bundle.zip");
        SupportBundle.Create(_root, bundle, "summary here");
        using (var zip = ZipFile.OpenRead(bundle))
        {
            var names = zip.Entries.Select(entry => entry.FullName).ToList();
            Assert.Contains("summary.txt", names);
            Assert.Contains("logs/deckhost-today.log", names);
            Assert.Contains("logs/plugins/x.log", names);
            Assert.Contains("devices/n4.json", names);
            Assert.Contains("settings.json", names);
            Assert.Contains(names, name => name.StartsWith("crashes/crash-", StringComparison.Ordinal));
            Assert.DoesNotContain("logs/deckhost-old.log", names);
            Assert.DoesNotContain("profiles.json", names);
            using var log = new StreamReader(zip.GetEntry("logs/deckhost-today.log")!.Open());
            Assert.Equal("pressed key in ~", log.ReadToEnd());
        }

        SupportBundle.Create(_root, bundle, "summary", includeProfiles: true);
        using var withProfiles = ZipFile.OpenRead(bundle);
        Assert.Contains(withProfiles.Entries, entry => entry.FullName == "profiles.json");
    }

    [Fact]
    public void The_documentation_lives_on_the_releases_repository_pages()
    {
        Assert.Equal("https://vman26.github.io/DeckHost-releases/", SupportViewModel.DocumentationUrlFor("Vman26/DeckHost-releases"));
        Assert.Equal("https://github.com/odd", SupportViewModel.DocumentationUrlFor("odd"));
    }
}

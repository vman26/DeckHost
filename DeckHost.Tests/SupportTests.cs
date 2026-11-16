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

    [Test]
    public void A_crash_report_is_written_found_once_and_has_no_home_folder()
    {
        var crashes = Path.Combine(_root, "crashes");
        var first = new DateTimeOffset(2026, 11, 1, 10, 0, 0, TimeZoneInfo.Local.GetUtcOffset(new DateTime(2026, 11, 1)));
        var path = CrashReports.Write(crashes, Thrown(), "1.2.3", "test", first);
        CrashReports.Write(crashes, Thrown(), "1.2.3", "test", first.AddHours(1));

        Assert.That(path, Is.Not.Null);
        var text = File.ReadAllText(path!);
        Assert.That(text, Does.Contain("DeckHost 1.2.3 closed unexpectedly (test)."));
        Assert.That(text, Does.Contain("InvalidOperationException"));
        Assert.That(text, Does.Not.Contain(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)));

        Assert.That(CrashReports.Find(crashes).Count, Is.EqualTo(2));
        var newer = Expect.Single(CrashReports.Find(crashes, seenUntil: first));
        Assert.That(newer.Time, Is.EqualTo(first.AddHours(1)));

        CrashReports.Prune(crashes, keep: 1);
        Assert.That(CrashReports.Find(crashes), Has.Exactly(1).Items);
    }

    [Test]
    public void The_issue_link_carries_the_report_and_stays_short()
    {
        var body = CrashReports.IssueBody("1.2.3", new string('x', 20_000), ["N4 Pro (connected)"]);
        var uri = CrashReports.IssueUri("owner/releases", "Crash in DeckHost", body);

        Assert.That(uri.AbsoluteUri, Does.StartWith("https://github.com/owner/releases/issues/new?title=Crash%20in%20DeckHost&body="));
        Assert.That(uri.AbsoluteUri, Does.Contain("N4%20Pro"));
        Assert.That(uri.AbsoluteUri.Length < 12_000, Is.True);
        Assert.That(uri.AbsoluteUri, Does.Contain("cut%20short"));
    }

    [Test]
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
            Assert.That(names, Does.Contain("summary.txt"));
            Assert.That(names, Does.Contain("logs/deckhost-today.log"));
            Assert.That(names, Does.Contain("logs/plugins/x.log"));
            Assert.That(names, Does.Contain("devices/n4.json"));
            Assert.That(names, Does.Contain("settings.json"));
            Assert.That(names.Any(name => name.StartsWith("crashes/crash-", StringComparison.Ordinal)), Is.True);
            Assert.That(names, Does.Not.Contain("logs/deckhost-old.log"));
            Assert.That(names, Does.Not.Contain("profiles.json"));
            using var log = new StreamReader(zip.GetEntry("logs/deckhost-today.log")!.Open());
            Assert.That(log.ReadToEnd(), Is.EqualTo("pressed key in ~"));
        }

        SupportBundle.Create(_root, bundle, "summary", includeProfiles: true);
        using var withProfiles = ZipFile.OpenRead(bundle);
        Assert.That(withProfiles.Entries.Any(entry => entry.FullName == "profiles.json"), Is.True);
    }

    [Test]
    public void The_documentation_lives_on_the_repository_pages()
    {
        Assert.That(SupportViewModel.DocumentationUrlFor("Vman26/DeckHost"), Is.EqualTo("https://vman26.github.io/DeckHost/"));
        Assert.That(SupportViewModel.DocumentationUrlFor("odd"), Is.EqualTo("https://github.com/odd"));
    }
}

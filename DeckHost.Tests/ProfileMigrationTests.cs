using System.Text.Json.Nodes;
using DeckHost.Core.Profiles;

namespace DeckHost.Tests;

public class ProfileMigrationTests
{
    private static readonly IReadOnlyList<ProfileMigrations.Step> Steps =
    [
        // A rename: "selected" became "selectedProfileId".
        new(1, "rename selected", static document =>
        {
            if (document.Remove("selected", out var value))
            {
                document["selectedProfileId"] = value;
            }
        }),
        new(2, "nothing", static _ => { }),
    ];

    [Test]
    public void An_old_document_runs_every_step_up_to_the_target_and_gets_its_version()
    {
        var document = JsonNode.Parse("""{ "selected": "p1", "profiles": [] }""")!.AsObject();

        var applied = ProfileMigrations.Migrate(document, 3, Steps);

        Assert.That(applied.Count, Is.EqualTo(2));
        Assert.That(document["selectedProfileId"]!.GetValue<string>(), Is.EqualTo("p1"));
        Assert.That(ProfileMigrations.VersionOf(document), Is.EqualTo(3));
    }

    [Test]
    public void A_missing_step_is_an_error_and_a_newer_document_is_left_alone()
    {
        Assert.Throws<InvalidOperationException>(() => ProfileMigrations.Migrate(JsonNode.Parse("""{ "version": 1 }""")!.AsObject(), 5, Steps));

        var newer = JsonNode.Parse("""{ "version": 9 }""")!.AsObject();
        Assert.That(ProfileMigrations.Migrate(newer, 3, Steps), Is.Empty);
        Assert.That(ProfileMigrations.VersionOf(newer), Is.EqualTo(9));
    }

    [Test]
    public void Reading_migrates_older_files_and_reports_newer_ones()
    {
        var (old, newerOld, migrated) = ProfileStore.Read("""{ "version": 1, "selected": "p1", "profiles": [] }""", 3, Steps);
        Assert.That(old!.SelectedProfileId, Is.EqualTo("p1"));
        Assert.That(old.Version, Is.EqualTo(3));
        Assert.That(newerOld, Is.Null);
        Assert.That(migrated.Count, Is.EqualTo(2));

        var (future, newer, none) = ProfileStore.Read("""{ "version": 7, "selectedProfileId": "p2", "profiles": [], "somethingNew": 1 }""", 3, Steps);
        Assert.That(future!.SelectedProfileId, Is.EqualTo("p2"));
        Assert.That(newer, Is.EqualTo(7));
        Assert.That(none, Is.Empty);
    }

    [Test]
    public void A_file_from_a_newer_version_is_kept_in_the_backups_before_it_is_saved_over()
    {
        var folder = Path.Combine(Path.GetTempPath(), "deckhost-migration-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var path = Path.Combine(folder, "profiles.json");
            File.WriteAllText(path, """{ "version": 99, "selectedProfileId": null, "profiles": [] }""");
            var store = new ProfileStore(path);

            var document = store.Load()!;
            Assert.That(store.NewerVersionLoaded, Is.EqualTo(99));
            store.Save(document);

            Assert.That(File.ReadAllText(Path.Combine(store.BackupDirectory, "profiles-from-v99.json")), Does.Contain("99"));
            Assert.That(ProfileMigrations.VersionOf(JsonNode.Parse(File.ReadAllText(path))!.AsObject()), Is.EqualTo(ProfileDocument.CurrentVersion));
            Assert.That(store.NewerVersionLoaded, Is.Null);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}

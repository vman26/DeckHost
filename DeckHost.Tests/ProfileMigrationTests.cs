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

    [Fact]
    public void An_old_document_runs_every_step_up_to_the_target_and_gets_its_version()
    {
        var document = JsonNode.Parse("""{ "selected": "p1", "profiles": [] }""")!.AsObject();

        var applied = ProfileMigrations.Migrate(document, 3, Steps);

        Assert.Equal(2, applied.Count);
        Assert.Equal("p1", document["selectedProfileId"]!.GetValue<string>());
        Assert.Equal(3, ProfileMigrations.VersionOf(document));
    }

    [Fact]
    public void A_missing_step_is_an_error_and_a_newer_document_is_left_alone()
    {
        Assert.Throws<InvalidOperationException>(() => ProfileMigrations.Migrate(JsonNode.Parse("""{ "version": 1 }""")!.AsObject(), 5, Steps));

        var newer = JsonNode.Parse("""{ "version": 9 }""")!.AsObject();
        Assert.Empty(ProfileMigrations.Migrate(newer, 3, Steps));
        Assert.Equal(9, ProfileMigrations.VersionOf(newer));
    }

    [Fact]
    public void Reading_migrates_older_files_and_reports_newer_ones()
    {
        var (old, newerOld, migrated) = ProfileStore.Read("""{ "version": 1, "selected": "p1", "profiles": [] }""", 3, Steps);
        Assert.Equal("p1", old!.SelectedProfileId);
        Assert.Equal(3, old.Version);
        Assert.Null(newerOld);
        Assert.Equal(2, migrated.Count);

        var (future, newer, none) = ProfileStore.Read("""{ "version": 7, "selectedProfileId": "p2", "profiles": [], "somethingNew": 1 }""", 3, Steps);
        Assert.Equal("p2", future!.SelectedProfileId);
        Assert.Equal(7, newer);
        Assert.Empty(none);
    }

    [Fact]
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
            Assert.Equal(99, store.NewerVersionLoaded);
            store.Save(document);

            Assert.Contains("99", File.ReadAllText(Path.Combine(store.BackupDirectory, "profiles-from-v99.json")));
            Assert.Equal(ProfileDocument.CurrentVersion, ProfileMigrations.VersionOf(JsonNode.Parse(File.ReadAllText(path))!.AsObject()));
            Assert.Null(store.NewerVersionLoaded);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}

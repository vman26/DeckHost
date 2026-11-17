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

    private const string OldFile = """{ "version": 1, "selected": "p1", "profiles": [] }""";
    private const string NewerFile = """{ "version": 7, "selectedProfileId": "p2", "profiles": [], "somethingNew": 1 }""";
    private const int TargetVersion = 3;
    private const int FarFutureVersion = 99;

    [Test]
    public void Migrate_AnOldDocument_RunsEveryStepUpToTheTarget()
    {
        // Arrange
        var document = JsonNode.Parse("""{ "selected": "p1", "profiles": [] }""")!.AsObject();

        // Act
        var applied = ProfileMigrations.Migrate(document, TargetVersion, Steps);

        // Assert
        Assert.That(applied.Count, Is.EqualTo(2));
        Assert.That(document["selectedProfileId"]!.GetValue<string>(), Is.EqualTo("p1"));
    }

    [Test]
    public void Migrate_AnOldDocument_GetsTheTargetVersion()
    {
        // Arrange
        var document = JsonNode.Parse("""{ "selected": "p1", "profiles": [] }""")!.AsObject();

        // Act
        ProfileMigrations.Migrate(document, TargetVersion, Steps);

        // Assert
        Assert.That(ProfileMigrations.VersionOf(document), Is.EqualTo(TargetVersion));
    }

    [Test]
    public void Migrate_ADocumentWithAMissingStep_IsAnError()
    {
        // Arrange
        var document = JsonNode.Parse("""{ "version": 1 }""")!.AsObject();

        // Act
        TestDelegate migrate = () => ProfileMigrations.Migrate(document, 5, Steps);

        // Assert
        Assert.Throws<InvalidOperationException>(migrate);
    }

    [Test]
    public void Migrate_ANewerDocument_IsLeftAlone()
    {
        // Arrange
        var newer = JsonNode.Parse("""{ "version": 9 }""")!.AsObject();

        // Act
        var applied = ProfileMigrations.Migrate(newer, TargetVersion, Steps);

        // Assert
        Assert.That(applied, Is.Empty);
        Assert.That(ProfileMigrations.VersionOf(newer), Is.EqualTo(9));
    }

    [Test]
    public void Read_AnOlderFile_IsMigrated()
    {
        // Act
        var (document, newerVersion, migrated) = ProfileStore.Read(OldFile, TargetVersion, Steps);

        // Assert
        Assert.That(document!.SelectedProfileId, Is.EqualTo("p1"));
        Assert.That(document.Version, Is.EqualTo(TargetVersion));
        Assert.That(newerVersion, Is.Null);
        Assert.That(migrated.Count, Is.EqualTo(2));
    }

    [Test]
    public void Read_ANewerFile_ReportsItsVersionAndStillReadsWhatItCan()
    {
        // Act
        var (document, newerVersion, migrated) = ProfileStore.Read(NewerFile, TargetVersion, Steps);

        // Assert
        Assert.That(document!.SelectedProfileId, Is.EqualTo("p2"));
        Assert.That(newerVersion, Is.EqualTo(7));
        Assert.That(migrated, Is.Empty);
    }

    [Test]
    public void Save_AfterLoadingAFileFromANewerVersion_KeepsThatFileInTheBackups()
    {
        // Arrange
        var folder = Path.Combine(Path.GetTempPath(), "deckhost-migration-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var path = Path.Combine(folder, "profiles.json");
            File.WriteAllText(path, $$"""{ "version": {{FarFutureVersion}}, "selectedProfileId": null, "profiles": [] }""");
            var store = new ProfileStore(path);
            var document = store.Load()!;

            // Act
            store.Save(document);

            // Assert
            Assert.That(File.ReadAllText(Path.Combine(store.BackupDirectory, $"profiles-from-v{FarFutureVersion}.json")), Does.Contain(FarFutureVersion.ToString()));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Test]
    public void Save_AfterLoadingAFileFromANewerVersion_WritesTheCurrentVersionAndForgetsTheNewerOne()
    {
        // Arrange
        var folder = Path.Combine(Path.GetTempPath(), "deckhost-migration-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var path = Path.Combine(folder, "profiles.json");
            File.WriteAllText(path, $$"""{ "version": {{FarFutureVersion}}, "selectedProfileId": null, "profiles": [] }""");
            var store = new ProfileStore(path);
            var document = store.Load()!;
            var newerBefore = store.NewerVersionLoaded;

            // Act
            store.Save(document);

            // Assert
            Assert.That(newerBefore, Is.EqualTo(FarFutureVersion));
            Assert.That(ProfileMigrations.VersionOf(JsonNode.Parse(File.ReadAllText(path))!.AsObject()), Is.EqualTo(ProfileDocument.CurrentVersion));
            Assert.That(store.NewerVersionLoaded, Is.Null);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}

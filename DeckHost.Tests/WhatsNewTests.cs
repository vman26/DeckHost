using DeckHost.App.ViewModels;

namespace DeckHost.Tests;

public sealed class WhatsNewTests
{
    private const string Released = "1.2.0";
    private const string Newer = "1.3.0";
    private const string NewerPrerelease = "1.3.0-alpha.2+4b09090";
    private const string ReleasedNotes = "- A thing that shipped in 1.2.0.";
    private const string UnreleasedNotes = "- A thing still being made.";
    private const string CandidateNotes = "- The candidate's own notes.";
    private const string ReleasesPage = "https://github.com/example/DeckHost/releases";

    private static readonly string Changelog = $"""
        # Changelog

        Intro text that isn't a section.

        ## [Unreleased]

        ### Added

        {UnreleasedNotes}

        ## [1.3.0-rc.1] - 2026-11-20

        {CandidateNotes}

        ## [{Released}] - 2026-10-01

        ### Fixed

        {ReleasedNotes}

        [Unreleased]: https://github.com/example/DeckHost/compare/v1.2.0...HEAD
        [{Released}]: https://github.com/example/DeckHost/releases/tag/v1.2.0
        """;

    [Test]
    public void Decide_NoVersionSeenYet_RemembersWithoutShowing()
    {
        // Act
        var decision = WhatsNewViewModel.Decide(null, Released);

        // Assert
        Assert.That(decision, Is.EqualTo(WhatsNewDecision.Remember));
    }

    [Test]
    public void Decide_ANewerVersion_Shows()
    {
        // Act
        var decision = WhatsNewViewModel.Decide(Released, Newer);

        // Assert
        Assert.That(decision, Is.EqualTo(WhatsNewDecision.Show));
    }

    [Test]
    public void Decide_TheSameVersionWithAnotherCommit_ShowsNothing()
    {
        // Arrange
        const string Seen = "1.3.0-alpha.2";

        // Act
        var decision = WhatsNewViewModel.Decide(Seen, NewerPrerelease);

        // Assert
        Assert.That(decision, Is.EqualTo(WhatsNewDecision.Nothing));
    }

    [Test]
    public void Decide_AnOlderVersion_ShowsNothing()
    {
        // Act
        var decision = WhatsNewViewModel.Decide(Newer, Released);

        // Assert
        Assert.That(decision, Is.EqualTo(WhatsNewDecision.Nothing));
    }

    [Test]
    public void WithoutBuild_AVersionWithACommit_DropsTheCommitAndTheV()
    {
        // Act
        var version = WhatsNewViewModel.WithoutBuild("v" + NewerPrerelease);

        // Assert
        Assert.That(version, Is.EqualTo("1.3.0-alpha.2"));
    }

    [Test]
    public void NotesFor_AReleasedVersion_IsItsSectionWithoutTheLinkDefinitions()
    {
        // Act
        var notes = WhatsNewViewModel.NotesFor(Changelog, Released);

        // Assert
        Assert.That(notes, Is.EqualTo($"### Fixed\n\n{ReleasedNotes}"));
    }

    [Test]
    public void NotesFor_AReleaseCandidateWithASection_IsThatSection()
    {
        // Act
        var notes = WhatsNewViewModel.NotesFor(Changelog, "1.3.0-rc.1+abcdef0");

        // Assert
        Assert.That(notes, Is.EqualTo(CandidateNotes));
    }

    [Test]
    public void NotesFor_APrereleaseWithoutASection_FallsBackToUnreleased()
    {
        // Act
        var notes = WhatsNewViewModel.NotesFor(Changelog, NewerPrerelease);

        // Assert
        Assert.That(notes, Does.Contain(UnreleasedNotes));
    }

    [Test]
    public void NotesFor_APrereleaseOfAReleasedVersion_UsesTheBaseVersion()
    {
        // Act
        var notes = WhatsNewViewModel.NotesFor(Changelog, "1.2.0-beta.1");

        // Assert
        Assert.That(notes, Does.Contain(ReleasedNotes));
    }

    [Test]
    public void NotesFor_AReleaseWithoutASection_IsNull()
    {
        // Act
        var notes = WhatsNewViewModel.NotesFor(Changelog, Newer);

        // Assert
        Assert.That(notes, Is.Null);
    }

    [Test]
    public void Constructor_AVersionWithItsNotes_HasTheTitleAndTheNotes()
    {
        // Act
        var page = new WhatsNewViewModel(Released, Changelog, ReleasesPage);

        // Assert
        Assert.That((page.Title, page.HasNotes), Is.EqualTo(($"New in DeckHost {Released}", true)));
    }

    [Test]
    public void ReadEmbeddedChangelog_TheBuiltApp_HasAnUnreleasedSection()
    {
        // Act
        var changelog = WhatsNewViewModel.ReadEmbeddedChangelog();

        // Assert
        Assert.That(changelog, Is.Not.Null);
        Assert.That(WhatsNewViewModel.Sections(changelog!).ContainsKey("Unreleased"), Is.True);
    }
}

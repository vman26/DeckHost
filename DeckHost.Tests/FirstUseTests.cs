using Avalonia.Controls;
using DeckHost.App.Platform;
using DeckHost.App.SystemPlugins;
using DeckHost.App.ViewModels;
using DeckHost.Contracts.Enums;
using DeckHost.Contracts.Models;

namespace DeckHost.Tests;

/// <summary>The first close to the tray: DeckHost says once that it keeps running.</summary>
public class TrayCloseNoticeTests
{
    private static readonly AppSettingsData TrayOn = new(CloseToTray: true);

    [Test]
    public void Decide_FirstCloseToTheTray_AsksFirst()
    {
        // Arrange
        var settings = TrayOn;

        // Act
        var outcome = TrayCloseNotice.Decide(WindowCloseReason.WindowClosing, exiting: false, settings);

        // Assert
        Assert.That(outcome, Is.EqualTo(WindowCloseOutcome.AskFirst));
    }

    [Test]
    public void Decide_NoticeShownBefore_HidesToTheTraySilently()
    {
        // Arrange
        var settings = TrayOn with { TrayNoticeShown = true };

        // Act
        var outcome = TrayCloseNotice.Decide(WindowCloseReason.WindowClosing, exiting: false, settings);

        // Assert
        Assert.That(outcome, Is.EqualTo(WindowCloseOutcome.HideToTray));
    }

    [Test]
    public void Decide_CloseToTrayOff_Closes()
    {
        // Arrange
        var settings = new AppSettingsData(CloseToTray: false);

        // Act
        var outcome = TrayCloseNotice.Decide(WindowCloseReason.WindowClosing, exiting: false, settings);

        // Assert
        Assert.That(outcome, Is.EqualTo(WindowCloseOutcome.Close));
    }

    [TestCase(WindowCloseReason.OSShutdown, false)]
    [TestCase(WindowCloseReason.ApplicationShutdown, false)]
    [TestCase(WindowCloseReason.WindowClosing, true)]
    public void Decide_ShutdownOrExiting_ClosesWithoutAsking(WindowCloseReason reason, bool exiting)
    {
        // Arrange
        var settings = TrayOn;

        // Act
        var outcome = TrayCloseNotice.Decide(reason, exiting, settings);

        // Assert
        Assert.That(outcome, Is.EqualTo(WindowCloseOutcome.Close));
    }

    [Test]
    public void Answer_KeepRunning_RemembersTheNoticeAndKeepsTheTray()
    {
        // Arrange
        var settings = TrayOn;

        // Act
        var answered = TrayCloseNotice.Answer(settings, keepRunning: true);

        // Assert
        Assert.That(answered, Is.EqualTo(TrayOn with { TrayNoticeShown = true }));
    }

    [Test]
    public void Answer_Quit_RemembersTheNoticeAndTurnsCloseToTrayOff()
    {
        // Arrange
        var settings = TrayOn;

        // Act
        var answered = TrayCloseNotice.Answer(settings, keepRunning: false);

        // Assert
        Assert.That(answered, Is.EqualTo(TrayOn with { TrayNoticeShown = true, CloseToTray = false }));
    }
}

/// <summary>The tips over the main window after the welcome tour.</summary>
public class CoachMarksViewModelTests
{
    private const double Gap = 12;
    private static readonly Avalonia.Size Window = new(1440, 920);
    private static readonly Avalonia.Size Card = new(300, 120);

    [Test]
    public void Marks_InOrder_PointAtActionsThenDeviceThenPickers()
    {
        // Arrange (the fixed tips)

        // Act
        var targets = CoachMarksViewModel.Marks.Select(static mark => mark.Target);

        // Assert
        Assert.That(targets, Is.EqualTo(new[] { CoachMarkTarget.Actions, CoachMarkTarget.Device, CoachMarkTarget.Pickers }));
    }

    [Test]
    public void Constructor_NewTips_StartAtTheActionList()
    {
        // Arrange

        // Act
        var marks = new CoachMarksViewModel();

        // Assert
        Assert.That((marks.Current.Target, marks.StepText, marks.NextLabel), Is.EqualTo((CoachMarkTarget.Actions, "1 of 3", "Next")));
    }

    [Test]
    public void Next_ToTheLastTip_ShowsThePickersWithDone()
    {
        // Arrange
        var marks = new CoachMarksViewModel();

        // Act
        marks.Next();
        marks.Next();

        // Assert
        Assert.That((marks.Current.Target, marks.IsLast, marks.NextLabel), Is.EqualTo((CoachMarkTarget.Pickers, true, "Done")));
    }

    [Test]
    public void Next_OnTheLastTip_FinishesOnce()
    {
        // Arrange
        var marks = new CoachMarksViewModel();
        var finished = 0;
        marks.Finished += (_, _) => finished++;
        foreach (var _ in CoachMarksViewModel.Marks)
        {
            marks.Next();
        }

        // Act
        marks.Next();

        // Assert
        Assert.That((finished, marks.IsFinished), Is.EqualTo((1, true)));
    }

    [Test]
    public void Skip_OnTheFirstTip_FinishesOnce()
    {
        // Arrange
        var marks = new CoachMarksViewModel();
        var finished = 0;
        marks.Finished += (_, _) => finished++;

        // Act
        marks.Skip();
        marks.Skip();

        // Assert
        Assert.That((finished, marks.IsFinished, marks.Index), Is.EqualTo((1, true, 0)));
    }

    [Test]
    public void PlaceCard_TallListOnTheRight_GoesLeftOfIt()
    {
        // Arrange
        var list = new Avalonia.Rect(1120, 60, 320, 860);

        // Act
        var place = CoachMarksViewModel.PlaceCard(list, Card, Window, Gap);

        // Assert
        Assert.That(place, Is.EqualTo(new Avalonia.Point(list.X - Gap - Card.Width, list.Y + Gap)));
    }

    [Test]
    public void PlaceCard_HeaderPicker_GoesBelowIt()
    {
        // Arrange
        var picker = new Avalonia.Rect(70, 8, 240, 44);

        // Act
        var place = CoachMarksViewModel.PlaceCard(picker, Card, Window, Gap);

        // Assert
        Assert.That(place.Y, Is.EqualTo(picker.Bottom + Gap));
    }

    [Test]
    public void PlaceCard_TargetAtTheLeftEdge_StaysInsideTheWindow()
    {
        // Arrange
        var picker = new Avalonia.Rect(0, 8, 100, 44);

        // Act
        var place = CoachMarksViewModel.PlaceCard(picker, Card, Window, Gap);

        // Assert
        Assert.That(place.X, Is.EqualTo(Gap));
    }
}

/// <summary>The empty layout's hint: "Drag an action here", until the first binding.</summary>
public class EmptyDeckHintTests
{
    private static ControlItemViewModel Key(string id) => new(new PhysicalControl(id, id, ControlType.Key, new Rect(0, 0, 10, 10),
        InputCapabilities.Press | InputCapabilities.Release, OutputCapabilities.ColorImage | OutputCapabilities.Text, 112, 112));

    [Test]
    public void IsEmptyDeck_NoControlHasABinding_IsTrue()
    {
        // Arrange
        var controls = new[] { Key("key.0"), Key("key.1") };

        // Act
        var empty = MainViewModel.IsEmptyDeck(controls);

        // Assert
        Assert.That(empty, Is.True);
    }

    [Test]
    public void IsEmptyDeck_OneControlHasABinding_IsFalse()
    {
        // Arrange
        var play = SystemPluginSet.Create()
            .SelectMany(plugin => plugin.Actions.Select(action => ActionItemViewModel.Create(plugin.Id, plugin.Name, action)))
            .First(action => action.Name == "Play / Pause");
        var bound = Key("key.1");
        bound.AssignedBinding = new ControlBindingViewModel(
            new ControlBinding(bound.Id, ProfileActionKind.Single, play.ToActionReference(), new ControlAppearance(), []), play, []);
        var controls = new[] { Key("key.0"), bound };

        // Act
        var empty = MainViewModel.IsEmptyDeck(controls);

        // Assert
        Assert.That(empty, Is.False);
    }

    [Test]
    public void IsEmptyDeck_NoControls_IsFalse()
    {
        // Arrange
        var controls = Array.Empty<ControlItemViewModel>();

        // Act
        var empty = MainViewModel.IsEmptyDeck(controls);

        // Assert
        Assert.That(empty, Is.False);
    }
}

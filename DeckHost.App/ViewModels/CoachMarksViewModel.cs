using System;
using System.Collections.Generic;
using Avalonia;
using CommunityToolkit.Mvvm.Input;

namespace DeckHost.App.ViewModels;

/// <summary>The part of the main window a coach mark points at.</summary>
public enum CoachMarkTarget
{
    /// <summary>The action list on the right.</summary>
    Actions,

    /// <summary>The device canvas.</summary>
    Device,

    /// <summary>The header's device and profile pickers.</summary>
    Pickers,
}

/// <summary>One tip over the main window: what it points at and what it says.</summary>
public sealed record CoachMark(CoachMarkTarget Target, string Text);

/// <summary>
/// The tips shown over the real main window after the welcome tour, one at a time: where the
/// actions are, where they go, and where profiles, pages and devices are picked. Next goes on,
/// Skip (or Esc) ends them; the main window draws the highlight and the card.
/// </summary>
public sealed partial class CoachMarksViewModel : ViewModelBase
{
    public static readonly IReadOnlyList<CoachMark> Marks =
    [
        new(CoachMarkTarget.Actions, "Drag an action from here…"),
        new(CoachMarkTarget.Device, "…onto a key, knob or touch zone. Click a control to set it up below."),
        new(CoachMarkTarget.Pickers, "Profiles, pages and devices live here."),
    ];

    private int _index;
    private bool _isFinished;

    /// <summary>Raised once, when the last tip was read or the tips were skipped.</summary>
    public event EventHandler? Finished;

    public int Index
    {
        get => _index;
        private set
        {
            if (SetProperty(ref _index, value))
            {
                foreach (var name in new[] { nameof(Current), nameof(Text), nameof(StepText), nameof(IsLast), nameof(NextLabel) })
                {
                    OnPropertyChanged(name);
                }
            }
        }
    }

    public CoachMark Current => Marks[_index];

    public string Text => Current.Text;

    public string StepText => $"{_index + 1} of {Marks.Count}";

    public bool IsLast => _index == Marks.Count - 1;

    public string NextLabel => IsLast ? "Done" : "Next";

    public bool IsFinished
    {
        get => _isFinished;
        private set => SetProperty(ref _isFinished, value);
    }

    /// <summary>The next tip, or the end after the last one.</summary>
    [RelayCommand]
    public void Next()
    {
        if (IsFinished)
        {
            return;
        }

        if (IsLast)
        {
            Finish();
            return;
        }

        Index++;
    }

    /// <summary>Ends the tips now (Skip, Esc).</summary>
    [RelayCommand]
    public void Skip()
    {
        if (!IsFinished)
        {
            Finish();
        }
    }

    private void Finish()
    {
        IsFinished = true;
        Finished?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Where the card goes, in the overlay's coordinates: below the target when it fits (the header,
    /// the device), else beside it (the tall action list: on its left), else above; always inside the area.
    /// </summary>
    public static Point PlaceCard(Rect target, Size card, Size area, double gap)
    {
        double x;
        double y;
        if (target.Bottom + gap + card.Height <= area.Height && target.Height <= area.Height / 2)
        {
            x = target.X + (target.Width - card.Width) / 2;
            y = target.Bottom + gap;
        }
        else if (target.X - gap - card.Width >= 0)
        {
            x = target.X - gap - card.Width;
            y = target.Y + gap;
        }
        else if (target.Right + gap + card.Width <= area.Width)
        {
            x = target.Right + gap;
            y = target.Y + gap;
        }
        else
        {
            x = target.X + (target.Width - card.Width) / 2;
            y = target.Y - gap - card.Height;
        }

        return new Point(
            Math.Clamp(x, gap, Math.Max(gap, area.Width - card.Width - gap)),
            Math.Clamp(y, gap, Math.Max(gap, area.Height - card.Height - gap)));
    }
}

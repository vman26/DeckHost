using System;
using System.Collections.Generic;
using Avalonia;

namespace DeckHost.App.ViewModels;

/// <summary>Which way an arrow key moves the keyboard focus on the device canvas.</summary>
public enum CanvasDirection
{
    Left,
    Right,
    Up,
    Down,
}

/// <summary>
/// Keyboard navigation between the controls on the device canvas (pure): an arrow key goes to the
/// nearest control whose center lies that way, preferring ones in the same row or column.
/// </summary>
public static class CanvasNavigation
{
    /// <summary>The index of the control to go to from <paramref name="current"/>, or null when there is none that way.</summary>
    public static int? Next(IReadOnlyList<Rect> controls, int current, CanvasDirection direction)
    {
        if (current < 0 || current >= controls.Count)
        {
            return controls.Count > 0 ? 0 : null;
        }

        var from = controls[current].Center;
        int? best = null;
        var bestScore = double.MaxValue;
        for (var index = 0; index < controls.Count; index++)
        {
            if (index == current)
            {
                continue;
            }

            var to = controls[index].Center;
            var (along, across) = direction switch
            {
                CanvasDirection.Left => (from.X - to.X, Math.Abs(to.Y - from.Y)),
                CanvasDirection.Right => (to.X - from.X, Math.Abs(to.Y - from.Y)),
                CanvasDirection.Up => (from.Y - to.Y, Math.Abs(to.X - from.X)),
                _ => (to.Y - from.Y, Math.Abs(to.X - from.X)),
            };

            if (along <= 0.5)
            {
                continue;
            }

            // Sideways distance counts more, so the focus stays in its row or column.
            var score = along + (across * 3);
            if (score < bestScore)
            {
                bestScore = score;
                best = index;
            }
        }

        return best;
    }
}

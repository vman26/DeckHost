namespace DeckHost.Contracts.Models;

/// <summary>
/// Visual appearance configuration for a control.
/// </summary>
public sealed record ControlAppearance(
    /// <summary>
    /// Label text to display on the control.
    /// </summary>
    string? Text = null,

    /// <summary>
    /// Path to an image file to display.
    /// </summary>
    string? ImagePath = null,

    /// <summary>
    /// RGB LED feedback configuration (optional).
    /// </summary>
    RgbFeedback? RgbFeedback = null);

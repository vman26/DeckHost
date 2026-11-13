namespace DeckHost.Core.Models;

/// <summary>
/// What the host saw of a device since DeckHost started: inputs, images and lights sent, failures and
/// the last error. For the Help page and support bundles ("does the device talk to us at all?").
/// </summary>
public sealed record DeviceStatistics(
    string DeviceId,
    long Inputs,
    DateTimeOffset? LastInput,
    long FramesSent,
    long FrameFailures,
    long LightFailures,
    string? LastError,
    DateTimeOffset? LastErrorTime);

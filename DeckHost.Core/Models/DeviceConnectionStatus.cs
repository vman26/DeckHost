namespace DeckHost.Core.Models;

public sealed record DeviceConnectionStatus(
    string DeviceId,
    string State,
    string Message);

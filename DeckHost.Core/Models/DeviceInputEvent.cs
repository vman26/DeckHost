using DeckHost.Contracts.Enums;

namespace DeckHost.Core.Models;

public sealed record DeviceInputEvent(
    string DeviceId,
    string ControlId,
    InputCapabilities EventType,
    int Value);

namespace DeckHost.Core.Models;

public sealed record RuntimeLogEntry(
    DateTimeOffset Timestamp,
    string Level,
    string Message);

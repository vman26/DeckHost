using System;
using System.IO;
using System.Text.Json;

namespace DeckHost.App.Platform;

/// <summary>What <see cref="StartupGuard.BeginRun"/> decided for this start.</summary>
internal sealed record StartupDecision(bool SafeMode, string Reason, int UnhealthyRuns);

/// <summary>
/// Remembers whether the last runs of DeckHost ended well, in <c>startup-state.json</c>, so a crash at start
/// (managed or native) can't repeat forever: after two failed runs, or one that failed within a minute of
/// starting, DeckHost starts in safe mode. A run is healthy once it stayed up (<see cref="MarkHealthy"/>).
/// </summary>
internal sealed class StartupGuard(string directory, Func<DateTimeOffset>? now = null)
{
    public const string FileName = "startup-state.json";
    public const int UnhealthyRunsForSafeMode = 2;

    /// <summary>How long a run has to stay up to count as healthy.</summary>
    public static readonly TimeSpan HealthyAfter = TimeSpan.FromSeconds(20);

    /// <summary>A failure this soon after starting is a crash at start, not a crash during use.</summary>
    public static readonly TimeSpan QuickFailure = TimeSpan.FromSeconds(60);

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    private readonly Func<DateTimeOffset> _now = now ?? (() => DateTimeOffset.UtcNow);

    private string FilePath => Path.Combine(directory, FileName);

    /// <summary>Called first thing at start: counts the previous run when it didn't end well, then marks this one as running.</summary>
    public StartupDecision BeginRun(bool forceSafeMode = false)
    {
        var state = Load();
        var previousFailed = state.Running || state.Crashed;
        var failedQuickly = state.Crashed && state.StartedAt is { } started && state.CrashedAt is { } crashedAt && crashedAt - started < QuickFailure;
        if (previousFailed)
        {
            state.UnhealthyRuns++;
        }

        var reason = forceSafeMode ? "It was started in safe mode."
            : failedQuickly ? "DeckHost closed unexpectedly right after it started."
            : state.UnhealthyRuns >= UnhealthyRunsForSafeMode ? "DeckHost closed unexpectedly the last times it started."
            : string.Empty;

        state.Running = true;
        state.Crashed = false;
        state.StartedAt = _now();
        state.CrashedAt = null;
        Save(state);
        return new StartupDecision(reason.Length > 0, reason, state.UnhealthyRuns);
    }

    /// <summary>This run stayed up: the failures before it no longer count.</summary>
    public void MarkHealthy() => Update(state =>
    {
        state.UnhealthyRuns = 0;
        state.Running = false;
    });

    /// <summary>DeckHost is closing normally (not a crash, not proof of health either).</summary>
    public void MarkCleanExit() => Update(static state => state.Running = false);

    /// <summary>An unhandled exception is taking this run down.</summary>
    public void MarkCrashed() => Update(state =>
    {
        state.Crashed = true;
        state.CrashedAt = _now();
    });

    /// <summary>Forgets every earlier failure (the "Restart normally" button).</summary>
    public void ClearFailures() => Update(static state =>
    {
        state.UnhealthyRuns = 0;
        state.Crashed = false;
        state.Running = false;
    });

    private void Update(Action<StartupState> change)
    {
        var state = Load();
        change(state);
        Save(state);
    }

    private StartupState Load()
    {
        try
        {
            return File.Exists(FilePath) ? JsonSerializer.Deserialize<StartupState>(File.ReadAllText(FilePath)) ?? new StartupState() : new StartupState();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return new StartupState();
        }
    }

    private void Save(StartupState state)
    {
        try
        {
            Directory.CreateDirectory(directory);
            var temporary = FilePath + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(state, Options));
            File.Move(temporary, FilePath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Not being able to remember a run must never stop DeckHost from starting.
        }
    }

    internal sealed class StartupState
    {
        public int UnhealthyRuns { get; set; }

        public bool Running { get; set; }

        public bool Crashed { get; set; }

        public DateTimeOffset? StartedAt { get; set; }

        public DateTimeOffset? CrashedAt { get; set; }
    }
}

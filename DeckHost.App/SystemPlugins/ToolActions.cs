using DeckHost.App.Platform;
using DeckHost.Contracts;
using DeckHost.Contracts.Enums;
using DeckHost.Contracts.Interfaces;
using DeckHost.Contracts.Models;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;

namespace DeckHost.App.SystemPlugins;

/// <summary>
/// A countdown or a stopwatch on a key: press to start or pause, hold to reset. A countdown can
/// play a sound when it reaches zero.
/// </summary>
internal sealed class TimerAction() : DeckActionBase(
    "builtin.timer",
    "Timer",
    InputCapabilities.Press | InputCapabilities.LongPress,
    OutputCapabilities.Text,
    "A countdown or a stopwatch: press to start or pause, hold to reset.")
{
    private const string ModeKey = "mode";
    private const string Countdown = "countdown";
    private const string StopwatchMode = "stopwatch";
    private const string MinutesKey = "minutes";
    private const string SecondsKey = "seconds";
    private const string SoundKey = "sound";
    private const uint RunningColor = 0xFF34D399;
    private const uint DoneColor = 0xFFF87171;

    private readonly ConcurrentDictionary<string, TimerState> _timers = new(StringComparer.Ordinal);

    protected override string? PaletteIcon => Icons.Stopwatch;

    protected override IReadOnlyList<ActionSettingDefinition> OwnSettings { get; } =
    [
        new(ModeKey, "Counts", ActionSettingKind.Choice, Countdown, Options: [new(Countdown, "Down (countdown)"), new(StopwatchMode, "Up (stopwatch)")]),
        new(MinutesKey, "Minutes", ActionSettingKind.Number, "5") { Range = new(0, 180), ShowWhen = new(ModeKey, Countdown) },
        new(SecondsKey, "Seconds", ActionSettingKind.Number, "0") { Range = new(0, 59), ShowWhen = new(ModeKey, Countdown) },
        new(SoundKey, "Sound at zero", ActionSettingKind.FilePath, Placeholder: "Optional .wav or .mp3") { ShowWhen = new(ModeKey, Countdown) },
    ];

    public override bool CanBeMultiActionStep => false;

    public override TimeSpan? RefreshInterval => TimeSpan.FromMilliseconds(500);

    private static string StateKey(string deviceId, string controlId) => $"{deviceId}|{controlId}";

    internal static TimeSpan Duration(ActionSettings settings) =>
        TimeSpan.FromMinutes(settings.GetInt(MinutesKey, 5)) + TimeSpan.FromSeconds(settings.GetInt(SecondsKey, 0));

    protected override void Run(ActionContext context)
    {
        var id = StateKey(context.DeviceId, context.ControlId);
        var state = _timers.GetOrAdd(id, static _ => new TimerState());
        var now = Environment.TickCount64;
        lock (state)
        {
            state.Alarm?.Dispose();
            state.Alarm = null;
            if (context.EventType == InputCapabilities.LongPress)
            {
                state.Reset();
            }
            else if (state.IsRunning)
            {
                state.Pause(now);
            }
            else
            {
                state.Start(now);
            }

            if (state.IsRunning && context.Settings.Get(ModeKey, Countdown) == Countdown)
            {
                var left = Duration(context.Settings) - state.Elapsed(now);
                var sound = context.Settings.Get(SoundKey).Trim().Trim('"');
                var host = context.Host;
                var (deviceId, controlId) = (context.DeviceId, context.ControlId);
                state.Alarm = new Timer(
                    _ =>
                    {
                        if (sound.Length > 0)
                        {
                            try
                            {
                                SoundPlayer.Play($"timer|{id}", sound, 100);
                            }
                            catch (Exception ex) when (ex is IOException or InvalidOperationException or PlatformNotSupportedException or System.ComponentModel.Win32Exception)
                            {
                                // The key shows the countdown ended either way.
                            }
                        }

                        host.RequestRender(deviceId, controlId);
                    },
                    null,
                    left > TimeSpan.Zero ? left : TimeSpan.Zero,
                    Timeout.InfiniteTimeSpan);
            }
        }
    }

    public override void Render(IRenderContext context)
    {
        var now = Environment.TickCount64;
        var countdown = context.Settings.Get(ModeKey, Countdown) == Countdown;
        _timers.TryGetValue(StateKey(context.DeviceId, context.ControlId), out var state);
        var elapsed = TimeSpan.Zero;
        var running = false;
        if (state is not null)
        {
            lock (state)
            {
                elapsed = state.Elapsed(now);
                running = state.IsRunning;
            }
        }

        string value;
        double? fraction = null;
        var color = running ? RunningColor : SecondaryTextColor;
        if (countdown)
        {
            var total = Duration(context.Settings);
            var left = total - elapsed;
            if (left <= TimeSpan.Zero && state is not null && (running || elapsed > TimeSpan.Zero))
            {
                // Blinks at zero until it's reset.
                value = (now / 500) % 2 == 0 ? "0:00" : string.Empty;
                color = DoneColor;
                fraction = 1;
            }
            else
            {
                value = Format(left < TimeSpan.Zero ? TimeSpan.Zero : left, roundUp: true);
                fraction = total > TimeSpan.Zero ? elapsed / total : null;
            }
        }
        else
        {
            value = Format(elapsed, roundUp: false);
        }

        var label = ResolveDisplayText(context, running ? (countdown ? "Running" : "Stopwatch") : elapsed > TimeSpan.Zero ? "Paused" : countdown ? "Timer" : "Stopwatch");
        context.DrawReading(label, value, fraction, color, ResolveIcon(context, Icons.Stopwatch));
    }

    /// <summary>"4:59", "1:02:03"; a countdown rounds up so it shows 0:00 only at the end.</summary>
    internal static string Format(TimeSpan time, bool roundUp)
    {
        var seconds = (long)(roundUp ? Math.Ceiling(time.TotalSeconds - 0.0005) : Math.Floor(time.TotalSeconds));
        var hours = seconds / 3600;
        var minutes = (seconds / 60) % 60;
        var rest = seconds % 60;
        return hours > 0
            ? string.Create(CultureInfo.InvariantCulture, $"{hours}:{minutes:00}:{rest:00}")
            : string.Create(CultureInfo.InvariantCulture, $"{minutes + (hours * 60)}:{rest:00}");
    }

    /// <summary>One key's timer: time counted before the last start, and when it was started (ticks), if it runs.</summary>
    internal sealed class TimerState
    {
        private TimeSpan _before;
        private long? _startedAt;

        public Timer? Alarm { get; set; }

        public bool IsRunning => _startedAt is not null;

        public TimeSpan Elapsed(long now) => _before + (_startedAt is { } start ? TimeSpan.FromMilliseconds(now - start) : TimeSpan.Zero);

        public void Start(long now) => _startedAt ??= now;

        public void Pause(long now)
        {
            _before = Elapsed(now);
            _startedAt = null;
        }

        public void Reset()
        {
            _before = TimeSpan.Zero;
            _startedAt = null;
        }
    }
}

/// <summary>
/// Runs a program with arguments (never through a shell unless you start one), waits up to a
/// timeout and can show its result on the key or save its output in a variable.
/// </summary>
internal sealed class RunCommandAction() : DeckActionBase(
    "system.command",
    "Run Command",
    InputCapabilities.Press,
    OutputCapabilities.None,
    "Runs a program or script in the background and shows whether it worked; can save its output in a variable.")
{
    private const string ProgramKey = "program";
    private const string ArgumentsKey = "arguments";
    private const string FolderKey = "folder";
    private const string VariableKey = "variable";
    private const string TimeoutKey = "timeout";
    private static readonly TimeSpan ResultShownFor = TimeSpan.FromSeconds(4);

    private readonly ConcurrentDictionary<string, (string Text, long Until)> _results = new(StringComparer.Ordinal);

    protected override IReadOnlyList<ActionSettingDefinition> OwnSettings { get; } =
    [
        new(ProgramKey, "Program", ActionSettingKind.FilePath, Placeholder: "python3, powershell.exe, /usr/bin/backup.sh",
            Description: "Runs the program itself, not a shell. For shell features (pipes, &&) run the shell: cmd with /c …, or sh with -c …"),
        new(ArgumentsKey, "Arguments", Placeholder: "--fast \"a file.txt\"", Description: "Separated by spaces; put an argument with spaces in double quotes."),
        new(FolderKey, "Start in", Placeholder: "Optional folder"),
        new(TimeoutKey, "Stop it after", ActionSettingKind.Number, "30") { Range = new(1, 600) { Unit = "s" } },
        new(VariableKey, "Save the output in", ActionSettingKind.Variable, Placeholder: "Optional variable name", Description: "The first 4000 characters of what it prints, for Show Variable and workflows."),
    ];

    private static string StateKey(string deviceId, string controlId) => $"{deviceId}|{controlId}";

    public override TimeSpan? RefreshInterval => TimeSpan.FromSeconds(1);

    protected override void Run(ActionContext context)
    {
        var program = context.Settings.Get(ProgramKey).Trim().Trim('"');
        var arguments = SplitArguments(context.Settings.Get(ArgumentsKey));
        var folder = context.Settings.Get(FolderKey).Trim();
        var timeout = TimeSpan.FromSeconds(Math.Clamp(context.Settings.GetInt(TimeoutKey, 30), 1, 600));
        var variable = context.Settings.Get(VariableKey).Trim();
        var id = StateKey(context.DeviceId, context.ControlId);
        _results[id] = ("Running…", long.MaxValue);
        RunInBackground(context, () =>
        {
            try
            {
                if (program.Length == 0)
                {
                    throw new InvalidOperationException("Choose a program in Customize.");
                }

                var (exitCode, output) = Execute(program, arguments, folder, timeout);
                if (variable.Length > 0)
                {
                    context.Variables.Publish(VariableKeys.UserVariable(variable), output.Length > 4000 ? output[..4000] : output.TrimEnd());
                }

                _results[id] = (exitCode == 0 ? "Done" : $"Exit {exitCode}", Environment.TickCount64 + (long)ResultShownFor.TotalMilliseconds);
                if (exitCode != 0)
                {
                    throw new InvalidOperationException($"It ended with code {exitCode}.");
                }
            }
            catch
            {
                _results.TryRemove(id, out _);
                throw;
            }
        });
    }

    private static (int ExitCode, string Output) Execute(string program, IReadOnlyList<string> arguments, string folder, TimeSpan timeout)
    {
        var startInfo = new ProcessStartInfo(program)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        if (folder.Length > 0)
        {
            startInfo.WorkingDirectory = folder;
        }

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("The program did not start.");
        var output = process.StandardOutput.ReadToEndAsync();
        _ = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(timeout))
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
            }

            throw new TimeoutException($"Stopped after {timeout.TotalSeconds:0} s.");
        }

        return (process.ExitCode, output.Result);
    }

    /// <summary>Splits on spaces, keeping "double quoted" parts together (pure, for the tests).</summary>
    internal static IReadOnlyList<string> SplitArguments(string text)
    {
        var arguments = new List<string>();
        var current = new StringBuilder();
        var quoted = false;
        var started = false;
        foreach (var character in text ?? string.Empty)
        {
            if (character == '"')
            {
                quoted = !quoted;
                started = true;
            }
            else if (char.IsWhiteSpace(character) && !quoted)
            {
                if (started)
                {
                    arguments.Add(current.ToString());
                    current.Clear();
                    started = false;
                }
            }
            else
            {
                current.Append(character);
                started = true;
            }
        }

        if (started)
        {
            arguments.Add(current.ToString());
        }

        return arguments;
    }

    protected override string Title(IRenderContext context)
    {
        var program = context.Settings.Get(ProgramKey);
        return string.IsNullOrWhiteSpace(program) ? Name : Path.GetFileNameWithoutExtension(program.Trim().Trim('"'));
    }

    protected override string? Subtitle(IRenderContext context) => string.IsNullOrWhiteSpace(context.Settings.Get(ProgramKey)) ? "Not set" : null;

    protected override string? Icon(IRenderContext context) => Icons.Terminal;

    protected override (uint From, uint To) TileColors => Palette.Tools;

    protected override string? Caption(IRenderContext context) =>
        _results.TryGetValue(StateKey(context.DeviceId, context.ControlId), out var result) && result.Until > Environment.TickCount64
            ? result.Text
            : Title(context);
}

/// <summary>Sends a web request (a webhook, a smart-home or a local API) and shows the status it got back.</summary>
internal sealed class HttpRequestAction() : DeckActionBase(
    "system.http",
    "Web Request",
    InputCapabilities.Press,
    OutputCapabilities.None,
    "Sends a GET, POST, PUT or DELETE to an address (webhooks, Home Assistant, local APIs) and can save the answer in a variable.")
{
    private const string MethodKey = "method";
    private const string UrlKey = "url";
    private const string BodyKey = "body";
    private const string ContentTypeKey = "contentType";
    private const string HeadersKey = "headers";
    private const string VariableKey = "variable";
    private static readonly TimeSpan ResultShownFor = TimeSpan.FromSeconds(4);
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(15) };

    private readonly ConcurrentDictionary<string, (string Text, long Until)> _results = new(StringComparer.Ordinal);

    protected override IReadOnlyList<ActionSettingDefinition> OwnSettings { get; } =
    [
        new(MethodKey, "Method", ActionSettingKind.Choice, "POST", Options: [new("GET", "GET"), new("POST", "POST"), new("PUT", "PUT"), new("PATCH", "PATCH"), new("DELETE", "DELETE")]),
        new(UrlKey, "Address", Placeholder: "https://example.com/hook"),
        new(BodyKey, "Body", Placeholder: "{\"on\": true}", Description: "Sent with POST, PUT and PATCH."),
        new(ContentTypeKey, "Content type", ActionSettingKind.Choice, "application/json", Options: [new("application/json", "JSON"), new("text/plain", "Text"), new("application/x-www-form-urlencoded", "Form")]),
        new(HeadersKey, "Headers", ActionSettingKind.Secret, Placeholder: "Authorization: Bearer …", Description: "One per line, or several separated by ';;'. Kept like a password."),
        new(VariableKey, "Save the answer in", ActionSettingKind.Variable, Placeholder: "Optional variable name"),
    ];

    private static string StateKey(string deviceId, string controlId) => $"{deviceId}|{controlId}";

    public override TimeSpan? RefreshInterval => TimeSpan.FromSeconds(1);

    protected override void Run(ActionContext context)
    {
        var method = context.Settings.Get(MethodKey, "POST");
        var url = context.Settings.Get(UrlKey).Trim();
        var body = context.Settings.Get(BodyKey);
        var contentType = context.Settings.Get(ContentTypeKey, "application/json");
        var headers = ParseHeaders(context.Settings.Get(HeadersKey));
        var variable = context.Settings.Get(VariableKey).Trim();
        var id = StateKey(context.DeviceId, context.ControlId);
        RunInBackground(context, () =>
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            {
                throw new InvalidOperationException("Enter an http:// or https:// address.");
            }

            using var request = new HttpRequestMessage(new HttpMethod(method), uri);
            if (method is "POST" or "PUT" or "PATCH")
            {
                request.Content = new StringContent(body, Encoding.UTF8, contentType);
            }

            foreach (var (name, value) in headers)
            {
                request.Headers.TryAddWithoutValidation(name, value);
            }

            using var response = Client.Send(request);
            var text = new StreamReader(response.Content.ReadAsStream()).ReadToEnd();
            if (variable.Length > 0)
            {
                context.Variables.Publish(VariableKeys.UserVariable(variable), text.Length > 4000 ? text[..4000] : text);
            }

            var status = (int)response.StatusCode;
            _results[id] = (status.ToString(CultureInfo.InvariantCulture), Environment.TickCount64 + (long)ResultShownFor.TotalMilliseconds);
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"{status} {response.ReasonPhrase}");
            }
        });
    }

    /// <summary>"Name: value" lines (or ";;"-separated); lines without a colon are skipped (pure, for the tests).</summary>
    internal static IReadOnlyList<(string Name, string Value)> ParseHeaders(string text) =>
        (text ?? string.Empty)
            .Split(["\r\n", "\n", ";;"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(static line => line.Split(':', 2, StringSplitOptions.TrimEntries))
            .Where(static parts => parts.Length == 2 && parts[0].Length > 0)
            .Select(static parts => (parts[0], parts[1]))
            .ToList();

    protected override string Title(IRenderContext context) =>
        Uri.TryCreate(context.Settings.Get(UrlKey).Trim(), UriKind.Absolute, out var uri) ? uri.Host : Name;

    protected override string? Subtitle(IRenderContext context) => string.IsNullOrWhiteSpace(context.Settings.Get(UrlKey)) ? "Not set" : null;

    protected override string? Icon(IRenderContext context) => Icons.Globe;

    protected override (uint From, uint To) TileColors => Palette.Web;

    protected override string? Caption(IRenderContext context) =>
        _results.TryGetValue(StateKey(context.DeviceId, context.ControlId), out var result) && result.Until > Environment.TickCount64
            ? result.Text
            : Title(context);
}

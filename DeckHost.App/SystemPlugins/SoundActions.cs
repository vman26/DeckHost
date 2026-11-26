using DeckHost.App.Platform;
using DeckHost.Contracts;
using DeckHost.Contracts.Enums;
using DeckHost.Contracts.Interfaces;
using DeckHost.Contracts.Models;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;

namespace DeckHost.App.SystemPlugins;

/// <summary>A soundboard key: plays a file; pressing it again stops (or restarts) it.</summary>
internal sealed class PlaySoundAction : DeckActionBase
{
    private const string FileKey = "file";
    private const string VolumeKey = "volume";
    private const string AgainKey = "again";
    private const string DeviceKey = "device";
    private const string Stop = "stop";
    private const string Restart = "restart";

    /// <summary>The control to redraw when a sound ends, by sound id.</summary>
    private readonly ConcurrentDictionary<string, (IActionHost Host, string DeviceId, string ControlId)> _owners = new(StringComparer.Ordinal);

    public PlaySoundAction()
        : base("system.sound", "Play Sound", InputCapabilities.Press, OutputCapabilities.None, "Plays a sound file (WAV, MP3, …), like a soundboard. Press again to stop it.")
    {
        SoundPlayer.Stopped += id =>
        {
            if (_owners.TryRemove(id, out var owner))
            {
                owner.Host.RequestRender(owner.DeviceId, owner.ControlId);
            }
        };
    }

    protected override IReadOnlyList<ActionSettingDefinition> OwnSettings { get; } =
    [
        new(FileKey, "Sound file", ActionSettingKind.FilePath, Placeholder: "A .wav or .mp3 file"),
        new(VolumeKey, "Volume", ActionSettingKind.Number, "100") { Range = new(0, 100, 5) { Unit = "%" } },
        new(AgainKey, "Pressed while it plays", ActionSettingKind.Choice, Stop, Options: [new(Stop, "Stops it"), new(Restart, "Plays it again from the start")]),
        new(DeviceKey, "Output device (Linux, optional)", Placeholder: "Default", Description: "The PipeWire or PulseAudio sink name (wpctl status, pactl list short sinks). Windows and macOS play on the default device."),
    ];

    public override TimeSpan? RefreshInterval => null;

    internal static string SoundId(string deviceId, string controlId) => $"{deviceId}|{controlId}";

    protected override void Run(ActionContext context)
    {
        var id = SoundId(context.DeviceId, context.ControlId);
        var path = context.Settings.Get(FileKey).Trim().Trim('"');
        var volume = context.Settings.GetInt(VolumeKey, 100);
        var device = context.Settings.Get(DeviceKey).Trim();
        var stopOnly = SoundPlayer.IsPlaying(id) && context.Settings.Get(AgainKey, Stop) == Stop;
        RunInBackground(context, () =>
        {
            if (stopOnly)
            {
                SoundPlayer.Stop(id);
                return;
            }

            if (string.IsNullOrWhiteSpace(path))
            {
                throw new InvalidOperationException("Choose a sound file in Customize.");
            }

            _owners[id] = (context.Host, context.DeviceId, context.ControlId);
            SoundPlayer.Play(id, path, volume, device.Length > 0 ? device : null);
        });
    }

    protected override string Title(IRenderContext context)
    {
        var path = context.Settings.Get(FileKey);
        return string.IsNullOrWhiteSpace(path) ? Name : Path.GetFileNameWithoutExtension(path.Trim().Trim('"'));
    }

    protected override string? Subtitle(IRenderContext context) =>
        string.IsNullOrWhiteSpace(context.Settings.Get(FileKey)) ? "Not set" : null;

    protected override string? Icon(IRenderContext context) =>
        SoundPlayer.IsPlaying(SoundId(context.DeviceId, context.ControlId)) ? Icons.Stop : Icons.Music;

    protected override (uint From, uint To) TileColors => Palette.Sound;

    protected override string? Caption(IRenderContext context) => Title(context);
}

internal sealed class StopSoundsAction() : DeckActionBase(
    "system.sound-stop",
    "Stop All Sounds",
    InputCapabilities.Press,
    OutputCapabilities.None,
    "Stops every sound that Play Sound keys are playing.")
{
    protected override void Run(ActionContext context) => RunInBackground(context, SoundPlayer.StopAll);

    protected override string? Subtitle(IRenderContext context) => null;

    protected override string? Icon(IRenderContext context) => Icons.Stop;

    protected override (uint From, uint To) TileColors => Palette.Sound;
}

/// <summary>Mutes the default microphone: toggle, mute, unmute or push to talk; shows whether it is muted.</summary>
internal sealed class MicrophoneMuteAction() : DeckActionBase(
    "system.mic-mute",
    "Microphone Mute",
    InputCapabilities.Press | InputCapabilities.Release,
    OutputCapabilities.None,
    "Mutes or unmutes the default microphone, or unmutes it only while you hold the key (push to talk).")
{
    private const string ModeKey = "mode";
    private const string ToggleMode = "toggle";
    private const string MuteMode = "mute";
    private const string UnmuteMode = "unmute";
    private const string PushToTalkMode = "push-to-talk";

    protected override IReadOnlyList<ActionSettingDefinition> OwnSettings { get; } =
    [
        new(ModeKey, "Press", ActionSettingKind.Choice, ToggleMode, Options:
        [
            new(ToggleMode, "Mutes or unmutes"),
            new(MuteMode, "Mutes"),
            new(UnmuteMode, "Unmutes"),
            new(PushToTalkMode, "Unmutes while held (push to talk)"),
        ]),
    ];

    public override TimeSpan? RefreshInterval => TimeSpan.FromSeconds(2);

    protected override bool RunOnRelease => true;

    protected override void Run(ActionContext context)
    {
        var mode = context.Settings.Get(ModeKey, ToggleMode);
        var released = context.EventType == InputCapabilities.Release;
        bool? target = (mode, released) switch
        {
            (PushToTalkMode, false) => false,
            (PushToTalkMode, true) => true,
            (_, true) => null,
            (MuteMode, _) => true,
            (UnmuteMode, _) => false,
            _ => null,
        };

        if (released && mode != PushToTalkMode)
        {
            return;
        }

        RunInBackground(context, () => SystemVolume.SetMicrophoneMuted(target));
    }

    protected override string? Icon(IRenderContext context) =>
        SystemVolume.MicrophoneMuted() == true ? Icons.MicrophoneOff : Icons.Microphone;

    protected override string? Subtitle(IRenderContext context) => null;

    protected override string? Caption(IRenderContext context) => SystemVolume.MicrophoneMuted() switch
    {
        true => "Muted",
        false => "Live",
        _ => null,
    };

    protected override (uint From, uint To) TileColors => Palette.Sound;
}

using CommunityToolkit.Mvvm.Input;
using DeckHost.Contracts.Models;
using DeckHost.Core.Devices;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.RegularExpressions;

namespace DeckHost.App.ViewModels;

/// <summary>
/// Edits a <see cref="LightStyle"/>: the effect, its color or colors, speed, how far the LEDs run
/// apart, and for <see cref="LightEffects.Custom"/> a composer of steps (fade to a color, hold it),
/// with presets to start from. Used by Device Settings (Knob lights) and Customize (a knob's own
/// light). Raises <see cref="Changed"/> on every edit; <see cref="ToStyle"/> gives the result.
/// </summary>
public sealed partial class LightStyleEditorViewModel : ViewModelBase
{
    private static readonly Dictionary<string, string> Labels = new()
    {
        [LightEffects.Solid] = "Steady color",
        [LightEffects.Breathe] = "Breathing",
        [LightEffects.Pulse] = "Heartbeat",
        [LightEffects.Blink] = "Blink",
        [LightEffects.ColorCycle] = "Color cycle",
        [LightEffects.Rainbow] = "Rainbow",
        [LightEffects.Wave] = "Wave",
        [LightEffects.Chase] = "Chase",
        [LightEffects.Candle] = "Candle",
        [LightEffects.Sparkle] = "Sparkle",
        [LightEffects.Custom] = "Custom (compose your own)",
    };

    private readonly string _offValue;
    private string _effect;
    private string _color;
    private double _periodSeconds;
    private double? _spread;
    private bool _loading;

    /// <param name="style">The style to edit; null starts at <paramref name="offValue"/>.</param>
    /// <param name="offValue">The effect value of the Off option (Device Settings: "no light" means null).</param>
    public LightStyleEditorViewModel(LightStyle? style, string offValue = LightEffects.Off, string offLabel = "Off")
    {
        _offValue = offValue;
        Effects =
        [
            new ActionSettingOption(offValue, offLabel),
            .. LightEffects.All.Where(static effect => effect != LightEffects.Off).Select(static effect => new ActionSettingOption(effect, Labels[effect])),
        ];

        _loading = true;
        _effect = style is null || style.Effect == LightEffects.Off ? offValue : style.Effect;
        _color = "#" + (style?.Color ?? "0000FF").TrimStart('#');
        _periodSeconds = style?.PeriodSeconds ?? LightStyle.DefaultPeriodSeconds;
        _spread = style?.Spread;
        foreach (var hex in style?.Colors ?? [])
        {
            AddColorEntry(hex);
        }

        foreach (var step in style?.Steps ?? [])
        {
            AddStepEntry(step);
        }

        _loading = false;
        Colors.CollectionChanged += (_, _) => RaiseChanged();
        Steps.CollectionChanged += (_, _) =>
        {
            foreach (var step in Steps)
            {
                step.RefreshPosition();
            }

            OnPropertyChanged(nameof(CustomLengthText));
            RaiseChanged();
        };
    }

    public event EventHandler? Changed;

    public IReadOnlyList<ActionSettingOption> Effects { get; }

    public ActionSettingOption SelectedEffect
    {
        get => Effects.FirstOrDefault(option => option.Value == _effect) ?? Effects[0];
        set
        {
            if (value is null || value.Value == _effect)
            {
                return;
            }

            _effect = value.Value;
            if (_effect == LightEffects.Custom && Steps.Count == 0)
            {
                LoadPreset(Presets[0]);
            }

            if (LightEffects.UsesColors(_effect) && Colors.Count == 0)
            {
                AddColorEntry(_color);
                AddColorEntry("#FF00AA");
            }

            OnPropertyChanged();
            RaiseShape();
            RaiseChanged();
        }
    }

    public string Effect => _effect;

    public bool IsOff => _effect == _offValue;

    public bool ShowColor => LightEffects.UsesColor(_effect);

    public bool ShowColors => LightEffects.UsesColors(_effect);

    public bool ShowSteps => _effect == LightEffects.Custom;

    /// <summary>Speed for the built-in effects; a custom effect's speed comes from its steps.</summary>
    public bool ShowSpeed => LightEffects.IsAnimated(_effect) && _effect != LightEffects.Custom;

    public bool ShowSpread => LightEffects.UsesSpread(_effect);

    public string Color
    {
        get => _color;
        set
        {
            if (SetProperty(ref _color, value ?? string.Empty))
            {
                RaiseChanged();
            }
        }
    }

    /// <summary>Seconds per cycle.</summary>
    public double PeriodSeconds
    {
        get => _periodSeconds;
        set
        {
            var clamped = Math.Round(Math.Clamp(value, LightStyle.MinPeriodSeconds, 10), 1);
            if (SetProperty(ref _periodSeconds, clamped))
            {
                OnPropertyChanged(nameof(PeriodText));
                RaiseChanged();
            }
        }
    }

    public string PeriodText => $"{_periodSeconds:0.#} s";

    /// <summary>How far each LED runs ahead of the previous one, in percent of the cycle (0 to 50).</summary>
    public double SpreadPercent
    {
        get => Math.Round((_spread ?? LightEffectMath.DefaultSpread(_effect)) * 100);
        set
        {
            var spread = Math.Round(Math.Clamp(value, 0, 50)) / 100d;
            if (_spread != spread)
            {
                _spread = spread;
                OnPropertyChanged();
                OnPropertyChanged(nameof(SpreadText));
                RaiseChanged();
            }
        }
    }

    public string SpreadText => SpreadPercent == 0 ? "together" : $"{SpreadPercent:0} %";

    /// <summary>The colors of Color cycle.</summary>
    public ObservableCollection<LightColorEntryViewModel> Colors { get; } = [];

    /// <summary>The steps of a custom effect.</summary>
    public ObservableCollection<LightStepViewModel> Steps { get; } = [];

    public string CustomLengthText => $"One round takes {LightEffectMath.CustomLength(ToStyle(forPreview: true) ?? new LightStyle()):0.#} s.";

    /// <summary>Ready-made custom effects to start from.</summary>
    public static IReadOnlyList<LightPreset> Presets { get; } =
    [
        new("Police", [new("FF0000", 0, 0.25), new("000000", 0, 0.1), new("0000FF", 0, 0.25), new("000000", 0, 0.1)]),
        new("Sunset", [new("FF5E00", 2, 1), new("FF006E", 2, 1), new("7A00FF", 2, 1)]),
        new("Ocean", [new("0077FF", 2.5, 0.5), new("00E5FF", 2.5, 0.5), new("00FF9D", 2.5, 0.5)]),
        new("Traffic light", [new("FF0000", 0.2, 2), new("FFA500", 0.2, 0.8), new("00FF00", 0.2, 2)]),
        new("Fire", [new("FF2200", 0.15, 0.05), new("FF7700", 0.1, 0.05), new("FF3300", 0.2, 0.1), new("FFAA00", 0.1, 0.02)]),
        new("Alert", [new("FF0000", 0.05, 0.15), new("000000", 0.05, 0.15)]),
    ];

    public LightPreset? SelectedPreset
    {
        get => null;
        set
        {
            if (value is not null)
            {
                LoadPreset(value);
                OnPropertyChanged();
            }
        }
    }

    /// <summary>The edited light; null for Off when Off means "no light of its own" (Device Settings).</summary>
    public LightStyle? ToStyle() => ToStyle(forPreview: false);

    /// <summary>The style for the live preview: Off shows dark.</summary>
    public LightStyle PreviewStyle => ToStyle(forPreview: true) ?? new LightStyle(Effect: LightEffects.Off);

    private LightStyle? ToStyle(bool forPreview)
    {
        if (IsOff)
        {
            return _offValue == LightEffects.Off || forPreview ? new LightStyle(Hex(_color) ?? "0000FF", LightEffects.Off, _periodSeconds) : null;
        }

        return new LightStyle(Hex(_color) ?? "0000FF", _effect, _periodSeconds)
        {
            Colors = ShowColors ? [.. Colors.Select(static entry => Hex(entry.Color)).OfType<string>()] : null,
            Steps = ShowSteps ? [.. Steps.Select(static step => step.ToStep()).OfType<LightStep>()] : null,
            Spread = ShowSpread ? _spread : null,
        };
    }

    /// <summary>"#ff8800" → "FF8800"; null when it isn't a color.</summary>
    internal static string? Hex(string? text)
    {
        var hex = (text ?? string.Empty).Trim().TrimStart('#');
        return Regex.IsMatch(hex, "^[0-9A-Fa-f]{6}$") ? hex.ToUpperInvariant() : null;
    }

    [RelayCommand]
    private void AddColor() => AddColorEntry(Colors.Count == 0 ? _color : "#FFFFFF");

    [RelayCommand]
    private void AddStep() => AddStepEntry(new LightStep(Steps.Count == 0 ? Hex(_color) ?? "0000FF" : "FFFFFF"));

    internal void RemoveColor(LightColorEntryViewModel entry)
    {
        if (Colors.Count > 1)
        {
            Colors.Remove(entry);
        }
    }

    internal void RemoveStep(LightStepViewModel step)
    {
        if (Steps.Count > 1)
        {
            Steps.Remove(step);
        }
    }

    internal void MoveStep(LightStepViewModel step, int offset)
    {
        var index = Steps.IndexOf(step);
        var target = index + offset;
        if (index >= 0 && target >= 0 && target < Steps.Count)
        {
            Steps.Move(index, target);
        }
    }

    internal void StepChanged()
    {
        OnPropertyChanged(nameof(CustomLengthText));
        RaiseChanged();
    }

    private void LoadPreset(LightPreset preset)
    {
        _loading = true;
        Steps.Clear();
        foreach (var step in preset.Steps)
        {
            AddStepEntry(step);
        }

        _loading = false;
        OnPropertyChanged(nameof(CustomLengthText));
        RaiseChanged();
    }

    private void AddColorEntry(string hex) => Colors.Add(new LightColorEntryViewModel(this, "#" + hex.TrimStart('#')));

    private void AddStepEntry(LightStep step) => Steps.Add(new LightStepViewModel(this, step));

    private void RaiseShape()
    {
        OnPropertyChanged(nameof(IsOff));
        OnPropertyChanged(nameof(ShowColor));
        OnPropertyChanged(nameof(ShowColors));
        OnPropertyChanged(nameof(ShowSteps));
        OnPropertyChanged(nameof(ShowSpeed));
        OnPropertyChanged(nameof(ShowSpread));
        OnPropertyChanged(nameof(SpreadPercent));
        OnPropertyChanged(nameof(SpreadText));
        OnPropertyChanged(nameof(CustomLengthText));
    }

    private void RaiseChanged()
    {
        if (!_loading)
        {
            OnPropertyChanged(nameof(PreviewStyle));
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    internal void ColorEntryChanged() => RaiseChanged();
}

/// <summary>A ready-made custom light effect.</summary>
public sealed record LightPreset(string Name, IReadOnlyList<LightStep> Steps)
{
    public override string ToString() => Name;
}

/// <summary>One color of a Color cycle.</summary>
public sealed partial class LightColorEntryViewModel(LightStyleEditorViewModel owner, string color) : ViewModelBase
{
    private string _color = color;

    public string Color
    {
        get => _color;
        set
        {
            if (SetProperty(ref _color, value ?? string.Empty))
            {
                owner.ColorEntryChanged();
            }
        }
    }

    [RelayCommand]
    private void Remove() => owner.RemoveColor(this);
}

/// <summary>One step of a custom light effect: fade to a color, then hold it.</summary>
public sealed partial class LightStepViewModel : ViewModelBase
{
    private readonly LightStyleEditorViewModel _owner;
    private string _color;
    private double _fadeSeconds;
    private double _holdSeconds;

    public LightStepViewModel(LightStyleEditorViewModel owner, LightStep step)
    {
        _owner = owner;
        _color = "#" + step.Color.TrimStart('#');
        _fadeSeconds = step.FadeSeconds;
        _holdSeconds = step.HoldSeconds;
    }

    public int Position => _owner.Steps.IndexOf(this) + 1;

    public string Title => $"Step {Position}";

    public string Color
    {
        get => _color;
        set
        {
            if (SetProperty(ref _color, value ?? string.Empty))
            {
                _owner.StepChanged();
            }
        }
    }

    public double FadeSeconds
    {
        get => _fadeSeconds;
        set
        {
            if (SetProperty(ref _fadeSeconds, Math.Round(Math.Clamp(value, 0, 10), 2)))
            {
                OnPropertyChanged(nameof(FadeText));
                _owner.StepChanged();
            }
        }
    }

    public string FadeText => _fadeSeconds == 0 ? "instant" : $"{_fadeSeconds:0.##} s";

    public double HoldSeconds
    {
        get => _holdSeconds;
        set
        {
            if (SetProperty(ref _holdSeconds, Math.Round(Math.Clamp(value, 0, 10), 2)))
            {
                OnPropertyChanged(nameof(HoldText));
                _owner.StepChanged();
            }
        }
    }

    public string HoldText => $"{_holdSeconds:0.##} s";

    public bool CanMoveUp => Position > 1;

    public bool CanMoveDown => Position < _owner.Steps.Count;

    public LightStep? ToStep() => LightStyleEditorViewModel.Hex(_color) is { } hex ? new LightStep(hex, _fadeSeconds, _holdSeconds) : null;

    public void RefreshPosition()
    {
        OnPropertyChanged(nameof(Position));
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(CanMoveUp));
        OnPropertyChanged(nameof(CanMoveDown));
    }

    [RelayCommand]
    private void Remove() => _owner.RemoveStep(this);

    [RelayCommand]
    private void MoveUp() => _owner.MoveStep(this, -1);

    [RelayCommand]
    private void MoveDown() => _owner.MoveStep(this, 1);
}

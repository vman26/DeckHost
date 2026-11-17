using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using CommunityToolkit.Mvvm.Input;
using DeckHost.Contracts.Models;

namespace DeckHost.App.ViewModels;

/// <summary>What a bound layer property follows, as the editor words it.</summary>
public sealed record BindingTargetOption(string Value, string Label);

/// <summary>One background layer as a card in the Background Studio.</summary>
public sealed class BackgroundLayerRowViewModel : ViewModelBase
{
    public static IReadOnlyList<BindingTargetOption> TargetOptions { get; } =
    [
        new(BackgroundBindingTargets.Color, "its color (from one color to another)"),
        new(BackgroundBindingTargets.Opacity, "its opacity (more visible as the value grows)"),
        new(BackgroundBindingTargets.Visible, "whether it shows (above a value)"),
    ];

    private readonly BackgroundLayersViewModel _owner;
    private bool _visible;
    private double _opacityPercent;
    private string _color;
    private string _toColor;
    private double _angle;
    private string _imagePath;
    private bool _isBound;
    private string _variable;
    private BindingTargetOption _target;
    private string _min;
    private string _max;
    private string _fromColor;
    private string _boundToColor;

    internal BackgroundLayerRowViewModel(BackgroundLayersViewModel owner, BackgroundLayer layer)
    {
        _owner = owner;
        Original = layer;
        _visible = layer.Visible;
        _opacityPercent = Math.Round(Math.Clamp(layer.Opacity, 0, 1) * 100);
        _color = layer.Color ?? string.Empty;
        _toColor = layer.ToColor ?? string.Empty;
        _angle = layer.Angle ?? 0;
        _imagePath = layer.ImagePath ?? string.Empty;
        _isBound = layer.Binding is not null;
        _variable = layer.Binding?.Variable ?? string.Empty;
        _target = TargetOptions.FirstOrDefault(option => option.Value == layer.Binding?.Target) ?? TargetOptions[0];
        _min = (layer.Binding?.Min ?? 0).ToString("0.###", CultureInfo.InvariantCulture);
        _max = (layer.Binding?.Max ?? 100).ToString("0.###", CultureInfo.InvariantCulture);
        _fromColor = layer.Binding?.FromColor ?? string.Empty;
        _boundToColor = layer.Binding?.ToColor ?? string.Empty;
    }

    /// <summary>The layer as it was loaded: a layer of a kind this version doesn't know is written back from it unchanged.</summary>
    public BackgroundLayer Original { get; }

    public string Kind => Original.Kind;

    public bool IsKnown => BackgroundLayerKinds.Known.Contains(Kind);

    public bool IsSolid => Kind == BackgroundLayerKinds.Solid;

    public bool IsGradient => Kind == BackgroundLayerKinds.Gradient;

    public bool IsImage => Kind == BackgroundLayerKinds.Image;

    /// <summary>Whether it has a color (or two) to set.</summary>
    public bool HasColor => IsSolid || IsGradient;

    public string KindLabel => Kind switch
    {
        BackgroundLayerKinds.Solid => "Solid color",
        BackgroundLayerKinds.Gradient => "Gradient",
        BackgroundLayerKinds.Image => "Image",
        _ => $"Layer of a kind this version doesn't know ({Kind}); kept as it is",
    };

    public string ColorLabel => IsGradient ? "From" : "Color";

    public string BoundColorsHint => IsGradient ? "The gradient's end color follows the variable." : "The color follows the variable.";

    public bool Visible
    {
        get => _visible;
        set => Set(ref _visible, value);
    }

    /// <summary>0 to 100.</summary>
    public double OpacityPercent
    {
        get => _opacityPercent;
        set => Set(ref _opacityPercent, Math.Clamp(value, 0, 100));
    }

    public string Color
    {
        get => _color;
        set => Set(ref _color, value ?? string.Empty);
    }

    public string ToColor
    {
        get => _toColor;
        set => Set(ref _toColor, value ?? string.Empty);
    }

    /// <summary>The gradient's direction in degrees: 0 left to right, 90 top to bottom.</summary>
    public double Angle
    {
        get => _angle;
        set => Set(ref _angle, Math.Clamp(value, 0, 360));
    }

    public string ImagePath
    {
        get => _imagePath;
        set => Set(ref _imagePath, value ?? string.Empty);
    }

    public bool IsBound
    {
        get => _isBound;
        set => Set(ref _isBound, value);
    }

    /// <summary>The variable to follow: <c>system.cpu</c>, <c>system.memory</c> or <c>vars.name</c> (a Set variable node's variable).</summary>
    public string Variable
    {
        get => _variable;
        set => Set(ref _variable, value ?? string.Empty);
    }

    public BindingTargetOption Target
    {
        get => _target;
        set => Set(ref _target, value ?? TargetOptions[0]);
    }

    public bool TargetIsColor => Target.Value == BackgroundBindingTargets.Color;

    public string Min
    {
        get => _min;
        set => Set(ref _min, value ?? string.Empty);
    }

    public string Max
    {
        get => _max;
        set => Set(ref _max, value ?? string.Empty);
    }

    public string FromColor
    {
        get => _fromColor;
        set => Set(ref _fromColor, value ?? string.Empty);
    }

    public string BoundToColor
    {
        get => _boundToColor;
        set => Set(ref _boundToColor, value ?? string.Empty);
    }

    private void Set<T>(ref T field, T value, [System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        if (!SetProperty(ref field, value, name))
        {
            return;
        }

        if (name == nameof(Target))
        {
            OnPropertyChanged(nameof(TargetIsColor));
        }

        _owner.Changed();
    }

    /// <summary>The layer these settings make.</summary>
    internal BackgroundLayer ToLayer()
    {
        if (!IsKnown)
        {
            return Original with { Visible = Visible };
        }

        static string? Blank(string text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
        static double Number(string text, double fallback) =>
            double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && double.IsFinite(value) ? value : fallback;

        return new BackgroundLayer(Kind, Visible, Math.Round(OpacityPercent / 100d, 3))
        {
            Color = HasColor ? Blank(Color) : null,
            ToColor = IsGradient ? Blank(ToColor) : null,
            Angle = IsGradient ? Angle : null,
            ImagePath = IsImage ? Blank(ImagePath) : null,
            Binding = IsBound && Blank(Variable) is { } variable
                ? new BackgroundBinding(variable, Target.Value, Number(Min, 0), Number(Max, 100))
                {
                    FromColor = TargetIsColor ? Blank(FromColor) : null,
                    ToColor = TargetIsColor ? Blank(BoundToColor) : null,
                }
                : null,
        };
    }
}

/// <summary>
/// The Background Studio's layer list for one background: add, reorder, remove and edit layers. Every edit reports the new
/// list (<c>onChanged</c>), which the owner stores on the profile or the default background.
/// </summary>
public sealed partial class BackgroundLayersViewModel : ViewModelBase
{
    private readonly Action<IReadOnlyList<BackgroundLayer>> _onChanged;
    private bool _loading;

    public BackgroundLayersViewModel(IReadOnlyList<BackgroundLayer>? layers, Action<IReadOnlyList<BackgroundLayer>> onChanged)
    {
        _onChanged = onChanged;
        Load(layers);
    }

    public ObservableCollection<BackgroundLayerRowViewModel> Rows { get; } = [];

    public bool HasRows => Rows.Count > 0;

    /// <summary>Shows another list (another profile) without reporting a change.</summary>
    public void Load(IReadOnlyList<BackgroundLayer>? layers)
    {
        _loading = true;
        try
        {
            Rows.Clear();
            foreach (var layer in layers ?? [])
            {
                Rows.Add(new BackgroundLayerRowViewModel(this, layer));
            }
        }
        finally
        {
            _loading = false;
        }

        OnPropertyChanged(nameof(HasRows));
    }

    /// <summary>The layers as the rows make them, bottom to top.</summary>
    public IReadOnlyList<BackgroundLayer> Layers => [.. Rows.Select(static row => row.ToLayer())];

    internal void Changed()
    {
        if (!_loading)
        {
            _onChanged(Layers);
        }
    }

    private void Add(BackgroundLayer layer)
    {
        Rows.Add(new BackgroundLayerRowViewModel(this, layer));
        OnPropertyChanged(nameof(HasRows));
        Changed();
    }

    [RelayCommand]
    private void AddSolid() => Add(new BackgroundLayer(BackgroundLayerKinds.Solid) { Color = "#1E293B" });

    [RelayCommand]
    private void AddGradient() => Add(new BackgroundLayer(BackgroundLayerKinds.Gradient) { Color = "#1E3A8A", ToColor = "#7C3AED", Angle = 45 });

    [RelayCommand]
    private void AddImage() => Add(new BackgroundLayer(BackgroundLayerKinds.Image));

    /// <summary>A red glow that gets stronger as the CPU gets busier (a layer that follows <c>system.cpu</c>).</summary>
    [RelayCommand]
    private void AddCpuGlow() => Add(new BackgroundLayer(BackgroundLayerKinds.Solid, Opacity: 0.8)
    {
        Color = "#EF4444",
        Binding = new BackgroundBinding("system.cpu", BackgroundBindingTargets.Opacity, 0, 100),
    });

    public void Remove(BackgroundLayerRowViewModel row)
    {
        if (Rows.Remove(row))
        {
            OnPropertyChanged(nameof(HasRows));
            Changed();
        }
    }

    /// <summary>Moves a layer up (towards the top of the picture: later in the list) or down.</summary>
    public void Move(BackgroundLayerRowViewModel row, int direction)
    {
        var index = Rows.IndexOf(row);
        var target = index + direction;
        if (index < 0 || target < 0 || target >= Rows.Count)
        {
            return;
        }

        Rows.Move(index, target);
        Changed();
    }
}

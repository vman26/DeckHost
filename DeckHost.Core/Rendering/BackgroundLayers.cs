using System.Globalization;
using DeckHost.Contracts;
using DeckHost.Contracts.Interfaces;
using DeckHost.Contracts.Models;
using SkiaSharp;

namespace DeckHost.Core.Rendering;

/// <summary>A background layer with everything that follows a variable worked out, ready to draw.</summary>
internal readonly record struct ResolvedLayer(string Kind, double Opacity, SKColor Color, SKColor ToColor, double Angle, string? ImagePath)
{
    /// <summary>A gradient's colors with their places (two at least); null for the other kinds.</summary>
    public GradientColors? Stops { get; init; }

    public double CenterX { get; init; }

    public double CenterY { get; init; }

    public double Radius { get; init; }
}

/// <summary>The colors of a gradient and where each starts (0 to 1, in order). Compared by value, so two resolved backgrounds can be told apart.</summary>
internal sealed class GradientColors(SKColor[] colors, float[] positions) : IEquatable<GradientColors>
{
    public SKColor[] Colors { get; } = colors;

    public float[] Positions { get; } = positions;

    public bool Equals(GradientColors? other) => other is not null && Colors.SequenceEqual(other.Colors) && Positions.SequenceEqual(other.Positions);

    public override bool Equals(object? obj) => Equals(obj as GradientColors);

    public override int GetHashCode() => HashCode.Combine(Colors.Length, Positions.Length, Colors.Length > 0 ? Colors[0] : default);
}

/// <summary>
/// A profile background as layers: the old <see cref="ProfileBackground.Color"/> and <see cref="ProfileBackground.ImagePath"/>
/// first, then <see cref="ProfileBackground.Layers"/>. Bindings are resolved here from the variable store, so the renderer
/// never talks to a data source of its own.
/// </summary>
public static class BackgroundLayers
{
    /// <summary>The background's layers bottom to top, the two older fields as a solid and an image layer.</summary>
    public static IReadOnlyList<BackgroundLayer> Effective(ProfileBackground? background)
    {
        if (background is null)
        {
            return [];
        }

        var layers = new List<BackgroundLayer>();
        if (!string.IsNullOrWhiteSpace(background.Color))
        {
            layers.Add(new BackgroundLayer(BackgroundLayerKinds.Solid) { Color = background.Color });
        }

        if (!string.IsNullOrWhiteSpace(background.ImagePath))
        {
            layers.Add(new BackgroundLayer(BackgroundLayerKinds.Image) { ImagePath = background.ImagePath });
        }

        layers.AddRange(background.Layers ?? []);
        return layers;
    }

    /// <summary>
    /// The variables the background's visible layers follow (what the host redraws for when one changes). A layer
    /// that can never show (hidden, an unknown kind, an opacity of 0) follows nothing.
    /// </summary>
    public static IReadOnlySet<string> BoundVariables(ProfileBackground? background) =>
        Effective(background)
            .Where(static layer => layer.Binding is { Variable.Length: > 0 } && CanShow(layer))
            .Select(static layer => layer.Binding!.Variable.Trim())
            .ToHashSet(StringComparer.Ordinal);

    private static bool CanShow(BackgroundLayer layer) =>
        layer.Visible && BackgroundLayerKinds.Known.Contains(layer.Kind) && EffectiveOpacity(layer) > 0;

    private static double EffectiveOpacity(BackgroundLayer layer) => Math.Clamp(double.IsFinite(layer.Opacity) ? layer.Opacity : 1, 0, 1);

    /// <summary>Whether two resolved backgrounds draw the same picture (so a device showing one needs no redraw for the other).</summary>
    internal static bool Same(IReadOnlyList<ResolvedLayer> first, IReadOnlyList<ResolvedLayer> second) => first.SequenceEqual(second);

    /// <summary>Whether the background has a layer that follows a variable.</summary>
    public static bool IsReactive(ProfileBackground? background) => BoundVariables(background).Count > 0;

    internal static IReadOnlyList<ResolvedLayer> Resolve(ProfileBackground? background, IVariableStore? variables)
    {
        var resolved = new List<ResolvedLayer>();
        foreach (var layer in Effective(background))
        {
            if (!layer.Visible || !BackgroundLayerKinds.Known.Contains(layer.Kind))
            {
                continue;
            }

            var opacity = EffectiveOpacity(layer);
            var color = Parse(layer.Color, new SKColor(0, 0, 0, 0));
            var toColor = Parse(layer.ToColor, color);
            var isGradient = layer.Kind is BackgroundLayerKinds.Gradient or BackgroundLayerKinds.Radial;
            var stops = isGradient ? StopsOf(layer, color, toColor) : [];
            if (isGradient)
            {
                (color, toColor) = (stops[0].Color, stops[^1].Color);
            }

            if (layer.Binding is { Variable.Length: > 0 } binding)
            {
                var value = ReadNumber(variables, binding);
                var t = binding.Max > binding.Min ? Math.Clamp((value - binding.Min) / (binding.Max - binding.Min), 0, 1) : value >= binding.Max ? 1 : 0;
                switch (binding.Target)
                {
                    case BackgroundBindingTargets.Opacity:
                        opacity *= t;
                        break;
                    case BackgroundBindingTargets.Visible:
                        if (value < binding.Min)
                        {
                            continue;
                        }

                        break;
                    default:
                    {
                        var from = Parse(binding.FromColor, color);
                        var to = Parse(binding.ToColor, isGradient ? toColor : color);
                        var mixed = Lerp(from, to, t);
                        if (isGradient)
                        {
                            // The color that follows the variable is the gradient's last one.
                            toColor = mixed;
                            stops[^1] = (mixed, stops[^1].Position);
                        }
                        else
                        {
                            color = mixed;
                        }

                        break;
                    }
                }
            }

            if (opacity <= 0)
            {
                continue;
            }

            resolved.Add(new ResolvedLayer(layer.Kind, opacity, color, toColor, layer.Angle ?? 0, layer.ImagePath)
            {
                Stops = isGradient ? new GradientColors([.. stops.Select(static stop => stop.Color)], [.. stops.Select(static stop => stop.Position)]) : null,
                CenterX = Math.Clamp(layer.CenterX ?? 0.5, 0, 1),
                CenterY = Math.Clamp(layer.CenterY ?? 0.5, 0, 1),
                Radius = Math.Clamp(layer.Radius ?? 1, 0.01, 4),
            });
        }

        return resolved;
    }

    /// <summary>The colors of a gradient with their places, in order: its <see cref="BackgroundLayer.Stops"/> when it has two or more, else Color to ToColor.</summary>
    private static List<(SKColor Color, float Position)> StopsOf(BackgroundLayer layer, SKColor color, SKColor toColor)
    {
        if (layer.Stops is { Count: >= 2 } given)
        {
            return
            [
                .. given
                    .Select((stop, index) => (Index: index, Color: Parse(stop.Color, color), Position: (float)Math.Clamp(double.IsFinite(stop.Position) ? stop.Position : 0, 0, 1)))
                    .OrderBy(static stop => stop.Position)
                    .ThenBy(static stop => stop.Index)
                    .Select(static stop => (stop.Color, stop.Position)),
            ];
        }

        return [(color, 0f), (toColor, 1f)];
    }

    private static SKColor Parse(string? text, SKColor fallback) =>
        !string.IsNullOrWhiteSpace(text) && SKColor.TryParse(text.Trim(), out var parsed) ? parsed : fallback;

    private static SKColor Lerp(SKColor from, SKColor to, double t) => new(
        Channel(from.Red, to.Red, t), Channel(from.Green, to.Green, t), Channel(from.Blue, to.Blue, t), Channel(from.Alpha, to.Alpha, t));

    private static byte Channel(byte from, byte to, double t) => (byte)Math.Round(from + ((to - from) * t));

    /// <summary>The value a binding reads: a number, true (1) or false (0), a number written as text, else the fallback (or 0).</summary>
    internal static double ReadNumber(IVariableStore? variables, BackgroundBinding binding)
    {
        var fallback = binding.Fallback ?? 0;
        if (variables is null)
        {
            return fallback;
        }

        var key = binding.Variable.Trim();
        if (variables.TryGetValue<double>(key, out var d) && double.IsFinite(d))
        {
            return d;
        }

        if (variables.TryGetValue<int>(key, out var i))
        {
            return i;
        }

        if (variables.TryGetValue<long>(key, out var l))
        {
            return l;
        }

        if (variables.TryGetValue<float>(key, out var f) && float.IsFinite(f))
        {
            return f;
        }

        if (variables.TryGetValue<bool>(key, out var flag))
        {
            return flag ? 1 : 0;
        }

        if (variables.TryGetValue<string>(key, out var text))
        {
            if (string.Equals(text?.Trim(), "true", StringComparison.OrdinalIgnoreCase))
            {
                return 1;
            }

            if (string.Equals(text?.Trim(), "false", StringComparison.OrdinalIgnoreCase))
            {
                return 0;
            }

            if (double.TryParse(text?.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) && double.IsFinite(parsed))
            {
                return parsed;
            }
        }

        return fallback;
    }
}

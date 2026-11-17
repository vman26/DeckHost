using System.Globalization;
using DeckHost.Contracts;
using DeckHost.Contracts.Interfaces;
using DeckHost.Contracts.Models;
using SkiaSharp;

namespace DeckHost.Core.Rendering;

/// <summary>A background layer with everything that follows a variable worked out, ready to draw.</summary>
internal readonly record struct ResolvedLayer(string Kind, double Opacity, SKColor Color, SKColor ToColor, double Angle, string? ImagePath);

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

    /// <summary>The variables the background's layers follow (what the host redraws for when one changes).</summary>
    public static IReadOnlySet<string> BoundVariables(ProfileBackground? background) =>
        Effective(background).Where(static layer => layer.Binding is { Variable.Length: > 0 }).Select(static layer => layer.Binding!.Variable.Trim()).ToHashSet(StringComparer.Ordinal);

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

            var opacity = Math.Clamp(double.IsFinite(layer.Opacity) ? layer.Opacity : 1, 0, 1);
            var color = Parse(layer.Color, new SKColor(0, 0, 0, 0));
            var toColor = Parse(layer.ToColor, color);
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
                        var to = Parse(binding.ToColor, layer.Kind == BackgroundLayerKinds.Gradient ? toColor : color);
                        var mixed = Lerp(from, to, t);
                        if (layer.Kind == BackgroundLayerKinds.Gradient)
                        {
                            toColor = mixed;
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

            resolved.Add(new ResolvedLayer(layer.Kind, opacity, color, toColor, layer.Angle ?? 0, layer.ImagePath));
        }

        return resolved;
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

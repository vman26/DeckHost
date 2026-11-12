using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Media;

namespace DeckHost.App.Views;

/// <summary>
/// A color as #RRGGBB text with a color picker next to it; empty means "not set". While it is not
/// set, the box shows <see cref="DefaultHex"/> in a muted style, so it is clear what will be used.
/// With <see cref="AllowOpacity"/> an opacity slider follows: below 100 % the value is #AARRGGBB,
/// and 0 % is transparent.
/// </summary>
public partial class ColorField : UserControl
{
    public static readonly StyledProperty<string?> HexProperty =
        AvaloniaProperty.Register<ColorField, string?>(nameof(Hex), defaultBindingMode: BindingMode.TwoWay);

    /// <summary>Describes the default, shown as the tooltip (e.g. "Profile background").</summary>
    public static readonly StyledProperty<string?> PlaceholderProperty =
        AvaloniaProperty.Register<ColorField, string?>(nameof(Placeholder));

    /// <summary>The color used when nothing is set.</summary>
    public static readonly StyledProperty<string?> DefaultHexProperty =
        AvaloniaProperty.Register<ColorField, string?>(nameof(DefaultHex), "#FFFFFF");

    /// <summary>Shows the opacity slider and allows #AARRGGBB values.</summary>
    public static readonly StyledProperty<bool> AllowOpacityProperty =
        AvaloniaProperty.Register<ColorField, bool>(nameof(AllowOpacity));

    private bool _syncing;

    /// <summary>The text <see cref="Sync"/> last put in the box (the value, or the default while unset).</summary>
    private string? _syncedText;

    public ColorField()
    {
        InitializeComponent();

        // TextChanged and ColorChanged can arrive after Sync has returned, so _syncing alone doesn't
        // tell them apart from the user. Without the comparison with what Sync showed, opening a
        // window wrote the shown default (#000000, #FFFFFF) into the setting as if it was chosen.
        HexBox.TextChanged += (_, _) =>
        {
            if (!_syncing && HexBox.Text != _syncedText)
            {
                Hex = HexBox.Text;
            }
        };
        Picker.ColorChanged += (_, e) =>
        {
            var hex = $"#{e.NewColor.R:X2}{e.NewColor.G:X2}{e.NewColor.B:X2}";
            if (!_syncing && !(TryParse(_syncedText, out var shown) && $"#{shown.R:X2}{shown.G:X2}{shown.B:X2}" == hex))
            {
                // A new color keeps the opacity that was set.
                Hex = Format(new Color(CurrentAlpha(), e.NewColor.R, e.NewColor.G, e.NewColor.B));
            }
        };
        ClearButton.Click += (_, _) => Hex = string.Empty;
        OpacitySlider.ValueChanged += (_, e) =>
        {
            if (!_syncing)
            {
                SetOpacity((byte)Math.Round(Math.Clamp(e.NewValue, 0, 100) * 255 / 100));
            }
        };
        TransparentButton.Click += (_, _) => SetOpacity(0);
        Sync(Hex);
    }

    public string? Hex
    {
        get => GetValue(HexProperty);
        set => SetValue(HexProperty, value);
    }

    public bool AllowOpacity
    {
        get => GetValue(AllowOpacityProperty);
        set => SetValue(AllowOpacityProperty, value);
    }

    public string? Placeholder
    {
        get => GetValue(PlaceholderProperty);
        set => SetValue(PlaceholderProperty, value);
    }

    public string? DefaultHex
    {
        get => GetValue(DefaultHexProperty);
        set => SetValue(DefaultHexProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == HexProperty || change.Property == DefaultHexProperty || change.Property == PlaceholderProperty || change.Property == AllowOpacityProperty)
        {
            Sync(Hex);
        }
    }

    private void Sync(string? hex)
    {
        if (HexBox is null)
        {
            return;
        }

        _syncing = true;
        try
        {
            var isDefault = string.IsNullOrWhiteSpace(hex);
            var shown = isDefault ? DefaultHex ?? string.Empty : hex!;
            _syncedText = shown;
            if (HexBox.Text != shown)
            {
                HexBox.Text = shown;
            }

            HexBox.Classes.Set("isDefault", isDefault);
            ClearButton.IsEnabled = !isDefault;
            ToolTip.SetTip(HexBox, isDefault
                ? $"Default{(string.IsNullOrWhiteSpace(Placeholder) ? string.Empty : $" ({Placeholder})")}: {DefaultHex}. Type or pick a color to change it."
                : null);

            if (TryParse(shown, out var color))
            {
                Picker.Color = Color.FromRgb(color.R, color.G, color.B);
            }

            OpacityRow.IsVisible = AllowOpacity;
            var alpha = isDefault || !TryParse(hex, out var withAlpha) ? (byte)255 : withAlpha.A;
            OpacitySlider.Value = Math.Round(alpha * 100d / 255);
            OpacityText.Text = alpha == 0 ? "None" : $"{Math.Round(alpha * 100d / 255)} %";
        }
        finally
        {
            _syncing = false;
        }
    }

    /// <summary>The opacity of the value as set (opaque while unset).</summary>
    private byte CurrentAlpha() =>
        !string.IsNullOrWhiteSpace(Hex) && TryParse(Hex, out var color) ? color.A : (byte)255;

    private void SetOpacity(byte alpha)
    {
        // Unset: the default color gets the opacity.
        var source = !string.IsNullOrWhiteSpace(Hex) && TryParse(Hex, out var current) ? current
            : TryParse(DefaultHex, out var fallback) ? fallback
            : Colors.Black;
        Hex = Format(new Color(alpha, source.R, source.G, source.B));
    }

    /// <summary>#RRGGBB when opaque, #AARRGGBB otherwise.</summary>
    private static string Format(Color color) => color.A == 255
        ? $"#{color.R:X2}{color.G:X2}{color.B:X2}"
        : $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";

    private static bool TryParse(string? hex, out Color color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(hex))
        {
            return false;
        }

        var trimmed = hex.Trim();
        return Color.TryParse(trimmed.StartsWith('#') ? trimmed : "#" + trimmed, out color);
    }
}

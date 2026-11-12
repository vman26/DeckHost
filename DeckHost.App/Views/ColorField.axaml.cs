using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Media;

namespace DeckHost.App.Views;

/// <summary>
/// A color as #RRGGBB text with a color picker next to it; empty means "not set". While it is not
/// set, the box shows <see cref="DefaultHex"/> in a muted style, so it is clear what will be used.
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

    private bool _syncing;

    public ColorField()
    {
        InitializeComponent();
        HexBox.TextChanged += (_, _) =>
        {
            if (!_syncing)
            {
                Hex = HexBox.Text;
            }
        };
        Picker.ColorChanged += (_, e) =>
        {
            if (!_syncing)
            {
                Hex = $"#{e.NewColor.R:X2}{e.NewColor.G:X2}{e.NewColor.B:X2}";
            }
        };
        ClearButton.Click += (_, _) => Hex = string.Empty;
        Sync(Hex);
    }

    public string? Hex
    {
        get => GetValue(HexProperty);
        set => SetValue(HexProperty, value);
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
        if (change.Property == HexProperty || change.Property == DefaultHexProperty || change.Property == PlaceholderProperty)
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
                Picker.Color = color;
            }
        }
        finally
        {
            _syncing = false;
        }
    }

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

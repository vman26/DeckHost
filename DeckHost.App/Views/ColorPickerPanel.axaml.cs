using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using DeckHost.App.Platform;

namespace DeckHost.App.Views;

/// <summary>
/// The color picker in a <see cref="ColorField"/>'s drop-down: a saturation/brightness square
/// (white at the top left, the hue at the top right, black at the bottom), a hue slider, an
/// opacity slider (with <see cref="ShowAlpha"/>), an eyedropper that picks from anywhere on the
/// screen, HEX and R/G/B boxes, and preset colors. The color is kept as HSV, so the hue survives
/// dragging to white or black.
/// </summary>
public partial class ColorPickerPanel : UserControl
{
    public static readonly StyledProperty<bool> ShowAlphaProperty =
        AvaloniaProperty.Register<ColorPickerPanel, bool>(nameof(ShowAlpha));

    /// <summary>Preset colors: neutrals, then a bright row and a soft row.</summary>
    public static readonly string[] PresetColors =
    [
        "#000000", "#FFFFFF", "#0B1220", "#334155", "#64748B", "#94A3B8", "#E2E8F0", "#F8FAFC",
        "#EF4444", "#F97316", "#F59E0B", "#EAB308", "#22C55E", "#14B8A6", "#06B6D4", "#3B82F6",
        "#6366F1", "#8B5CF6", "#A855F7", "#EC4899", "#F43F5E", "#FCA5A5", "#FDBA74", "#86EFAC",
        "#67E8F9", "#93C5FD", "#C4B5FD", "#F9A8D4",
    ];

    private static readonly TimeSpan EyedropperInterval = TimeSpan.FromMilliseconds(30);

    private HsvColor _hsv = HsvColor.FromAhsv(1, 0, 0, 0);
    private bool _updating;
    private DispatcherTimer? _eyedropperTimer;
    private ScreenColorPicker.IScreenSampler? _sampler;
    private HsvColor _beforeEyedropper;
    private bool _waitForRelease;

    public ColorPickerPanel()
    {
        InitializeComponent();

        foreach (var preset in PresetColors)
        {
            var color = Color.Parse(preset);
            var swatch = new Button { Background = new SolidColorBrush(color) };
            swatch.Classes.Add("swatch");
            ToolTip.SetTip(swatch, preset);
            swatch.Click += (_, _) => SetFromUser(HsvColor.FromAhsv(_hsv.A, color.ToHsv().H, color.ToHsv().S, color.ToHsv().V));
            Presets.Items.Add(swatch);
        }

        Spectrum.PropertyChanged += (_, e) =>
        {
            if (e.Property == ColorSpectrum.HsvColorProperty && !_updating)
            {
                // The square only moves saturation and brightness.
                var picked = Spectrum.HsvColor;
                SetFromUser(HsvColor.FromAhsv(_hsv.A, _hsv.H, picked.S, picked.V), keepHueWhenGray: false);
            }
        };
        HueSlider.PropertyChanged += (_, e) =>
        {
            if (e.Property == ColorSlider.HsvColorProperty && !_updating)
            {
                SetFromUser(HsvColor.FromAhsv(_hsv.A, HueSlider.HsvColor.H, _hsv.S, _hsv.V), keepHueWhenGray: false);
            }
        };
        AlphaSlider.PropertyChanged += (_, e) =>
        {
            if (e.Property == ColorSlider.HsvColorProperty && !_updating)
            {
                SetFromUser(HsvColor.FromAhsv(AlphaSlider.HsvColor.A, _hsv.H, _hsv.S, _hsv.V), keepHueWhenGray: false);
            }
        };

        HexInput.LostFocus += (_, _) => ApplyHexInput();
        HexInput.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                ApplyHexInput();
            }
        };
        foreach (var input in new[] { RedInput, GreenInput, BlueInput })
        {
            input.LostFocus += (_, _) => ApplyChannelInputs();
            input.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter)
                {
                    ApplyChannelInputs();
                }
            };
        }

        EyedropperButton.IsEnabled = ScreenColorPicker.IsSupported;
        if (ScreenColorPicker.UnsupportedReason is { } reason)
        {
            ToolTip.SetTip(EyedropperButton, reason);
        }

        EyedropperButton.Click += (_, _) => StartEyedropper();
        DetachedFromVisualTree += (_, _) => StopEyedropper(keep: true);
        Show(_hsv);
    }

    /// <summary>The user changed the color (not raised for <see cref="SetColor"/>).</summary>
    public event EventHandler<Color>? ColorPicked;

    public bool ShowAlpha
    {
        get => GetValue(ShowAlphaProperty);
        set => SetValue(ShowAlphaProperty, value);
    }

    public Color Color => _hsv.ToRgb();

    /// <summary>Shows a color without raising <see cref="ColorPicked"/>. A gray keeps the hue shown.</summary>
    public void SetColor(Color color)
    {
        var hsv = color.ToHsv();
        if (hsv.S <= 0 || hsv.V <= 0)
        {
            hsv = HsvColor.FromAhsv(hsv.A, _hsv.H, hsv.S, hsv.V);
        }

        if (hsv.ToRgb() != _hsv.ToRgb() || hsv.A != _hsv.A)
        {
            _hsv = hsv;
            Show(_hsv);
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ShowAlphaProperty && AlphaSlider is not null)
        {
            AlphaSlider.IsVisible = ShowAlpha;
        }
    }

    /// <param name="keepHueWhenGray">
    /// For a color that came in as RGB (HEX, R/G/B, a preset, the eyedropper): a gray has no hue of
    /// its own, so the one shown stays. The square and the sliders give their hue themselves.
    /// </param>
    private void SetFromUser(HsvColor hsv, bool keepHueWhenGray = true)
    {
        if (keepHueWhenGray && (hsv.S <= 0 || hsv.V <= 0))
        {
            hsv = HsvColor.FromAhsv(hsv.A, _hsv.H, hsv.S, hsv.V);
        }

        _hsv = hsv;
        Show(hsv);
        ColorPicked?.Invoke(this, hsv.ToRgb());
    }

    private void Show(HsvColor hsv)
    {
        _updating = true;
        try
        {
            var opaque = HsvColor.FromAhsv(1, hsv.H, hsv.S, hsv.V);
            Spectrum.HsvColor = opaque;
            HueSlider.HsvColor = opaque;
            AlphaSlider.HsvColor = hsv;
            AlphaSlider.IsVisible = ShowAlpha;

            var rgb = hsv.ToRgb();
            HexInput.Text = $"#{rgb.R:X2}{rgb.G:X2}{rgb.B:X2}";
            RedInput.Text = rgb.R.ToString(CultureInfo.InvariantCulture);
            GreenInput.Text = rgb.G.ToString(CultureInfo.InvariantCulture);
            BlueInput.Text = rgb.B.ToString(CultureInfo.InvariantCulture);
            if (_eyedropperTimer is null)
            {
                EyedropperSwatch.Background = new SolidColorBrush(Color.FromRgb(rgb.R, rgb.G, rgb.B));
            }
        }
        finally
        {
            _updating = false;
        }
    }

    private void ApplyHexInput()
    {
        var text = (HexInput.Text ?? string.Empty).Trim();
        if (Color.TryParse(text.StartsWith('#') ? text : "#" + text, out var color))
        {
            var hsv = color.ToHsv();
            SetFromUser(HsvColor.FromAhsv(_hsv.A, hsv.H, hsv.S, hsv.V));
        }
        else
        {
            Show(_hsv);
        }
    }

    private void ApplyChannelInputs()
    {
        static byte? Channel(TextBox box) =>
            int.TryParse(box.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? (byte)Math.Clamp(value, 0, 255) : null;

        if (Channel(RedInput) is { } red && Channel(GreenInput) is { } green && Channel(BlueInput) is { } blue)
        {
            var hsv = Color.FromRgb(red, green, blue).ToHsv();
            SetFromUser(HsvColor.FromAhsv(_hsv.A, hsv.H, hsv.S, hsv.V));
        }
        else
        {
            Show(_hsv);
        }
    }

    // ---------------------------------------------------------------- eyedropper

    private void StartEyedropper()
    {
        if (_eyedropperTimer is not null)
        {
            return;
        }

        _sampler = ScreenColorPicker.Open();
        if (_sampler is null)
        {
            ShowHint("The screen can't be read here.");
            return;
        }

        _beforeEyedropper = _hsv;
        _waitForRelease = true; // the click that started it is still down
        ShowHint("Point at a color anywhere on the screen, then click (or press Space). Esc cancels.");
        _eyedropperTimer = new DispatcherTimer(EyedropperInterval, DispatcherPriority.Input, (_, _) => PollEyedropper());
        _eyedropperTimer.Start();
    }

    private void PollEyedropper()
    {
        if (_sampler is null)
        {
            return;
        }

        var sample = _sampler.Sample();
        if (sample.Color is { } rgb)
        {
            var color = Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
            EyedropperSwatch.Background = new SolidColorBrush(color);
            ShowHint($"{color.R:X2}{color.G:X2}{color.B:X2}: click (or press Space) to take it, Esc to cancel.", prefix: "#");
        }

        if (sample.Cancel)
        {
            _hsv = _beforeEyedropper;
            StopEyedropper(keep: false);
            return;
        }

        if (_waitForRelease)
        {
            _waitForRelease = sample.Pick;
            return;
        }

        if (sample.Pick && sample.Color is { } picked)
        {
            StopEyedropper(keep: true);
            var hsv = Color.FromRgb((byte)(picked >> 16), (byte)(picked >> 8), (byte)picked).ToHsv();
            SetFromUser(HsvColor.FromAhsv(_hsv.A, hsv.H, hsv.S, hsv.V));
        }
    }

    private void StopEyedropper(bool keep)
    {
        _eyedropperTimer?.Stop();
        _eyedropperTimer = null;
        _sampler?.Dispose();
        _sampler = null;
        EyedropperHint.IsVisible = false;
        if (!keep)
        {
            Show(_hsv);
        }
        else
        {
            var rgb = _hsv.ToRgb();
            EyedropperSwatch.Background = new SolidColorBrush(Color.FromRgb(rgb.R, rgb.G, rgb.B));
        }
    }

    private void ShowHint(string text, string prefix = "")
    {
        EyedropperHint.Text = prefix + text;
        EyedropperHint.IsVisible = true;
    }
}

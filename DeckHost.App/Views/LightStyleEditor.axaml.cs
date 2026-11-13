using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using DeckHost.App.ViewModels;
using DeckHost.Core.Devices;
using System;

namespace DeckHost.App.Views;

/// <summary>The light editor (<see cref="LightStyleEditorViewModel"/>) with a live preview of four LEDs.</summary>
public partial class LightStyleEditor : UserControl
{
    private const int PreviewLeds = 4;
    private readonly SolidColorBrush[] _brushes = new SolidColorBrush[PreviewLeds];
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private readonly DateTime _start = DateTime.UtcNow;

    public LightStyleEditor()
    {
        InitializeComponent();
        for (var index = 0; index < PreviewLeds; index++)
        {
            _brushes[index] = new SolidColorBrush(Colors.Black);
        }

        Preview.ItemsSource = _brushes;
        _timer.Tick += (_, _) => Tick();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Tick();
        _timer.Start();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _timer.Stop();
        base.OnDetachedFromVisualTree(e);
    }

    private void Tick()
    {
        if (DataContext is not LightStyleEditorViewModel editor || !IsEffectivelyVisible)
        {
            return;
        }

        var style = editor.PreviewStyle;
        var clock = DateTime.UtcNow - _start;
        for (var index = 0; index < PreviewLeds; index++)
        {
            var (red, green, blue) = LightEffectMath.ColorAt(style, clock, index, PreviewLeds);
            _brushes[index].Color = Color.FromRgb(red, green, blue);
        }
    }
}

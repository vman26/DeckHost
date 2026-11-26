using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using DeckHost.App.Platform;
using DeckHost.Contracts.Enums;
using DeckHost.Contracts.Models;
using DeckHost.Core.Interfaces;
using DeckHost.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace DeckHost.App.Views;

/// <summary>
/// A deck on the desktop: a small window, on top of the others if you like, that shows a device's
/// keys as DeckHost draws them and presses them when you click (the Web Deck by default, so it
/// works without hardware; any other device can be picked). Clicks go through the host like the
/// device's own input (<see cref="IDeviceHostService.SimulateInput"/>). Opened from ☰ and the tray,
/// where it appears next to the mouse pointer on Windows.
/// </summary>
public sealed class DesktopDeckWindow : Window
{
    public const string DefaultDeviceId = "web.deck";

    private static DesktopDeckWindow? s_open;

    private readonly IDeviceHostService _host;
    private readonly Canvas _canvas = new();
    private readonly ComboBox _devicePicker = new() { MinWidth = 160 };
    private readonly CheckBox _onTop = new() { Content = "On top" };
    private readonly Dictionary<string, (Image Image, WriteableBitmap? Bitmap)> _images = new(StringComparer.Ordinal);
    private readonly HashSet<string> _held = new(StringComparer.Ordinal);
    private DeviceDefinition? _device;
    private bool _filling;

    public DesktopDeckWindow()
        : this(null!)
    {
    }

    public DesktopDeckWindow(IDeviceHostService host)
    {
        _host = host;
        Title = "DeckHost deck";
        Width = 420;
        Height = 260;
        MinWidth = 160;
        MinHeight = 120;
        Background = Brush("C.0B1220") ?? Brushes.Black;
        var settings = AppSettings.Load();
        Topmost = settings.DesktopDeckOnTop;
        _onTop.IsChecked = Topmost;
        _onTop.IsCheckedChanged += (_, _) =>
        {
            Topmost = _onTop.IsChecked == true;
            AppSettings.Save(AppSettings.Load() with { DesktopDeckOnTop = Topmost });
        };
        Avalonia.Automation.AutomationProperties.SetName(_devicePicker, "Device");
        _devicePicker.SelectionChanged += (_, _) =>
        {
            if (!_filling && _devicePicker.SelectedItem is DeviceChoice choice && choice.Id != _device?.Id)
            {
                AppSettings.Save(AppSettings.Load() with { DesktopDeckDevice = choice.Id });
                Show(choice.Id);
            }
        };

        var bar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(8, 6), Children = { _devicePicker, _onTop } };
        var root = new DockPanel();
        DockPanel.SetDock(bar, Dock.Top);
        root.Children.Add(bar);
        root.Children.Add(new Viewbox { Stretch = Stretch.Uniform, Margin = new Thickness(8), Child = _canvas });
        Content = root;

        if (host is null)
        {
            return;
        }

        host.ControlFrameRendered += OnFrame;
        host.DevicesChanged += OnDevicesChanged;
        Closed += (_, _) =>
        {
            host.ControlFrameRendered -= OnFrame;
            host.DevicesChanged -= OnDevicesChanged;
            ReleaseHeld();
            s_open = null;
        };
        FillPicker(settings.DesktopDeckDevice ?? DefaultDeviceId);
    }

    /// <summary>Opens the desktop deck (or brings it forward), next to the mouse pointer where the system tells where it is.</summary>
    public static void ShowDeck(IDeviceHostService host)
    {
        var window = s_open ??= new DesktopDeckWindow(host);
        if (CursorPosition() is { } cursor)
        {
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Position = new PixelPoint(Math.Max(0, cursor.X - 120), Math.Max(0, cursor.Y - 60));
        }
        else if (!window.IsVisible)
        {
            window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        window.Show();
        window.Activate();
    }

    private sealed record DeviceChoice(string Id, string Name)
    {
        public override string ToString() => Name;
    }

    private static bool HasPictures(DeviceDefinition device) => device.Controls.Any(static control => control.RenderWidth > 0 && control.Inputs.HasFlag(InputCapabilities.Press));

    private void FillPicker(string wanted)
    {
        var choices = _host.Devices.Where(HasPictures).Select(static device => new DeviceChoice(device.Id, device.Name)).ToList();
        var pick = choices.FirstOrDefault(choice => choice.Id == wanted) ?? choices.FirstOrDefault(static choice => choice.Id == DefaultDeviceId) ?? choices.FirstOrDefault();
        _filling = true;
        try
        {
            _devicePicker.ItemsSource = choices;
            _devicePicker.SelectedItem = pick;
        }
        finally
        {
            _filling = false;
        }

        if (pick is null)
        {
            _canvas.Children.Clear();
            _canvas.Children.Add(new TextBlock { Text = "Turn the Web Deck on in the Plugins page to get a deck here.", Foreground = Brushes.White, Width = 300, TextWrapping = TextWrapping.Wrap });
        }
        else
        {
            Show(pick.Id);
        }
    }

    private void OnDevicesChanged() => Dispatcher.UIThread.Post(() => FillPicker(_device?.Id ?? DefaultDeviceId));

    private void Show(string deviceId)
    {
        ReleaseHeld();
        _device = _host.Devices.FirstOrDefault(device => device.Id == deviceId);
        _canvas.Children.Clear();
        _images.Clear();
        if (_device is null)
        {
            return;
        }

        var controls = _device.Controls.Where(static control => control.Bounds.Width > 0).ToList();
        _canvas.Width = controls.Count == 0 ? 100 : controls.Max(static control => control.Bounds.X + control.Bounds.Width);
        _canvas.Height = controls.Count == 0 ? 100 : controls.Max(static control => control.Bounds.Y + control.Bounds.Height);
        foreach (var control in controls)
        {
            var image = new Image { Width = control.Bounds.Width, Height = control.Bounds.Height, Stretch = Stretch.Fill };
            Control face = control.RenderWidth > 0
                ? new Border
                {
                    Width = control.Bounds.Width,
                    Height = control.Bounds.Height,
                    CornerRadius = new CornerRadius(control.Shape == ControlShape.Round ? control.Bounds.Width / 2 : 8),
                    ClipToBounds = true,
                    Background = Brush("C.1E222B") ?? Brushes.DimGray,
                    Child = image,
                }
                : new Ellipse { Width = control.Bounds.Width, Height = control.Bounds.Height, Fill = Brush("C.334155") ?? Brushes.Gray };
            face.Cursor = new Cursor(StandardCursorType.Hand);
            ToolTip.SetTip(face, control.DisplayName);
            Avalonia.Automation.AutomationProperties.SetName(face, control.DisplayName);
            var id = control.Id;
            var inputs = control.Inputs;
            face.PointerPressed += (_, e) =>
            {
                e.Handled = true;
                if (inputs.HasFlag(InputCapabilities.Press) && _held.Add(id))
                {
                    _host.SimulateInput(new DeviceInputEvent(_device.Id, id, InputCapabilities.Press, 1));
                }
            };
            face.PointerReleased += (_, e) =>
            {
                e.Handled = true;
                if (_held.Remove(id) && inputs.HasFlag(InputCapabilities.Release))
                {
                    _host.SimulateInput(new DeviceInputEvent(_device.Id, id, InputCapabilities.Release, 0));
                }
            };
            face.PointerWheelChanged += (_, e) =>
            {
                if (inputs.HasFlag(InputCapabilities.Rotate) && e.Delta.Y != 0)
                {
                    e.Handled = true;
                    _host.SimulateInput(new DeviceInputEvent(_device.Id, id, InputCapabilities.Rotate, e.Delta.Y > 0 ? 1 : -1));
                }
            };
            Canvas.SetLeft(face, control.Bounds.X);
            Canvas.SetTop(face, control.Bounds.Y);
            _canvas.Children.Add(face);
            _images[id] = (image, null);
        }

        _host.Redraw(_device.Id);
    }

    private void ReleaseHeld()
    {
        if (_device is { } device)
        {
            foreach (var id in _held)
            {
                _host.SimulateInput(new DeviceInputEvent(device.Id, id, InputCapabilities.Release, 0));
            }
        }

        _held.Clear();
    }

    private void OnFrame(ControlFrame frame) => Dispatcher.UIThread.Post(() =>
    {
        if (_device is null || !string.Equals(frame.DeviceId, _device.Id, StringComparison.OrdinalIgnoreCase) || !_images.TryGetValue(frame.ControlId, out var entry))
        {
            return;
        }

        var bitmap = FrameBitmaps.Copy(frame.Width, frame.Height, frame.Rgba, entry.Bitmap);
        _images[frame.ControlId] = (entry.Image, bitmap);
        entry.Image.Source = bitmap;
        entry.Image.InvalidateVisual();
    });

    private static IBrush? Brush(string key) =>
        Application.Current is { } app && app.TryFindResource(key, app.ActualThemeVariant, out var value) && value is IBrush brush ? brush : null;

    private static PixelPoint? CursorPosition()
    {
        if (OperatingSystem.IsWindows() && GetCursorPos(out var point))
        {
            return new PixelPoint(point.X, point.Y);
        }

        return null;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out NativePoint point);
}

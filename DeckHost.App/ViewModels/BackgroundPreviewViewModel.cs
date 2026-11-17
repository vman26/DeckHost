using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Avalonia.Media;
using Avalonia.Threading;
using DeckHost.App.Views;
using DeckHost.Contracts.Interfaces;
using DeckHost.Contracts.Models;
using DeckHost.Core.Models;
using DeckHost.Core.Rendering;
using DeckHost.Core.State;

namespace DeckHost.App.ViewModels;

/// <summary>A control's display and what the background looks like on it.</summary>
public sealed record BackgroundPreviewTile(Rect Bounds, ControlFrame Frame);

/// <summary>
/// A small device in the Background Studio showing a background the way the device does (the same drawing as the host,
/// <see cref="BackgroundPreview"/>). It draws again, soon, after an edit (<see cref="Invalidate"/>) and when a variable
/// a layer follows changes. Several changes in a row become one drawing, and a drawing never overlaps the next.
/// </summary>
public sealed class BackgroundPreviewViewModel : ViewModelBase, IDisposable
{
    private static readonly TimeSpan CoalesceDelay = TimeSpan.FromMilliseconds(60);

    private readonly Func<DeviceDefinition?> _device;
    private readonly Func<ProfileBackground?> _background;
    private readonly IVariableStore _variables;
    private readonly bool _ownsVariables;
    private readonly bool _renderInline;
    private readonly SkiaDrawingResources _resources = new();
    private readonly IDisposable? _subscription;
    private volatile HashSet<string> _boundKeys = [];
    private DispatcherTimer? _timer;
    private bool _rendering;
    private bool _renderAgain;
    private bool _disposed;
    private DeviceGeometry _geometry = DeviceGeometry.Empty;
    private IBrush _screen = Brushes.Transparent;
    private IReadOnlyList<BackgroundPreviewTile> _tiles = [];
    private string _deviceName = string.Empty;

    public BackgroundPreviewViewModel(VariableStore? variables, Func<DeviceDefinition?> device, Func<ProfileBackground?> background)
        : this(variables, device, background, renderInline: false)
    {
    }

    /// <param name="renderInline">Draws on the calling thread at once (tests); otherwise on a background thread after a short delay.</param>
    internal BackgroundPreviewViewModel(VariableStore? variables, Func<DeviceDefinition?> device, Func<ProfileBackground?> background, bool renderInline)
    {
        _device = device;
        _background = background;
        _renderInline = renderInline;
        _ownsVariables = variables is null;
        _variables = variables ?? new VariableStore();
        _subscription = variables?.Changes
            .Where(change => _boundKeys.Contains(change.Key))
            .Subscribe(_ => OnBoundVariableChanged());
        Invalidate();
    }

    /// <summary>Where the device's body and screens are (the same shape as the editor's canvas).</summary>
    public DeviceGeometry Geometry
    {
        get => _geometry;
        private set => SetProperty(ref _geometry, value);
    }

    /// <summary>The screens' color between the controls, like the canvas.</summary>
    public IBrush Screen
    {
        get => _screen;
        private set => SetProperty(ref _screen, value);
    }

    /// <summary>The background on each display, at the display's own size.</summary>
    public IReadOnlyList<BackgroundPreviewTile> Tiles
    {
        get => _tiles;
        private set => SetProperty(ref _tiles, value);
    }

    public bool HasDevice => _geometry.Width > 0;

    public string DeviceName
    {
        get => _deviceName;
        private set => SetProperty(ref _deviceName, value);
    }

    /// <summary>The background, the device or a value it shows changed: draws it again soon.</summary>
    public void Invalidate()
    {
        if (_disposed)
        {
            return;
        }

        var background = _background();
        _boundKeys = [.. BackgroundLayers.BoundVariables(background)];

        if (_renderInline)
        {
            Apply(_device(), background, Render(_device(), background));
            return;
        }

        if (_timer is null)
        {
            _timer = new DispatcherTimer(CoalesceDelay, DispatcherPriority.Background, (_, _) =>
            {
                _timer?.Stop();
                _timer = null;
                StartRender();
            });
            _timer.Start();
        }
    }

    private void OnBoundVariableChanged()
    {
        if (_renderInline)
        {
            Invalidate();
        }
        else
        {
            Dispatcher.UIThread.Post(Invalidate);
        }
    }

    private void StartRender()
    {
        if (_disposed)
        {
            return;
        }

        if (_rendering)
        {
            _renderAgain = true;
            return;
        }

        var device = _device();
        var background = _background();
        _boundKeys = [.. BackgroundLayers.BoundVariables(background)];
        _rendering = true;
        _ = Task.Run(() => Render(device, background)).ContinueWith(
            task => Dispatcher.UIThread.Post(() =>
            {
                _rendering = false;
                if (_disposed)
                {
                    _resources.Dispose();
                    return;
                }

                Apply(device, background, task.IsCompletedSuccessfully ? task.Result : []);
                if (_renderAgain)
                {
                    _renderAgain = false;
                    StartRender();
                }
            }),
            TaskScheduler.Default);
    }

    private IReadOnlyList<ControlFrame> Render(DeviceDefinition? device, ProfileBackground? background) =>
        device is null ? [] : BackgroundPreview.Render(device, background, _variables, _resources);

    private void Apply(DeviceDefinition? device, ProfileBackground? background, IReadOnlyList<ControlFrame> frames)
    {
        var geometry = device is null ? DeviceGeometry.Empty : DeviceGeometry.Of(device);
        var (red, green, blue) = ProfileBackdrop.GetColor(background);
        Screen = new SolidColorBrush(Color.FromRgb(red, green, blue));
        Geometry = geometry;
        DeviceName = device?.Name ?? string.Empty;
        Tiles = device is null
            ? []
            : [.. frames.Select(frame => new BackgroundPreviewTile(geometry.Place(device.Controls.First(control => control.Id == frame.ControlId)), frame))];
        OnPropertyChanged(nameof(HasDevice));
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _subscription?.Dispose();
        _timer?.Stop();
        _timer = null;
        if (!_rendering)
        {
            _resources.Dispose();
        }

        if (_ownsVariables && _variables is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}

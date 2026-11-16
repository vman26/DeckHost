using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using System;
using System.Linq;

namespace DeckHost.App.Simulator;

/// <summary>
/// The device simulator's window (<c>DeckHost --simulator</c>): the model picker, the device and a
/// status line. DeckHost sees it as a connected device of the chosen model.
/// </summary>
public sealed class SimulatorWindow : Window
{
    private readonly SimulatorViewModel _viewModel;
    private readonly ComboBox _models = new() { MinWidth = 320, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _status = new() { VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(Color.Parse("#8A93A6")) };
    private readonly SimulatorSurface _surface;
    private readonly TranslateTransform _shake = new();
    private bool _syncing;

    public SimulatorWindow(SimulatorViewModel viewModel)
    {
        _viewModel = viewModel;
        _surface = new SimulatorSurface(viewModel) { RenderTransform = _shake };
        Title = "DeckHost device simulator";
        Width = 960;
        Height = 640;
        MinWidth = 420;
        MinHeight = 320;
        Background = new SolidColorBrush(Color.Parse("#111318"));
        Icon = LoadIcon();

        var header = new DockPanel { Margin = new Thickness(16, 12), LastChildFill = true };
        var label = new TextBlock { Text = "Simulate", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
        DockPanel.SetDock(label, Dock.Left);
        DockPanel.SetDock(_models, Dock.Left);
        _status.Margin = new Thickness(16, 0, 0, 0);
        header.Children.Add(label);
        header.Children.Add(_models);
        header.Children.Add(_status);

        var hint = new TextBlock
        {
            Text = "Click to press · mouse wheel turns knobs and switches · drag across a touch strip to swipe",
            Foreground = new SolidColorBrush(Color.Parse("#5C6576")),
            Margin = new Thickness(16, 6, 16, 12),
            FontSize = 12,
        };

        var root = new DockPanel();
        DockPanel.SetDock(header, Dock.Top);
        DockPanel.SetDock(hint, Dock.Bottom);
        root.Children.Add(header);
        root.Children.Add(hint);
        root.Children.Add(_surface);
        Content = root;

        _models.SelectionChanged += (_, _) =>
        {
            if (!_syncing && _models.SelectedIndex >= 0 && _models.SelectedIndex < _viewModel.Models.Count)
            {
                _viewModel.Choose(_viewModel.Models[_models.SelectedIndex].Key);
            }
        };
        _viewModel.ModelsChanged += (_, _) => SyncModels();
        _viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SimulatorViewModel.Status))
            {
                _status.Text = _viewModel.Status;
                _surface.InvalidateVisual();
            }
        };
        _viewModel.Vibrated += (_, _) => Shake();
        _status.Text = _viewModel.Status;

        Opened += (_, _) => _ = _viewModel.RunAsync();
        Closed += async (_, _) => await _viewModel.DisposeAsync();
    }

    private void SyncModels()
    {
        _syncing = true;
        _models.ItemsSource = _viewModel.Models.Select(static model => $"{model.Device.Name}  ·  {model.Plugin}").ToList();
        var current = _viewModel.Models.ToList().FindIndex(model => model.Key == _viewModel.CurrentModel);
        _models.SelectedIndex = current;
        _syncing = false;
    }

    /// <summary>A short shake, as the device's vibration motor would.</summary>
    private void Shake()
    {
        var step = 0;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        timer.Tick += (_, _) =>
        {
            step++;
            _shake.X = step >= 8 ? 0 : (step % 2 == 0 ? 2.5 : -2.5);
            if (step >= 8)
            {
                timer.Stop();
            }
        };
        timer.Start();
    }

    private static WindowIcon? LoadIcon()
    {
        try
        {
            return new WindowIcon(Avalonia.Platform.AssetLoader.Open(new Uri("avares://DeckHost.App/Assets/deckhost.ico")));
        }
        catch (Exception)
        {
            return null;
        }
    }
}

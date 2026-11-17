using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;

namespace DeckHost.App.Simulator;

/// <summary>The application for <c>DeckHost --simulator [model]</c>: one simulator window, its own process.</summary>
public sealed class SimulatorApp : Application
{
    /// <summary>The model to start as (<c>{pluginId}/{deviceId}</c>), else the last one used.</summary>
    internal static string? StartModel { get; set; }

    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
        RequestedThemeVariant = ThemeVariant.Dark;
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new SimulatorWindow(new SimulatorViewModel(StartModel));
        }

        base.OnFrameworkInitializationCompleted();
    }
}

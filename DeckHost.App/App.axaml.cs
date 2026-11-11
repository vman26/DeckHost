using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using DeckHost.App.Plugins;
using DeckHost.App.ViewModels;
using DeckHost.App.Views;
using DeckHost.Core.Devices;
using DeckHost.Core.State;
using DeckHost.Device.MiraBox;
using System.Threading;

namespace DeckHost.App;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var devicePlugin = new MiraBoxDevicePlugin();
            var applicationPlugins = FirstPartyApplicationPlugins.Create();
            var variableStore = new VariableStore();
            var deviceHostService = new DeviceHostService([devicePlugin], applicationPlugins, variableStore);
            var mainViewModel = new MainViewModel(deviceHostService, devicePlugin, applicationPlugins);

            desktop.MainWindow = new MainWindow
            {
                DataContext = mainViewModel,
            };

            desktop.MainWindow.Opened += async (_, _) => await deviceHostService.StartAsync(CancellationToken.None);
        }

        base.OnFrameworkInitializationCompleted();
    }
}
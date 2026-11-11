using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using DeckHost.App.Plugins;
using DeckHost.App.ViewModels;
using DeckHost.App.Views;
using DeckHost.Contracts.Interfaces;
using DeckHost.Core.Devices;
using DeckHost.Core.Profiles;
using DeckHost.Core.State;
using DeckHost.Device.MiraBox;
using System;
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
            // Composition root. Device plugins are the only place that knows about hardware.
            IDevicePlugin[] devicePlugins = [new MiraBoxDevicePlugin()];
            var applicationPlugins = FirstPartyApplicationPlugins.Create();
            var variableStore = new VariableStore();
            var deviceHostService = new DeviceHostService(devicePlugins, applicationPlugins, variableStore);
            var mainViewModel = new MainViewModel(deviceHostService, devicePlugins, applicationPlugins, ProfileStore.CreateDefault());

            desktop.MainWindow = new MainWindow
            {
                DataContext = mainViewModel,
            };

            desktop.MainWindow.Opened += async (_, _) => await deviceHostService.StartAsync(CancellationToken.None);

            desktop.Exit += (_, _) =>
            {
                mainViewModel.FlushPendingSave();

                // Blanks the device, stops the SDK threads and unloads the native bridge.
                // The host's worker never needs the UI thread, so waiting here cannot deadlock.
                deviceHostService.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(5));
                variableStore.Dispose();
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}

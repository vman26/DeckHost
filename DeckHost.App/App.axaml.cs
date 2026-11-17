using System.Net.Http;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.DependencyInjection;
using DeckHost.App.SystemPlugins;
using DeckHost.App.ViewModels;
using DeckHost.App.Views;
using DeckHost.Contracts.Interfaces;
using DeckHost.Core.Devices;
using DeckHost.Core.Interfaces;
using DeckHost.Core.Models;
using DeckHost.Core.Icons;
using DeckHost.Core.Plugins;
using DeckHost.Core.Profiles;
using DeckHost.Core.State;
using DeckHost.Core.StreamDeck;
using DeckHost.Device.MiraBox;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NLog.Extensions.Logging;
using System;
using System.IO;
using System.Threading;

namespace DeckHost.App;

public partial class App : Application
{
    /// <summary>Switches between the dark, light and system look; everything follows at once.</summary>
    public static void ApplyTheme(string? theme)
    {
        if (Current is { } app)
        {
            app.RequestedThemeVariant = Platform.AppTheme.ToVariant(theme);
        }
    }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        ApplyTheme(Platform.AppSettings.Load().Theme);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // An exception nobody catches on the UI thread (a web view that can't start) is reported, not fatal.
            Platform.CrashReporter.InstallDispatcherSafetyNet();

            var services = ConfigureServices();
            Ioc.Default.ConfigureServices(services);

            var logger = services.GetRequiredService<ILogger<App>>();
            var deviceHostService = services.GetRequiredService<DeviceHostService>();
            var pluginCatalog = services.GetRequiredService<PluginCatalog>();

            // The host's runtime log (shown in the app) also goes to the log files.
            var runtimeLogger = services.GetRequiredService<ILoggerFactory>().CreateLogger("Runtime");
            deviceHostService.RuntimeLogReceived += entry => runtimeLogger.Log(ToLogLevel(entry.Level), "{Message}", entry.Message);

            // Loading starts the Stream Deck plugin processes, so the host's device list must exist first.
            pluginCatalog.Load();
            deviceHostService.SetApplicationPlugins(pluginCatalog.EnabledApplicationPlugins);

            var mainViewModel = services.GetRequiredService<MainViewModel>();
            var mainWindow = new MainWindow { DataContext = mainViewModel };
            var settings = Platform.AppSettings.Load();
            var exiting = false;
            var osShutdown = false;
            AppSettingsViewModel.Changed += changed => settings = changed;

            // DeckHost keeps running in the tray: the device works without the window.
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            mainWindow.Closing += (_, e) =>
            {
                if (KeepsWindowOnClose(e.CloseReason, exiting, settings.KeepsRunningInTray))
                {
                    e.Cancel = true;
                    mainWindow.Hide();
                }
                else if (e.CloseReason is WindowCloseReason.OSShutdown or WindowCloseReason.ApplicationShutdown)
                {
                    exiting = true;
                    osShutdown = e.CloseReason == WindowCloseReason.OSShutdown;
                }
            };
            mainWindow.Closed += (_, _) => desktop.Shutdown();

            void ShowMainWindow()
            {
                mainWindow.Show();
                if (mainWindow.WindowState == WindowState.Minimized)
                {
                    mainWindow.WindowState = WindowState.Normal;
                }

                mainWindow.Activate();
            }

            void ExitApplication()
            {
                exiting = true;
                desktop.Shutdown();
            }

            // The safety net's notice, and the way out of safe mode: forget the failures and start again.
            Platform.CrashReporter.Recovered += _ => Avalonia.Threading.Dispatcher.UIThread.Post(mainViewModel.ShowRecoveredNotice);
            mainViewModel.RestartNormallyRequested += () =>
            {
                Platform.CrashReporter.Guard?.ClearFailures();
                if (Environment.ProcessPath is { } executable)
                {
                    var start = new System.Diagnostics.ProcessStartInfo(executable) { UseShellExecute = false };
                    start.ArgumentList.Add("--after-exit");
                    start.ArgumentList.Add(Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    System.Diagnostics.Process.Start(start)?.Dispose();
                }

                ExitApplication();
            };

            var updates = services.GetRequiredService<UpdateViewModel>();
            updates.RestartRequested += () =>
            {
                logger.LogInformation("Exiting to install an update.");
                ExitApplication();
            };

            CreateTrayIcon(
                ShowMainWindow,
                () =>
                {
                    ShowMainWindow();
                    mainWindow.OpenAppSettings();
                },
                ExitApplication,
                updates,
                () =>
                {
                    ShowMainWindow();
                    mainWindow.OpenUpdates();
                });
            ListenForActivation(ShowMainWindow);
            updates.StartAutomaticChecks();
            services.GetRequiredService<PluginUpdatesViewModel>().StartAutomaticChecks();

            if (Program.StartedAtLogin && settings.StartMinimized)
            {
                logger.LogInformation("DeckHost started with Windows; running in the tray.");
            }
            else
            {
                desktop.MainWindow = mainWindow;
            }

            // After a crash: Help and support; on the very first start: the welcome tour.
            var startPagesShown = false;
            mainWindow.Opened += (_, _) =>
            {
                if (!startPagesShown)
                {
                    startPagesShown = true;
                    mainWindow.ShowStartPages();

                    // Staying up for a while is what makes a run "healthy" (safe mode counts runs that didn't).
                    Avalonia.Threading.DispatcherTimer.RunOnce(() => Platform.CrashReporter.Guard?.MarkHealthy(), Platform.StartupGuard.HealthyAfter);
                }
            };

            logger.LogInformation("DeckHost {Version} started on {OS}.", AppInfo.Version, System.Runtime.InteropServices.RuntimeInformation.OSDescription);
            _ = deviceHostService.StartAsync(CancellationToken.None);

            desktop.Exit += (_, _) =>
            {
                Platform.CrashReporter.Guard?.MarkCleanExit();
                mainViewModel.FlushPendingSave();

                // Blanks the device, stops the SDK threads and unloads the native bridge.
                // The host's worker never needs the UI thread, so waiting here cannot deadlock.
                // When Windows is shutting down it won't wait long for us: blank the device quickly.
                deviceHostService.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(osShutdown ? 2 : 5));
                pluginCatalog.Dispose();

                // A downloaded update (Install updates automatically) installs now; its installer
                // waits for this process to end and doesn't start DeckHost again.
                updates.InstallPendingOnExit();
                logger.LogInformation("DeckHost stopped.");
                services.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(osShutdown ? 2 : 5));
                NLog.LogManager.Shutdown();
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void CreateTrayIcon(Action open, Action openSettings, Action exit, UpdateViewModel updates, Action showUpdate)
    {
        var menu = new NativeMenu();
        var openItem = new NativeMenuItem("Open DeckHost");
        openItem.Click += (_, _) => open();

        // While the window is in the tray, the tray menu announces an update.
        var updateItem = new NativeMenuItem { IsVisible = false };
        updateItem.Click += (_, _) => showUpdate();
        updates.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(UpdateViewModel.IsUpdateAvailable) or nameof(UpdateViewModel.IsSkipped))
            {
                updateItem.Header = $"Update to DeckHost {updates.Latest?.Version}…";
                updateItem.IsVisible = updates.IsUpdateAvailable && !updates.IsSkipped;
            }
        };
        menu.Items.Add(updateItem);
        var settingsItem = new NativeMenuItem("Settings…");
        settingsItem.Click += (_, _) => openSettings();
        var exitItem = new NativeMenuItem("Exit");
        exitItem.Click += (_, _) => exit();
        menu.Items.Add(openItem);
        menu.Items.Add(settingsItem);
        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(exitItem);

        var tray = new TrayIcon
        {
            Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://DeckHost.App/Assets/deckhost.ico"))),
            ToolTipText = "DeckHost",
            Menu = menu,
        };
        tray.Clicked += (_, _) => open();
        TrayIcon.SetIcons(this, [tray]);
    }

    /// <summary>A second launch signals this instance to show its window instead of starting again.</summary>
    private static void ListenForActivation(Action show)
    {
        if (Program.ActivateRequested is not { } activate)
        {
            return;
        }

        var thread = new Thread(() =>
        {
            try
            {
                while (activate.WaitOne())
                {
                    Dispatcher.UIThread.Post(show);
                }
            }
            catch (ObjectDisposedException)
            {
            }
        })
        {
            IsBackground = true,
            Name = "DeckHost activation",
        };
        thread.Start();
    }

    /// <summary>
    /// The composition root. Everything is a singleton except the view models of windows that can
    /// be opened more than once. Device plugins are the only place that knows about hardware.
    /// </summary>
    /// <summary>
    /// Whether closing the main window only hides it (DeckHost stays in the tray). Only the user's own
    /// close does: when Windows shuts down or signs out, or the application itself shuts down,
    /// refusing the close made Windows wait for DeckHost ("preventing shutdown").
    /// </summary>
    internal static bool KeepsWindowOnClose(WindowCloseReason reason, bool exiting, bool keepsRunningInTray) =>
        !exiting && keepsRunningInTray && reason is not (WindowCloseReason.OSShutdown or WindowCloseReason.ApplicationShutdown);

    private static ServiceProvider ConfigureServices()
    {
        var appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DeckHost");
        var services = new ServiceCollection();

        services.AddLogging(builder =>
        {
            builder.ClearProviders();
            builder.SetMinimumLevel(LogLevel.Debug);
            builder.AddNLog(Platform.Logging.CreateConfiguration());
        });

        // Devices
        services.AddSingleton<MiraBoxDevicePlugin>();
        services.AddSingleton<IDevicePlugin>(provider => provider.GetRequiredService<MiraBoxDevicePlugin>());
        services.AddSingleton<DeckHost.Device.Elgato.ElgatoDevicePlugin>();
        services.AddSingleton<IDevicePlugin>(provider => provider.GetRequiredService<DeckHost.Device.Elgato.ElgatoDevicePlugin>());
        services.AddSingleton<DeckHost.Device.Hid.HidDevicePlugin>();
        services.AddSingleton<IDevicePlugin>(provider => provider.GetRequiredService<DeckHost.Device.Hid.HidDevicePlugin>());
        services.AddSingleton<DeckHost.Device.Hid.IHidDeviceSource, DeckHost.Device.Hid.HidSharpDeviceSource>();
        services.AddSingleton<DeckHost.Device.Web.WebDeckPlugin>();
        services.AddSingleton<IDevicePlugin>(provider => provider.GetRequiredService<DeckHost.Device.Web.WebDeckPlugin>());

        // Devices of OpenAction device plugins (OpenDeck's Mirabox, Ajazz, ... plugins): the runtime
        // runs the plugin, this turns the devices it registers into DeckHost devices.
        services.AddSingleton(provider => new OpenActionDevicePlugin(provider.GetRequiredService<StreamDeckRuntime>(), Path.Combine(appData, "devices")));
        services.AddSingleton<IDevicePlugin>(provider => provider.GetRequiredService<OpenActionDevicePlugin>());

        // The device simulator (DeckHost --simulator) connects here and can be any model the other
        // device plugins describe; they are asked when a simulator connects, not now. Only listens
        // with DeckHost settings → Developer → Device simulator on.
        services.AddSingleton(provider => new DeckHost.Device.Simulator.SimulatorDevicePlugin(appData, () => provider.GetServices<IDevicePlugin>(), Platform.AppSettings.Load().DeviceSimulator));
        services.AddSingleton<IDevicePlugin>(provider => provider.GetRequiredService<DeckHost.Device.Simulator.SimulatorDevicePlugin>());

        // Runtime
        services.AddSingleton<VariableStore>();
        services.AddSingleton<IVariableStore>(provider => provider.GetRequiredService<VariableStore>());
        services.AddSingleton(provider => new DeviceHostService(
            provider.GetServices<IDevicePlugin>(),
            [],
            provider.GetRequiredService<IVariableStore>()));
        services.AddSingleton<IDeviceHostService>(provider => provider.GetRequiredService<DeviceHostService>());

        // Plugins: Stream Deck SDK plugins run in their own processes via this runtime.
        services.AddSingleton(provider =>
        {
            var logger = provider.GetRequiredService<ILogger<StreamDeckRuntime>>();
            var runtime = new StreamDeckRuntime(
                Path.Combine(appData, "streamdeck"),
                Path.Combine(Platform.Logging.LogDirectory, "plugins"),
                (level, message) =>
                {
                    // Shown in the app's runtime log (which also writes to the log file).
                    if (provider.GetService<DeviceHostService>() is { } host)
                    {
                        host.AppendLog(level, message);
                    }
                    else
                    {
                        logger.Log(ToLogLevel(level), "{Message}", message);
                    }
                });
            runtime.DevicesProvider = () => provider.GetRequiredService<IDeviceHostService>().Devices;
            if (Platform.WebView2PluginHost.IsSupported && Platform.WebViewSupport.IsAvailable)
            {
                // HTML-based Stream Deck plugins run in hidden WebView2 pages.
                runtime.HtmlPluginHost = new Platform.WebView2PluginHost();
            }

            return runtime;
        });
        services.AddSingleton<IPluginStateTransfer>(provider => provider.GetRequiredService<StreamDeckRuntime>());
        services.AddSingleton(provider =>
        {
            var catalog = new PluginCatalog(
                PluginCatalog.DefaultRootDirectory,
                provider.GetServices<IDevicePlugin>(),
                SystemPluginSet.Create(),
                SystemPluginSet.RequiredPluginIds,
                provider.GetRequiredService<StreamDeckRuntime>())
            {
                // Safe mode: only the built-in plugins, so whatever crashed DeckHost can't run again.
                LoadsExtraPlugins = !Platform.SafeMode.IsOn,
            };

            // DeckHost plugins run in their own process (this program with --plugin-host), unless
            // the user moved one inside DeckHost on the Plugins page.
            if (Environment.ProcessPath is { } executable && !executable.EndsWith("dotnet", StringComparison.OrdinalIgnoreCase) && !executable.EndsWith("dotnet.exe", StringComparison.OrdinalIgnoreCase))
            {
                catalog.ProcessLauncher = new DeckHost.Core.Plugins.Isolation.SelfPluginLauncher(executable);
            }

            var pluginLogger = provider.GetRequiredService<ILoggerFactory>().CreateLogger("Plugins");
            catalog.PluginLog = (level, message) => pluginLogger.Log(level switch
            {
                "Error" => LogLevel.Error,
                "Warning" => LogLevel.Warning,
                _ => LogLevel.Information,
            }, "{Message}", message);
            return catalog;
        });

        // Icons: installed packs, and Iconify for searching open-source icon sets online.
        services.AddSingleton(_ => new IconLibrary(IconLibrary.DefaultRootDirectory));
        services.AddSingleton(_ =>
        {
            var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd($"DeckHost/{AppInfo.Version.Split('+')[0]}");
            return new IconifyClient(http);
        });

        // Profiles and view models
        services.AddSingleton(_ => ProfileStore.CreateDefault());
        services.AddSingleton(provider => new MainViewModel(
            provider.GetRequiredService<IDeviceHostService>(),
            provider.GetRequiredService<PluginCatalog>(),
            provider.GetRequiredService<ProfileStore>(),
            provider.GetRequiredService<IPluginStateTransfer>()));
        services.AddSingleton(provider => new Core.Firmware.FirmwareService(provider.GetServices<IDevicePlugin>(), provider.GetRequiredService<IDeviceHostService>()));
        services.AddSingleton(provider => new PluginDocsService(provider.GetRequiredService<PluginCatalog>()));
        services.AddSingleton(provider => new DeviceSettingsService(provider.GetServices<IDevicePlugin>(), () => provider.GetRequiredService<MainViewModel>(), provider.GetRequiredService<Core.Firmware.FirmwareService>()));
        services.AddSingleton(provider =>
        {
            // The user's store sources can change while DeckHost runs: read them for every check.
            var store = new PluginStore(PluginStore.CreateHttpClient());
            var updater = new PluginUpdater(provider.GetRequiredService<PluginCatalog>(), store, Path.Combine(Path.GetTempPath(), "DeckHost", "downloads"));
            return new PluginUpdatesViewModel(
                () =>
                {
                    store.DeckHostCatalogUrls = PluginStoreViewModel.LoadCatalogUrls();
                    return updater.CheckAsync();
                },
                (update, progress) => updater.InstallAsync(update, progress),
                Platform.AppSettings.Load,
                Platform.AppSettings.Save);
        });
        services.AddTransient(provider => new PluginManagerViewModel(
            provider.GetRequiredService<PluginCatalog>(),
            provider.GetRequiredService<IDeviceHostService>(),
            provider.GetRequiredService<DeviceSettingsService>(),
            provider.GetRequiredService<PluginUpdatesViewModel>()));
        services.AddTransient(provider => new ProfileManagerViewModel(provider.GetRequiredService<MainViewModel>(), provider.GetService<VariableStore>()));

        // Updates: GitHub releases of DeckHost.
        services.AddSingleton(_ => new UpdateViewModel(
            new Core.Updates.UpdateChecker(Core.Updates.UpdateChecker.CreateHttpClient(), AppInfo.UpdateRepository),
            AppInfo.Version,
            Platform.AppSettings.Load,
            Platform.AppSettings.Save));

        return services.BuildServiceProvider();
    }

    private static LogLevel ToLogLevel(string level) => level switch
    {
        "Debug" => LogLevel.Debug,
        "Warning" => LogLevel.Warning,
        "Error" => LogLevel.Error,
        _ => LogLevel.Information,
    };
}

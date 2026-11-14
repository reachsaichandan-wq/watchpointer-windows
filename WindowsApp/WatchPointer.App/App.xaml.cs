using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WatchPointer.App.Services;
using WatchPointer.App.ViewModels;
using WatchPointer.Core.Session;
using WatchPointer.Core.Settings;

namespace WatchPointer.App;

public partial class App : Application
{
    private ServiceProvider? _services;
    private ReceiverHost? _host;
    private TrayIcon? _tray;
    private HotkeyService? _hotkeys;
    private LaserOverlay? _laser;
    private MainWindow? _window;
    private Mutex? _singleInstance;
    private bool _exiting;

    /// <summary>Runs <paramref name="action"/> on the UI thread (settings and engine events arrive on other threads).</summary>
    public static void OnUi(Action action)
    {
        var dispatcher = Current?.Dispatcher;
        if (dispatcher is null) return;
        if (dispatcher.CheckAccess()) action();
        else dispatcher.BeginInvoke(action);
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var renderIndex = Array.IndexOf(e.Args, "--render-pages");
        var renderDir = renderIndex >= 0 && renderIndex + 1 < e.Args.Length ? e.Args[renderIndex + 1] : null;
        _singleInstance = new Mutex(true, renderDir is null ? @"Local\WatchPointerForWindows" : $@"Local\WatchPointerRender{Environment.ProcessId}", out var first);
        if (!first)
        {
            MessageBox.Show("WatchPointer is already running (look for it in the notification area).", "WatchPointer");
            Shutdown();
            return;
        }

        var memoryLog = new MemoryLogProvider();
        var services = new ServiceCollection();
        services.AddLogging(b => b.ClearProviders().AddProvider(memoryLog).AddProvider(new FileLogProvider(FileLogProvider.DefaultDirectory)).SetMinimumLevel(LogLevel.Information));
        services.AddSingleton(memoryLog);
        services.AddSingleton<ISettingsStore>(sp => new JsonSettingsStore(JsonSettingsStore.DefaultPath, sp.GetRequiredService<ILogger<JsonSettingsStore>>()));
        services.AddSingleton<ReceiverHost>();
        services.AddSingleton<HotkeyConflicts>();
        services.AddSingleton<DashboardViewModel>();
        services.AddSingleton<PairingViewModel>();
        services.AddSingleton<PointerViewModel>();
        services.AddSingleton<CalibrationViewModel>();
        services.AddSingleton<GesturesViewModel>();
        services.AddSingleton<ProfilesViewModel>();
        services.AddSingleton<ShortcutsViewModel>();
        services.AddSingleton<DiagnosticsViewModel>();
        services.AddSingleton<AboutViewModel>();
        services.AddSingleton<MainViewModel>();
        _services = services.BuildServiceProvider();

        var log = _services.GetRequiredService<ILogger<App>>();
        DispatcherUnhandledException += (_, args) =>
        {
            log.LogError(args.Exception, "Unhandled UI exception");
            args.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) => log.LogCritical(args.ExceptionObject as Exception, "Unhandled exception");
        TaskScheduler.UnobservedTaskException += (_, args) => { log.LogError(args.Exception, "Unobserved task exception"); args.SetObserved(); };

        var settings = _services.GetRequiredService<ISettingsStore>();
        ApplyTheme(settings.Current.Ui.Theme);
        _host = _services.GetRequiredService<ReceiverHost>();
        log.LogInformation("WatchPointer for Windows {Version} starting on {Os}", AppInfo.Version, Environment.OSVersion);

        _laser = new LaserOverlay(_host.RealInput);
        _host.LaserChanged += on => OnUi(() => { if (on) _laser.Show(); else _laser.Hide(); });
        if (_host.Laser) _laser.Show();

        _hotkeys = new HotkeyService(_services.GetRequiredService<ILogger<HotkeyService>>());
        _services.GetRequiredService<HotkeyConflicts>().Source = () => _hotkeys.Conflicts;
        void ApplyHotkeys(AppSettings s) => _hotkeys.Apply(s.Hotkeys, _host.TogglePause, _host.Recenter, () => _host.Laser = !_host.Laser, _host.NextProfile);
        ApplyHotkeys(settings.Current);
        var lastHotkeys = settings.Current.Hotkeys;
        settings.Changed += s => OnUi(() =>
        {
            if (s.Hotkeys != lastHotkeys) { lastHotkeys = s.Hotkeys; ApplyHotkeys(s); }
            ApplyTheme(s.Ui.Theme);
        });

        var main = _services.GetRequiredService<MainViewModel>();
        _window = new MainWindow(main, settings);
        _window.Closing += (_, args) =>
        {
            if (_exiting || !settings.Current.Ui.MinimizeToTray) { args.Cancel = true; _ = ExitAsync(); return; }
            args.Cancel = true;
            _window.Hide();
        };
        _tray = new TrayIcon(ShowWindow, _host.TogglePause, _host.Recenter, () => _ = ExitAsync());
        _host.Engine.Notice += n =>
        {
            if (n.Notice is EngineNotice.Connected or EngineNotice.Disconnected)
                OnUi(() => _tray?.SetStatus($"WatchPointer: {(n.Notice == EngineNotice.Connected ? "watch connected" : "waiting for watch")}"));
        };

        if (renderDir is not null)
        {
            await PageRenderer.RenderAllAsync(_window, main, renderDir, e.Args.Contains("--light"));
            await ExitAsync();
            return;
        }
        if (!settings.Current.Ui.StartMinimized || e.Args.Contains("--show")) _window.Show();
        await _host.StartAsync();
    }

    private void ShowWindow()
    {
        if (_window is null) return;
        _window.Show();
        if (_window.WindowState == WindowState.Minimized) _window.WindowState = WindowState.Normal;
        _window.Activate();
    }

    public static void ApplyTheme(ThemePreference theme)
    {
        Current.ThemeMode = theme switch
        {
            ThemePreference.Light => ThemeMode.Light,
            ThemePreference.Dark => ThemeMode.Dark,
            _ => ThemeMode.System,
        };
    }

    private async Task ExitAsync()
    {
        if (_exiting) return;
        _exiting = true;
        try
        {
            _laser?.Hide();
            _tray?.Dispose();
            _hotkeys?.Dispose();
            // The container owns the receiver host (IAsyncDisposable) and the settings store (saves on dispose).
            if (_services is not null) await _services.DisposeAsync();
        }
        catch (Exception ex)
        {
            try { File.AppendAllText(Path.Combine(FileLogProvider.DefaultDirectory, "exit-error.log"), $"{DateTime.Now:O} {ex}\n"); } catch (IOException) { }
        }
        finally
        {
            try { _singleInstance?.ReleaseMutex(); } catch (ApplicationException) { /* not owned by this thread */ }
            Shutdown();
        }
    }
}

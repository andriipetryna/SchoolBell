using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using SchoolBell.Core;

namespace SchoolBell.App;

internal sealed class App : Application
{
    private readonly bool _background;
    private AppHost? _host;
    private MainWindow? _window;
    private TrayIcon? _tray;
    private EventWaitHandle? _showEvent;
    private RegisteredWaitHandle? _showWait;
    private int _uiErrors;
    private DateTime _uiErrorsWindowStart = DateTime.Now;
    private bool _exiting;

    public App(bool background)
    {
        _background = background;
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        DispatcherUnhandledException += OnDispatcherException;
        AppDomain.CurrentDomain.UnhandledException += OnFatalException;
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            ErrorLog.Write("Неперехоплена помилка задачі", e.Exception);
            e.SetObserved();
        };
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        CultureInfo.DefaultThreadCurrentUICulture = new CultureInfo("uk-UA");

        Native.PreventSleep();

        _host = new AppHost(Dispatcher);
        _window = new MainWindow(_host);
        _tray = new TrayIcon(_host, ShowWindow, RequestExit);

        _window.HiddenToTray += () =>
            _tray.ShowBalloon("Шкільний дзвінок працює",
                _window.HasUnsavedChanges
                    ? "Вікно приховано, але є незбережені зміни — відкрийте його та натисніть «Зберегти»."
                    : "Програма працює у треї. Клацніть іконку дзвіночка, щоб відкрити вікно.",
                _window.HasUnsavedChanges);

        _host.Tick += UpdateTooltip;

        // Другий запуск програми (ярлик) показує вже відкрите вікно.
        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, Program.ShowEventName);
        _showWait = ThreadPool.RegisterWaitForSingleObject(_showEvent,
            (_, _) => Dispatcher.InvokeAsync(ShowWindow), null, Timeout.Infinite, executeOnlyOnce: false);

        _host.Start();

        if (!_host.Settings.StartMinimized)
            _window.Show();
    }

    private void ShowWindow()
    {
        if (_exiting) return;
        _window?.ShowFromTray();
    }

    private void UpdateTooltip()
    {
        if (_host is null || _tray is null) return;
        var s = _host.Settings;
        string text;
        if (!s.Enabled)
        {
            text = "Шкільний дзвінок — розклад вимкнено";
        }
        else
        {
            var next = Schedule.Next(s, DateTime.Now);
            text = next is null
                ? "Шкільний дзвінок — немає запланованих подій"
                : $"Шкільний дзвінок\nНаступне: {next.Title}, {next.At:dd.MM HH:mm}";
        }
        if (_host.LastAlert is { State: AlertState.Active } alert)
            text += "\nДІЄ ПОВІТРЯНА ТРИВОГА" + alert.Info?.Level switch
            {
                AlertLevel.Red => " (червона)",
                AlertLevel.Yellow => " (жовта)",
                _ => ""
            };
        _tray.SetTooltip(text);
    }

    private void RequestExit()
    {
        var message = "Вийти з програми?\n\nПоки програма закрита, дзвінки, хвилина мовчання і сигнали тривоги НЕ лунатимуть.\n" +
                      "(Програма сама запуститься знову протягом 5 хвилин — так налаштовує інсталятор.)";
        if (_window?.HasUnsavedChanges == true)
            message += "\n\nУвага: є незбережені зміни в налаштуваннях — вони будуть втрачені.";

        var owner = _window is { IsVisible: true } ? _window : null;
        var answer = owner is null
            ? MessageBox.Show(message, "Шкільний дзвінок", MessageBoxButton.YesNo, MessageBoxImage.Warning)
            : MessageBox.Show(owner, message, "Шкільний дзвінок", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes) return;

        ExitApp(0);
    }

    private void ExitApp(int code)
    {
        if (_exiting) return;
        _exiting = true;
        try { _showWait?.Unregister(null); } catch { /* ignore */ }
        try { _showEvent?.Dispose(); } catch { /* ignore */ }
        try { _tray?.Dispose(); } catch { /* ignore */ }
        try { _host?.Dispose(); } catch { /* ignore */ }
        try { _window?.CloseForExit(); } catch { /* ignore */ }
        Shutdown(code);
    }

    private void OnDispatcherException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        ErrorLog.Write("Помилка в інтерфейсі", e.Exception);
        e.Handled = true; // одна помилка в інтерфейсі не повинна зупиняти дзвінки

        var now = DateTime.Now;
        if (now - _uiErrorsWindowStart > TimeSpan.FromMinutes(1))
        {
            _uiErrorsWindowStart = now;
            _uiErrors = 0;
        }
        if (++_uiErrors > 20)
        {
            // Програма в поганому стані — перезапускаємося з нуля.
            ErrorLog.Write("Забагато помилок за хвилину — перезапуск.");
            Program.RestartSelf();
            ExitApp(1);
        }
    }

    private void OnFatalException(object sender, UnhandledExceptionEventArgs e)
    {
        ErrorLog.Write("Критична помилка — програма буде перезапущена", e.ExceptionObject as Exception);
        if (e.IsTerminating)
        {
            try { _tray?.Dispose(); } catch { /* ignore */ }
            Program.RestartSelf();
        }
    }
}

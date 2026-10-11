using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using SchoolBell.Core;

namespace SchoolBell.App;

public static class Program
{
    private const string MutexName = @"Global\SchoolBell.SingleInstance";
    public const string ShowEventName = @"Local\SchoolBell.Show";

    /// <summary>
    /// Аргументи:
    ///   --background  запуск Планувальником: якщо програма вже працює, тихо вийти (не показувати вікно);
    ///   --restarted   перезапуск після збою: зачекати, поки старий процес звільнить місце.
    /// </summary>
    [STAThread]
    public static int Main(string[] args)
    {
        bool background = args.Any(a => string.Equals(a, "--background", StringComparison.OrdinalIgnoreCase));
        bool restarted = args.Any(a => string.Equals(a, "--restarted", StringComparison.OrdinalIgnoreCase));

        Mutex? mutex = null;
        bool owned = false;
        try
        {
            mutex = new Mutex(false, MutexName);
            try
            {
                owned = mutex.WaitOne(restarted ? TimeSpan.FromSeconds(20) : TimeSpan.Zero);
            }
            catch (AbandonedMutexException)
            {
                // Попередній екземпляр аварійно завершився — м'ютекс тепер наш.
                owned = true;
            }
        }
        catch (UnauthorizedAccessException)
        {
            // М'ютекс створено в сеансі іншого користувача — там програма вже працює.
            owned = false;
        }

        if (!owned)
        {
            if (!background) SignalRunningInstance();
            mutex?.Dispose();
            return 0;
        }

        try
        {
            var app = new App(background);
            return app.Run();
        }
        catch (Exception ex)
        {
            ErrorLog.Write("Фатальна помилка запуску", ex);
            return 1;
        }
        finally
        {
            try { mutex!.ReleaseMutex(); } catch { /* ignore */ }
            mutex!.Dispose();
        }
    }

    private static void SignalRunningInstance()
    {
        try
        {
            if (EventWaitHandle.TryOpenExisting(ShowEventName, out var ev))
            {
                using (ev) ev.Set();
                return;
            }
        }
        catch { /* ignore */ }

        System.Windows.MessageBox.Show(
            "Програма «Шкільний дзвінок» уже працює (можливо, в сеансі іншого користувача Windows).",
            "Шкільний дзвінок", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
    }

    /// <summary>Запускає нову копію програми (використовується після критичної помилки).</summary>
    public static void RestartSelf()
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe)) return;
            Process.Start(new ProcessStartInfo(exe, "--background --restarted") { UseShellExecute = false });
        }
        catch (Exception ex)
        {
            ErrorLog.Write("Не вдалося перезапустити програму", ex);
        }
    }
}

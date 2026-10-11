using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Threading;
using SchoolBell.Core;

namespace SchoolBell.App;

/// <summary>
/// Відтворення звуку з трьома рівнями захисту:
/// 1) WPF MediaPlayer (Media Foundation);
/// 2) якщо не вдалося — MCI (winmm);
/// 3) якщо файл відсутній або обидва способи впали — резервний сигнал Console.Beep.
/// Також є сторожовий таймер: «зависле» відтворення примусово завершується.
/// Усі методи викликаються з UI-потоку.
/// </summary>
internal sealed class AudioOutput : IAudioOutput
{
    private enum Mode { None, Media, Mci, Beep }

    private readonly MediaPlayer _media = new();
    private readonly DispatcherTimer _watch;
    private readonly Dispatcher _dispatcher;

    private Mode _mode = Mode.None;
    private int _generation;
    private bool _opened;
    private DateTime _openDeadline;
    private DateTime _deadline;
    private string? _path;
    private string _mciAlias = "";
    private SoundKind _kind;

    public event EventHandler? PlaybackEnded;

    /// <summary>Повідомлення про проблему з відтворенням (для банера в інтерфейсі).</summary>
    public event Action<string>? Problem;

    public AudioOutput(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
        _watch = new DispatcherTimer(DispatcherPriority.Normal, dispatcher) { Interval = TimeSpan.FromMilliseconds(500) };
        _watch.Tick += (_, _) => OnWatch();

        _media.MediaOpened += (_, _) =>
        {
            if (_mode != Mode.Media) return;
            _opened = true;
            var d = _media.NaturalDuration;
            _deadline = DateTime.Now + (d.HasTimeSpan ? d.TimeSpan + TimeSpan.FromSeconds(10) : TimeSpan.FromMinutes(20));
        };
        _media.MediaEnded += (_, _) =>
        {
            if (_mode == Mode.Media) Finish();
        };
        _media.MediaFailed += (_, e) =>
        {
            if (_mode == Mode.Media) FallbackToMci(e.ErrorException?.Message ?? "невідома помилка");
        };
    }

    public void Play(SoundKind kind, string? path)
    {
        StopInternal();
        _generation++;
        _kind = kind;

        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            Report($"Файл для «{SoundKindNames.Title(kind)}» не обрано або не знайдено — пролунав резервний сигнал. Оберіть файл на вкладці «Звуки».");
            StartBeep();
            return;
        }

        _path = path;
        _mode = Mode.Media;
        _opened = false;
        _openDeadline = DateTime.Now.AddSeconds(10);
        _deadline = DateTime.Now.AddMinutes(30);
        _watch.Start();

        try
        {
            _media.Open(new Uri(path, UriKind.Absolute));
            _media.Volume = 1.0;
            _media.Play();
        }
        catch (Exception ex)
        {
            FallbackToMci(ex.Message);
        }
    }

    public void Stop()
    {
        _generation++;
        StopInternal();
    }

    private void OnWatch()
    {
        try
        {
            var now = DateTime.Now;
            switch (_mode)
            {
                case Mode.Media:
                    if (!_opened && now > _openDeadline) FallbackToMci("файл не відкрився за 10 секунд");
                    else if (now > _deadline) Finish();
                    break;

                case Mode.Mci:
                    var code = Native.Mci($"status {_mciAlias} mode", out var status);
                    if (code != 0 || !status.StartsWith("play", StringComparison.OrdinalIgnoreCase) || now > _deadline)
                        Finish();
                    break;

                case Mode.Beep:
                    if (now > _deadline) Finish();
                    break;
            }
        }
        catch (Exception ex)
        {
            ErrorLog.Write("Сторожовий таймер відтворення", ex);
            Finish();
        }
    }

    private void FallbackToMci(string reason)
    {
        ErrorLog.Write($"MediaPlayer не зміг відтворити «{_path}»: {reason}. Пробую MCI.");
        StopMedia();

        _mode = Mode.Mci;
        _mciAlias = "schoolbell" + _generation;
        _deadline = DateTime.Now.AddMinutes(30);

        int code = Native.Mci($"open \"{_path}\" type mpegvideo alias {_mciAlias}", out _);
        if (code != 0)
            code = Native.Mci($"open \"{_path}\" alias {_mciAlias}", out _);
        if (code == 0)
            code = Native.Mci($"play {_mciAlias}", out _);

        if (code != 0)
        {
            var err = Native.MciError(code);
            ErrorLog.Write($"MCI теж не зміг відтворити «{_path}»: {err}");
            CloseMci();
            Report($"Не вдалося відтворити файл для «{SoundKindNames.Title(_kind)}» ({err}). Пролунав резервний сигнал. Спробуйте інший mp3.");
            StartBeep();
            return;
        }

        _watch.Start();
    }

    private void StartBeep()
    {
        _mode = Mode.Beep;
        _deadline = DateTime.Now.AddSeconds(30);
        _watch.Start();
        int gen = _generation;
        Task.Run(() =>
        {
            try
            {
                for (int i = 0; i < 6 && Volatile.Read(ref _generation) == gen; i++)
                {
                    Console.Beep(880, 350);
                    Thread.Sleep(150);
                }
            }
            catch
            {
                // На деяких ПК Beep недоступний — нічого не поробиш.
            }
        }).ContinueWith(_ => _dispatcher.InvokeAsync(() =>
        {
            if (gen == _generation && _mode == Mode.Beep) Finish();
        }));
    }

    private void Finish()
    {
        StopInternal();
        PlaybackEnded?.Invoke(this, EventArgs.Empty);
    }

    private void StopInternal()
    {
        _watch.Stop();
        switch (_mode)
        {
            case Mode.Media: StopMedia(); break;
            case Mode.Mci: CloseMci(); break;
        }
        _mode = Mode.None;
    }

    private void StopMedia()
    {
        try
        {
            _media.Stop();
            _media.Close();
        }
        catch (Exception ex)
        {
            ErrorLog.Write("Зупинка MediaPlayer", ex);
        }
    }

    private void CloseMci()
    {
        if (string.IsNullOrEmpty(_mciAlias)) return;
        Native.Mci($"stop {_mciAlias}", out _);
        Native.Mci($"close {_mciAlias}", out _);
        _mciAlias = "";
    }

    private void Report(string message)
    {
        ErrorLog.Write(message);
        Problem?.Invoke(message);
    }
}

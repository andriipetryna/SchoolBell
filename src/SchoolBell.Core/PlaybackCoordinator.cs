namespace SchoolBell.Core;

/// <summary>Абстракція програвача, щоб логіку черги можна було тестувати без Windows.</summary>
public interface IAudioOutput
{
    /// <summary>Почати відтворення. Після завершення (або помилки) викликати PlaybackEnded.</summary>
    void Play(SoundKind kind, string? path);
    void Stop();
    event EventHandler? PlaybackEnded;
}

/// <param name="Tag">Ідентифікатор події в журналі дня (для підсвічування в стрічці подій).</param>
public sealed record PlaybackRequest(SoundKind Sound, string Title, int Priority, DateTime ExpiresAt, bool Manual,
    string? PathOverride = null, string? Tag = null);

/// <summary>
/// Керує тим, що звучить зараз. Пріоритети: ручний запуск &gt; тривога/відбій &gt; хвилина мовчання &gt; дзвінок.
/// Звук з вищим пріоритетом перериває нижчий. Звук з нижчим або рівним пріоритетом стає в чергу
/// і відтворюється після поточного, якщо ще не «прострочений».
/// Усі методи викликаються з одного (UI) потоку.
/// </summary>
public sealed class PlaybackCoordinator
{
    public const int PriorityBell = 1;
    public const int PrioritySilence = 2;
    public const int PriorityAlert = 3;
    public const int PriorityManual = 10;

    private readonly IAudioOutput _output;
    private readonly Func<AppSettings> _settings;
    private readonly Func<DateTime> _clock;
    private readonly List<PlaybackRequest> _queue = new();

    public PlaybackRequest? Current { get; private set; }
    public IReadOnlyList<PlaybackRequest> Queue => _queue;

    /// <summary>Скільки чекати в черзі максимум, перш ніж звук буде пропущено.</summary>
    public TimeSpan MaxQueueDelay { get; init; } = TimeSpan.FromMinutes(3);

    public event EventHandler? StateChanged;

    private bool _stopping;

    public PlaybackCoordinator(IAudioOutput output, Func<AppSettings> settings, Func<DateTime> clock)
    {
        _output = output;
        _settings = settings;
        _clock = clock;
        _output.PlaybackEnded += (_, _) => { if (!_stopping) OnEnded(); };
    }

    public static int PriorityOf(SoundKind kind) => kind switch
    {
        SoundKind.AirAlert or SoundKind.AllClear => PriorityAlert,
        SoundKind.Silence => PrioritySilence,
        _ => PriorityBell
    };

    /// <summary>Запланований/автоматичний звук.</summary>
    public void RequestAuto(SoundKind kind, string title, string? tag = null)
    {
        var req = new PlaybackRequest(kind, title, PriorityOf(kind), _clock() + MaxQueueDelay, Manual: false, Tag: tag);
        Submit(req);
    }

    /// <summary>
    /// Ручний запуск кнопкою: завжди перериває поточний звук.
    /// pathOverride — для прослуховування ще не збереженого файлу.
    /// </summary>
    public void RequestManual(SoundKind kind, string? pathOverride = null, string? title = null, string? tag = null)
    {
        var req = new PlaybackRequest(kind, title ?? SoundKindNames.Title(kind) + " (вручну)", PriorityManual,
            DateTime.MaxValue, Manual: true, PathOverride: pathOverride, Tag: tag);
        Submit(req);
    }

    private void Submit(PlaybackRequest req)
    {
        if (Current is null)
        {
            Start(req);
        }
        else if (req.Priority > Current.Priority || req.Manual)
        {
            // Перериваємо поточний звук; перерваний не повертаємо в чергу.
            Current = null;
            StopOutput();
            Start(req);
        }
        else
        {
            _queue.Add(req);
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void StopAll()
    {
        _queue.Clear();
        if (Current is not null)
        {
            Current = null;
            StopOutput();
        }
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void StopOutput()
    {
        _stopping = true;
        try { _output.Stop(); }
        finally { _stopping = false; }
    }

    private void OnEnded()
    {
        Current = null;
        var now = _clock();
        _queue.RemoveAll(r => r.ExpiresAt < now);
        var next = _queue
            .OrderByDescending(r => r.Priority)
            .FirstOrDefault();
        if (next is not null)
        {
            _queue.Remove(next);
            Start(next);
        }
        else
        {
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void Start(PlaybackRequest req)
    {
        Current = req;
        StateChanged?.Invoke(this, EventArgs.Empty);
        var path = req.PathOverride ?? _settings().ResolveSound(req.Sound)?.StoredPath;
        _output.Play(req.Sound, path);
    }
}

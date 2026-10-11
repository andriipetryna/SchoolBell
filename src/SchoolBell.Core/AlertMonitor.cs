namespace SchoolBell.Core;

public sealed record AlertSnapshot(
    bool MonitorEnabled,
    AlertState State,
    AirAlertInfo? Info,
    DateTime? LastAttempt,
    DateTime? NextCheck,
    DateTime? LastSuccess,
    bool ConnectionLost,
    string? LastError,
    string? RegionName,
    string Source);

/// <summary>
/// Фонове опитування API тривог. Події піднімаються у фоновому потоці —
/// інтерфейс сам переносить їх у свій потік.
/// </summary>
public sealed class AlertMonitor : IDisposable
{
    private readonly HttpClient _http;
    private readonly Func<AppSettings> _settings;
    private readonly AlertStateTracker _tracker = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly SemaphoreSlim _wake = new(0);
    private string _trackedKey = "";
    private Task? _loop;

    /// <summary>Зміна стану тривоги (з поточними деталями тривоги, якщо вона діє).</summary>
    public event Action<AlertTransition, AirAlertInfo?>? Transition;

    /// <summary>Оновився стан (після кожного опитування).</summary>
    public event Action<AlertSnapshot>? Updated;

    public AlertMonitor(HttpClient http, Func<AppSettings> settings)
    {
        _http = http;
        _settings = settings;
    }

    public void Start() => _loop ??= Task.Run(() => RunAsync(_cts.Token));

    /// <summary>Перевірити негайно (напр. після зміни налаштувань).</summary>
    public void PollNow()
    {
        if (_wake.CurrentCount == 0) _wake.Release();
    }

    private async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var s = _settings();
            var alerts = s.Alerts;
            var delay = TimeSpan.FromSeconds(Math.Clamp(alerts.PollSeconds, 5, 300));

            try
            {
                var key = $"{alerts.MonitorEnabled}|{string.Join(",", alerts.RegionChain)}|{alerts.ApiKey}";
                if (key != _trackedKey)
                {
                    // Змінився регіон або джерело — починаємо спостереження заново, без «фальшивої» тривоги.
                    _trackedKey = key;
                    _tracker.Reset();
                }

                var client = new UkraineAlarmClient(_http, alerts.ApiKey);

                if (alerts.MonitorEnabled && alerts.RegionChain.Count > 0)
                {
                    try
                    {
                        var all = await client.GetAirAlertsAsync(ct).ConfigureAwait(false);
                        var status = AlertParsing.StatusFor(all, alerts.RegionChain);
                        var transition = _tracker.OnSuccess(status, DateTime.Now);
                        if (transition != AlertTransition.None)
                            Transition?.Invoke(transition, status);
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested)
                    {
                        return;
                    }
                    catch (Exception ex)
                    {
                        _tracker.OnFailure(ex.Message, DateTime.Now);
                        if (_tracker.ConsecutiveFailures == 2 || _tracker.ConsecutiveFailures % 40 == 0)
                            ErrorLog.Write($"Сервіс тривог недоступний ({client.SourceName}): {ex.Message}");
                    }
                }

                bool monitoring = alerts.MonitorEnabled && alerts.RegionChain.Count > 0;
                Updated?.Invoke(new AlertSnapshot(
                    monitoring,
                    _tracker.State,
                    _tracker.Info,
                    _tracker.LastAttempt,
                    monitoring ? DateTime.Now + delay : null,
                    _tracker.LastSuccess,
                    _tracker.ConnectionLost,
                    _tracker.LastError,
                    alerts.RegionName,
                    client.SourceName));
            }
            catch (Exception ex)
            {
                // Будь-яка непередбачена помилка не повинна зупинити цикл.
                ErrorLog.Write("Помилка циклу моніторингу тривоги", ex);
            }

            try
            {
                await _wake.WaitAsync(delay, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        try { _loop?.Wait(TimeSpan.FromSeconds(2)); } catch { /* ignore */ }
        _cts.Dispose();
        _wake.Dispose();
    }
}

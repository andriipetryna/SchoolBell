using System.Text;
using System.Text.Json;

namespace SchoolBell.Core;

public enum JournalKind
{
    /// <summary>Подія за розкладом відтворена.</summary>
    Scheduled,
    /// <summary>Хтось запустив звук кнопкою.</summary>
    Manual,
    AlertStarted,
    AlertEnded,
    AlertLevelChanged,
    /// <summary>Програма запустилась (або змінено регіон), а тривога вже триває.</summary>
    AlertOngoing,
    /// <summary>Головний вимикач розкладу.</summary>
    ScheduleToggled
}

public sealed class JournalEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public DateTime At { get; set; }
    public JournalKind Kind { get; set; }
    public SoundKind? Sound { get; set; }
    public string Title { get; set; } = "";
    public string? Detail { get; set; }

    /// <summary>Ключ події розкладу (для Scheduled).</summary>
    public string? ScheduleKey { get; set; }

    public AlertLevel? Level { get; set; }

    /// <summary>Новий стан вимикача (для ScheduleToggled).</summary>
    public bool? Enabled { get; set; }
}

/// <summary>
/// Журнал подій поточного дня. Зберігається у %ProgramData%\SchoolBell\journal\yyyy-MM-dd.json,
/// щоб після перезапуску програми стрічка подій не губилась. Файли старші за 14 днів видаляються.
/// Усі методи викликаються з UI-потоку.
/// </summary>
public sealed class DayJournal
{
    private const int KeepDays = 14;
    private List<JournalEntry> _entries = new();

    public DateOnly Day { get; private set; }
    public IReadOnlyList<JournalEntry> Entries => _entries;

    /// <summary>Збільшується при кожній зміні (для перемальовування інтерфейсу).</summary>
    public int Version { get; private set; }

    public event Action? Changed;

    public static string Dir => Path.Combine(AppPaths.DataDir, "journal");

    public DayJournal(DateTime now)
    {
        Load(DateOnly.FromDateTime(now));
        Cleanup(DateOnly.FromDateTime(now));
    }

    private static string FileFor(DateOnly day) => Path.Combine(Dir, $"{day:yyyy-MM-dd}.json");

    /// <summary>Переходить на новий день після півночі.</summary>
    public void EnsureDay(DateTime now)
    {
        var day = DateOnly.FromDateTime(now);
        if (day == Day) return;
        Load(day);
        Cleanup(day);
        Version++;
        Changed?.Invoke();
    }

    public JournalEntry Add(JournalEntry entry)
    {
        EnsureDay(entry.At);
        _entries.Add(entry);
        Save();
        Version++;
        Changed?.Invoke();
        return entry;
    }

    public bool HasScheduled(string key) =>
        _entries.Any(e => e.Kind == JournalKind.Scheduled && e.ScheduleKey == key);

    /// <summary>Остання запис про тривогу сьогодні (або null).</summary>
    public JournalEntry? LastAlertEntry() =>
        _entries.LastOrDefault(e => e.Kind is JournalKind.AlertStarted or JournalKind.AlertEnded
            or JournalKind.AlertLevelChanged or JournalKind.AlertOngoing);

    private void Load(DateOnly day)
    {
        Day = day;
        _entries = new();
        try
        {
            var file = FileFor(day);
            if (!File.Exists(file)) return;
            var list = JsonSerializer.Deserialize<List<JournalEntry>>(File.ReadAllText(file, Encoding.UTF8), SettingsStore.JsonOptions);
            if (list is not null) _entries = list.OrderBy(e => e.At).ToList();
        }
        catch (Exception ex)
        {
            ErrorLog.Write("Не вдалося прочитати журнал дня", ex);
        }
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            var file = FileFor(Day);
            var tmp = file + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(_entries, SettingsStore.JsonOptions), new UTF8Encoding(false));
            File.Move(tmp, file, overwrite: true);
        }
        catch (Exception ex)
        {
            ErrorLog.Write("Не вдалося записати журнал дня", ex);
        }
    }

    private static void Cleanup(DateOnly today)
    {
        try
        {
            if (!Directory.Exists(Dir)) return;
            foreach (var f in Directory.GetFiles(Dir, "*.json"))
            {
                if (DateOnly.TryParseExact(Path.GetFileNameWithoutExtension(f), "yyyy-MM-dd", out var d)
                    && d < today.AddDays(-KeepDays))
                {
                    try { File.Delete(f); } catch { /* ignore */ }
                }
            }
        }
        catch { /* ignore */ }
    }
}

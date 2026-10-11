namespace SchoolBell.Core;

public enum TimelineKind { Bell, Silence, Manual, Alert, AllClear, System, Now }

public enum TimelineState
{
    /// <summary>Відтворено за розкладом.</summary>
    Done,
    /// <summary>Час минув, а звук не лунав.</summary>
    Missed,
    /// <summary>Ще попереду.</summary>
    Upcoming,
    /// <summary>Найближча подія, що відтвориться.</summary>
    Next,
    /// <summary>Звучить зараз.</summary>
    Playing,
    /// <summary>Попереду, але не лунатиме (неактивний період або розклад вимкнено).</summary>
    Off,
    /// <summary>Запис журналу (ручний запуск, тривога, вимикач).</summary>
    Logged,
    /// <summary>Позначка поточного часу.</summary>
    Now
}

public sealed record TimelineItem(
    DateTime At,
    TimelineKind Kind,
    string Title,
    string? Detail,
    TimelineState State,
    AlertLevel? Level = null,
    string? Tag = null);

/// <summary>Складає стрічку подій дня: розклад + журнал + позначка «зараз».</summary>
public static class Timeline
{
    public static List<TimelineItem> Build(AppSettings s, DateOnly day, IReadOnlyList<JournalEntry> journal,
        DateTime now, string? playingTag)
    {
        var items = new List<TimelineItem>();
        var scheduledLog = journal
            .Where(j => j.Kind == JournalKind.Scheduled && j.ScheduleKey is not null)
            .GroupBy(j => j.ScheduleKey!)
            .ToDictionary(g => g.Key, g => g.First());

        bool nextAssigned = false;
        foreach (var e in Schedule.EventsOn(s, day))
        {
            var kind = e.Sound == SoundKind.Silence ? TimelineKind.Silence : TimelineKind.Bell;
            var key = e.Key;
            TimelineState state;
            string? detail;

            if (playingTag == key)
            {
                state = TimelineState.Playing;
                detail = "звучить зараз";
            }
            else if (scheduledLog.TryGetValue(key, out var played))
            {
                state = TimelineState.Done;
                detail = $"відтворено о {played.At:HH:mm:ss}";
            }
            else if (e.At <= now)
            {
                state = TimelineState.Missed;
                detail = WhyNotPlayed(s, journal, e.At) ?? "не відтворено";
            }
            else if (Schedule.FindInactivePeriod(s, e.At) is { } p)
            {
                state = TimelineState.Off;
                detail = $"не лунатиме: неактивний період «{p.Name}»";
            }
            else if (!s.Enabled)
            {
                state = TimelineState.Off;
                detail = "не лунатиме: розклад вимкнено";
            }
            else if (!nextAssigned)
            {
                state = TimelineState.Next;
                detail = "наступне";
                nextAssigned = true;
            }
            else
            {
                state = TimelineState.Upcoming;
                detail = null;
            }

            items.Add(new TimelineItem(e.At, kind, e.Title, detail, state, Tag: key));
        }

        foreach (var j in journal)
        {
            if (DateOnly.FromDateTime(j.At) != day) continue;
            var item = j.Kind switch
            {
                JournalKind.Scheduled => null, // уже показано рядком розкладу
                JournalKind.Manual => new TimelineItem(j.At, TimelineKind.Manual, j.Title, j.Detail ?? "запущено вручну",
                    playingTag == j.Id ? TimelineState.Playing : TimelineState.Logged, Tag: j.Id),
                JournalKind.AlertStarted or JournalKind.AlertOngoing or JournalKind.AlertLevelChanged =>
                    new TimelineItem(j.At, TimelineKind.Alert, j.Title, j.Detail,
                        playingTag == j.Id ? TimelineState.Playing : TimelineState.Logged, j.Level, j.Id),
                JournalKind.AlertEnded => new TimelineItem(j.At, TimelineKind.AllClear, j.Title, j.Detail,
                    playingTag == j.Id ? TimelineState.Playing : TimelineState.Logged, Tag: j.Id),
                JournalKind.ScheduleToggled => new TimelineItem(j.At, TimelineKind.System, j.Title, j.Detail,
                    TimelineState.Logged, Tag: j.Id),
                _ => null
            };
            if (item is not null) items.Add(item);
        }

        // Стабільне сортування: при однаковому часі порядок розкладу зберігається.
        var sorted = items.OrderBy(i => i.At).ToList();

        if (DateOnly.FromDateTime(now) == day)
        {
            int idx = sorted.FindIndex(i => i.At > now);
            if (idx < 0) idx = sorted.Count;
            sorted.Insert(idx, new TimelineItem(now, TimelineKind.Now, "Зараз", null, TimelineState.Now));
        }

        return sorted;
    }

    /// <summary>Пояснення, чому подія розкладу в минулому не лунала (або null, якщо невідомо).</summary>
    private static string? WhyNotPlayed(AppSettings s, IReadOnlyList<JournalEntry> journal, DateTime at)
    {
        if (Schedule.FindInactivePeriod(s, at) is { } p)
            return $"не лунав: неактивний період «{p.Name}»";
        if (!EnabledAt(s, journal, at))
            return "не лунав: розклад було вимкнено";
        return "не відтворено (програма не працювала?)";
    }

    /// <summary>Стан головного вимикача в момент t за записами журналу.</summary>
    private static bool EnabledAt(AppSettings s, IReadOnlyList<JournalEntry> journal, DateTime t)
    {
        var toggles = journal.Where(j => j.Kind == JournalKind.ScheduleToggled && j.Enabled is not null).ToList();
        var before = toggles.LastOrDefault(j => j.At <= t);
        if (before is not null) return before.Enabled!.Value;
        var after = toggles.FirstOrDefault(j => j.At > t);
        if (after is not null) return !after.Enabled!.Value;
        return s.Enabled;
    }
}

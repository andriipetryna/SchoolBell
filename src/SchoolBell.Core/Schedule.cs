namespace SchoolBell.Core;

public sealed record ScheduledEvent(DateTime At, SoundKind Sound, string Title)
{
    /// <summary>Унікальний ключ події, щоб не відтворити її двічі.</summary>
    public string Key => $"{At:yyyyMMddHHmm}:{Sound}";
}

public static class Schedule
{
    /// <summary>Усі заплановані події на дату з урахуванням днів тижня (без урахування неактивності й вимикача).</summary>
    public static List<ScheduledEvent> EventsOn(AppSettings s, DateOnly date)
    {
        var list = new List<ScheduledEvent>();
        var dow = date.DayOfWeek;

        if (s.BellDays.Contains(dow))
        {
            for (int i = 0; i < s.Lessons.Count; i++)
            {
                var l = s.Lessons[i];
                list.Add(new(date.ToDateTime(l.Start), SoundKind.BellStart, $"Дзвінок на {i + 1}-й урок"));
                list.Add(new(date.ToDateTime(l.End), SoundKind.BellEnd, $"Дзвінок з {i + 1}-го уроку"));
            }
        }

        if (s.SilenceEnabled && s.SilenceDays.Contains(dow))
            list.Add(new(date.ToDateTime(s.SilenceTime), SoundKind.Silence, "Хвилина мовчання"));

        // Хвилина мовчання має пріоритет: при однаковому часі вона йде першою.
        return list
            .OrderBy(e => e.At)
            .ThenBy(e => e.Sound == SoundKind.Silence ? 0 : 1)
            .ToList();
    }

    public static bool IsInactive(AppSettings s, DateTime t) => FindInactivePeriod(s, t) is not null;

    /// <summary>Період неактивності, що діє в момент t (або null).</summary>
    public static InactivityPeriod? FindInactivePeriod(AppSettings s, DateTime t)
    {
        var day = t.DayOfWeek;
        var prevDay = t.AddDays(-1).DayOfWeek;
        var time = TimeOnly.FromDateTime(t);

        foreach (var p in s.InactivityPeriods)
        {
            if (p.IsWholeDay)
            {
                if (p.Days.Contains(day)) return p;
            }
            else if (p.From < p.To)
            {
                if (p.Days.Contains(day) && time >= p.From && time < p.To) return p;
            }
            else // через північ: напр. 16:00–07:30
            {
                if (p.Days.Contains(day) && time >= p.From) return p;
                if (p.Days.Contains(prevDay) && time < p.To) return p;
            }
        }
        return null;
    }

    /// <summary>Чи дозволено автоматичне відтворення в момент t (вимикач + неактивність).</summary>
    public static bool IsAutoAllowed(AppSettings s, DateTime t) => s.Enabled && !IsInactive(s, t);

    /// <summary>Найближча подія після now, яка реально відтвориться.</summary>
    public static ScheduledEvent? Next(AppSettings s, DateTime now, int daysAhead = 14)
    {
        if (!s.Enabled) return null;
        var start = DateOnly.FromDateTime(now);
        for (int d = 0; d <= daysAhead; d++)
        {
            foreach (var e in EventsOn(s, start.AddDays(d)))
            {
                if (e.At > now && !IsInactive(s, e.At))
                    return e;
            }
        }
        return null;
    }
}

/// <summary>
/// Визначає, які події «настали» між двома опитуваннями. Стійкий до сну ПК, зміни часу
/// та перезапуску програми: подія відтворюється, якщо від її часу минуло не більше Grace,
/// і жодна подія не відтворюється двічі.
/// </summary>
public sealed class DueEventTracker
{
    private readonly HashSet<string> _fired = new();
    private DateTime? _lastPoll;

    public TimeSpan Grace { get; init; } = TimeSpan.FromSeconds(90);

    public List<ScheduledEvent> Poll(AppSettings s, DateTime now)
    {
        var windowStart = now - Grace;
        if (_lastPoll is { } last && last > windowStart && last <= now)
            windowStart = last;
        _lastPoll = now;

        var result = new List<ScheduledEvent>();
        var firstDay = DateOnly.FromDateTime(windowStart);
        var lastDay = DateOnly.FromDateTime(now);

        for (var day = firstDay; day <= lastDay; day = day.AddDays(1))
        {
            foreach (var e in Schedule.EventsOn(s, day))
            {
                if (e.At <= windowStart || e.At > now) continue;
                if (!_fired.Add(e.Key)) continue;
                if (!Schedule.IsAutoAllowed(s, e.At)) continue;
                result.Add(e);
            }
        }

        // Прибираємо старі ключі, щоб набір не ріс безкінечно.
        if (_fired.Count > 500)
        {
            var cutoff = now.AddDays(-2).ToString("yyyyMMddHHmm");
            _fired.RemoveWhere(k => string.CompareOrdinal(k, 0, cutoff, 0, 12) < 0);
        }

        return result;
    }
}

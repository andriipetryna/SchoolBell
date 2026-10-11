using System.Net.Http.Headers;
using System.Text.Json;

namespace SchoolBell.Core;

public sealed class RegionNode
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Type { get; init; } = "";
    public List<RegionNode> Children { get; init; } = new();

    public override string ToString() => Name;
}

public static class RegionTree
{
    /// <summary>Шлях від області до регіону з вказаним ID (порожній, якщо не знайдено).</summary>
    public static List<RegionNode> FindPath(IEnumerable<RegionNode> roots, string id)
    {
        foreach (var root in roots)
        {
            var path = new List<RegionNode>();
            if (Search(root, id, path)) return path;
        }
        return new();
    }

    private static bool Search(RegionNode node, string id, List<RegionNode> path)
    {
        path.Add(node);
        if (node.Id == id) return true;
        foreach (var c in node.Children)
            if (Search(c, id, path)) return true;
        path.RemoveAt(path.Count - 1);
        return false;
    }
}

/// <summary>Розбір відповідей UkraineAlarm API (v3).</summary>
public static class AlertParsing
{
    public const string AirAlertType = "AIR";

    /// <summary>GET /regions → {"states":[{regionId, regionName, regionType, regionChildIds:[...]}]}</summary>
    public static List<RegionNode> ParseRegions(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        JsonElement states;
        if (root.ValueKind == JsonValueKind.Object && TryGet(root, "states", out states)) { }
        else if (root.ValueKind == JsonValueKind.Array) states = root;
        else return new();

        return states.EnumerateArray().Select(ParseRegion).Where(r => r.Id != "").ToList();
    }

    private static RegionNode ParseRegion(JsonElement e)
    {
        var children = new List<RegionNode>();
        if (TryGet(e, "regionChildIds", out var ch) && ch.ValueKind == JsonValueKind.Array)
            children.AddRange(ch.EnumerateArray()
                .Where(x => x.ValueKind == JsonValueKind.Object)
                .Select(ParseRegion)
                .Where(r => r.Id != ""));

        return new RegionNode
        {
            Id = GetString(e, "regionId"),
            Name = GetString(e, "regionName"),
            Type = GetString(e, "regionType"),
            Children = children.OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase).ToList()
        };
    }

    /// <summary>
    /// GET /alerts → [{regionId, regionName, activeAlerts:[{regionId, type, lastUpdate,
    ///                 activeAlertLevels:[{alertLevel:"Red"|"Yellow", reason, createdAt}]}]}].
    /// Повертає регіони, де зараз діє повітряна тривога, з рівнем загрози.
    /// </summary>
    public static Dictionary<string, AirAlertInfo> ParseAirAlerts(string json)
    {
        var result = new Dictionary<string, AirAlertInfo>();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Array) return result;

        foreach (var region in root.EnumerateArray())
        {
            if (region.ValueKind != JsonValueKind.Object) continue;
            var regionId = GetString(region, "regionId");
            if (!TryGet(region, "activeAlerts", out var alerts) || alerts.ValueKind != JsonValueKind.Array) continue;

            foreach (var a in alerts.EnumerateArray())
            {
                if (!string.Equals(GetString(a, "type"), AirAlertType, StringComparison.OrdinalIgnoreCase)) continue;
                // Беремо regionId самої тривоги: запис області може містити тривоги її громад,
                // і тоді вважати тривожною всю область було б помилкою.
                var own = GetString(a, "regionId");
                var id = own != "" ? own : regionId;
                if (id == "") continue;

                var info = ParseLevel(a);
                result[id] = result.TryGetValue(id, out var existing) ? AirAlertInfo.Max(existing, info) : info;
            }
        }
        return result;
    }

    private static AirAlertInfo ParseLevel(JsonElement alert)
    {
        var best = new AirAlertInfo(AlertLevel.Unspecified, null);
        if (!TryGet(alert, "activeAlertLevels", out var levels) || levels.ValueKind != JsonValueKind.Array)
            return best;

        foreach (var l in levels.EnumerateArray())
        {
            var level = GetString(l, "alertLevel").ToLowerInvariant() switch
            {
                "red" => AlertLevel.Red,
                "yellow" => AlertLevel.Yellow,
                _ => AlertLevel.Unspecified
            };
            var reason = GetString(l, "reason");
            var candidate = new AirAlertInfo(level, reason == "" ? null : reason);
            best = best.Level == AlertLevel.Unspecified && best.Reason is null
                ? candidate
                : AirAlertInfo.Max(best, candidate);
        }
        return best;
    }

    /// <summary>
    /// Тривога для обраного регіону з урахуванням «батьківських» (район, область).
    /// Повертає null, якщо тривоги немає. Якщо діє кілька — береться найсерйозніша.
    /// </summary>
    public static AirAlertInfo? StatusFor(Dictionary<string, AirAlertInfo> alerts, IEnumerable<string> regionChain)
    {
        AirAlertInfo? result = null;
        foreach (var id in regionChain)
            if (alerts.TryGetValue(id, out var info))
                result = result is null ? info : AirAlertInfo.Max(result, info);
        return result;
    }

    private static bool TryGet(JsonElement e, string name, out JsonElement value)
    {
        if (e.ValueKind == JsonValueKind.Object)
        {
            foreach (var p in e.EnumerateObject())
            {
                if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    value = p.Value;
                    return true;
                }
            }
        }
        value = default;
        return false;
    }

    private static string GetString(JsonElement e, string name)
    {
        if (!TryGet(e, name, out var v)) return "";
        return v.ValueKind switch
        {
            JsonValueKind.String => v.GetString() ?? "",
            JsonValueKind.Number => v.GetRawText(),
            _ => ""
        };
    }
}

public sealed class UkraineAlarmClient
{
    public const string OfficialBase = "https://api.ukrainealarm.com/api/v3/";

    /// <summary>Публічна обгортка над офіційним API без ключа (ліміт 50 запитів/хв з однієї IP).</summary>
    public const string PublicMirrorBase = "https://siren.pp.ua/api/v3/";

    private readonly HttpClient _http;
    private readonly string? _apiKey;

    public string BaseUrl { get; }

    public UkraineAlarmClient(HttpClient http, string? apiKey)
    {
        _http = http;
        _apiKey = string.IsNullOrWhiteSpace(apiKey) ? null : apiKey.Trim();
        BaseUrl = _apiKey is null ? PublicMirrorBase : OfficialBase;
    }

    public string SourceName => _apiKey is null ? "siren.pp.ua (без ключа)" : "api.ukrainealarm.com";

    private async Task<string> GetAsync(string path, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, BaseUrl + path);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (_apiKey is not null) req.Headers.TryAddWithoutValidation("Authorization", _apiKey);
        using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
        if ((int)resp.StatusCode == 401)
            throw new HttpRequestException("Ключ API не прийнято (401). Перевірте ключ.");
        if ((int)resp.StatusCode == 429)
            throw new HttpRequestException("Забагато запитів до сервісу (429). Збільште інтервал перевірки.");
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Сирий JSON списку регіонів (зручно кешувати на диску).</summary>
    public Task<string> GetRegionsJsonAsync(CancellationToken ct = default) => GetAsync("regions", ct);

    public async Task<List<RegionNode>> GetRegionsAsync(CancellationToken ct = default)
        => AlertParsing.ParseRegions(await GetRegionsJsonAsync(ct).ConfigureAwait(false));

    public async Task<Dictionary<string, AirAlertInfo>> GetAirAlertsAsync(CancellationToken ct = default)
        => AlertParsing.ParseAirAlerts(await GetAsync("alerts", ct).ConfigureAwait(false));
}

/// <summary>Рівень загрози повітряної тривоги (поле alertLevel API).</summary>
public enum AlertLevel
{
    /// <summary>API не вказав рівень — звичайна повітряна тривога.</summary>
    Unspecified,
    Yellow,
    Red
}

public sealed record AirAlertInfo(AlertLevel Level, string? Reason)
{
    /// <summary>Найсерйозніша з двох: червона &gt; без рівня &gt; жовта.</summary>
    public static AirAlertInfo Max(AirAlertInfo a, AirAlertInfo b)
    {
        static int Rank(AlertLevel l) => l switch { AlertLevel.Red => 3, AlertLevel.Unspecified => 2, _ => 1 };
        if (Rank(b.Level) > Rank(a.Level)) return b;
        if (Rank(b.Level) == Rank(a.Level) && a.Reason is null) return b;
        return a;
    }

    public string LevelTitle => Level switch
    {
        AlertLevel.Red => "червоний рівень",
        AlertLevel.Yellow => "жовтий рівень",
        _ => ""
    };
}

public enum AlertState { Unknown, Clear, Active }

public enum AlertTransition
{
    None,
    /// <summary>Тривога почалась (була відсутня).</summary>
    Started,
    /// <summary>Відбій.</summary>
    Ended,
    /// <summary>Під час тривоги змінився рівень (жовтий ↔ червоний).</summary>
    LevelChanged,
    /// <summary>Перша перевірка після запуску застала тривогу — сигнал не відтворюється.</summary>
    AlreadyActive
}

/// <summary>
/// Відстежує стан тривоги за результатами опитувань.
/// Перше спостереження після запуску не вважається зміною — щоб після перезавантаження ПК
/// посеред тривоги сирена не пролунала повторно.
/// </summary>
public sealed class AlertStateTracker
{
    public AlertState State { get; private set; } = AlertState.Unknown;

    /// <summary>Деталі тривоги (рівень, причина), якщо вона діє.</summary>
    public AirAlertInfo? Info { get; private set; }

    public DateTime? LastSuccess { get; private set; }

    /// <summary>Час останньої спроби перевірки (успішної чи ні).</summary>
    public DateTime? LastAttempt { get; private set; }

    public int ConsecutiveFailures { get; private set; }
    public string? LastError { get; private set; }

    /// <summary>Немає зв'язку щонайменше два опитування поспіль.</summary>
    public bool ConnectionLost => ConsecutiveFailures >= 2;

    /// <summary>Зміна рівня (жовта ↔ червона) під час тривоги не вважається новою тривогою.</summary>
    public AlertTransition OnSuccess(AirAlertInfo? alert, DateTime now)
    {
        ConsecutiveFailures = 0;
        LastError = null;
        LastSuccess = now;
        LastAttempt = now;
        var oldLevel = Info?.Level;
        Info = alert;
        var newState = alert is not null ? AlertState.Active : AlertState.Clear;
        var old = State;
        State = newState;

        return (old, newState) switch
        {
            (AlertState.Unknown, AlertState.Active) => AlertTransition.AlreadyActive,
            (AlertState.Clear, AlertState.Active) => AlertTransition.Started,
            (AlertState.Active, AlertState.Clear) => AlertTransition.Ended,
            (AlertState.Active, AlertState.Active) when oldLevel != alert!.Level => AlertTransition.LevelChanged,
            _ => AlertTransition.None
        };
    }

    public void OnFailure(string error, DateTime now)
    {
        LastAttempt = now;
        ConsecutiveFailures++;
        LastError = error;
        // Стан не скидаємо: після відновлення зв'язку зміну буде виявлено коректно.
    }

    public void Reset()
    {
        State = AlertState.Unknown;
        Info = null;
        LastAttempt = null;
        ConsecutiveFailures = 0;
        LastError = null;
        LastSuccess = null;
    }
}

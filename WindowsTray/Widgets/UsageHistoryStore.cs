using System.IO;
using System.Text.Json;

namespace CodexBarTray;

/// <summary>One recorded utilization sample.</summary>
public sealed class HistorySample
{
    public DateTimeOffset CapturedAt { get; set; }
    public double UsedPercent { get; set; }
}

/// <summary>A normalized chart point: X in 0..1 across the time domain, Y in 0..100.</summary>
public readonly record struct HistPoint(double X, double Y);

/// <summary>
/// Tray-side sampled usage history, the lean equivalent of the macOS
/// PlanUtilizationHistoryStore. The tray is the long-running process (it owns the
/// serve engine), so it records a session (primary) and weekly (secondary) sample
/// per provider on every /usage refresh and prunes each series to its window
/// length. Persisted to <c>%LocalAppData%\CodexBar\history.json</c>.
/// </summary>
public sealed class UsageHistoryStore
{
    private const int MinSampleIntervalSeconds = 45; // dedupe add-triggered double refreshes
    private const int MaxPerSeries = 2000;           // hard cap to bound file size
    private const int DefaultSessionMinutes = 300;   // 5h
    private const int DefaultWeeklyMinutes = 7 * 24 * 60;

    // providerId -> "session"/"weekly" -> samples (oldest first)
    public Dictionary<string, Dictionary<string, List<HistorySample>>> Providers { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    private static string FilePath
    {
        get
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "CodexBar");
            return Path.Combine(dir, "history.json");
        }
    }

    public static UsageHistoryStore Load()
    {
        try
        {
            var path = FilePath;
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                var store = JsonSerializer.Deserialize<UsageHistoryStore>(json);
                if (store is not null)
                {
                    store.Providers = new Dictionary<string, Dictionary<string, List<HistorySample>>>(
                        store.Providers, StringComparer.OrdinalIgnoreCase);
                    return store;
                }
            }
        }
        catch { /* fall back to empty on any read/parse error */ }
        return new UsageHistoryStore();
    }

    public void Save()
    {
        try
        {
            var path = FilePath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var json = JsonSerializer.Serialize(this);
            File.WriteAllText(path, json);
        }
        catch { /* best-effort; history is non-critical */ }
    }

    /// <summary>Append samples for every provider's session/weekly window, prune, persist.</summary>
    public void Record(IReadOnlyList<ProviderResult> results, DateTimeOffset now)
    {
        var changed = false;
        foreach (var result in results)
        {
            if (result.Error is not null || string.IsNullOrEmpty(result.Provider)) continue;
            changed |= RecordWindow(result.Provider, "session", result.Usage?.Primary, now, DefaultSessionMinutes);
            changed |= RecordWindow(result.Provider, "weekly", result.Usage?.Secondary, now, DefaultWeeklyMinutes);
        }
        if (changed) Save();
    }

    public IReadOnlyList<HistorySample> Series(string providerId, QuotaWindowKind window)
    {
        var key = window == QuotaWindowKind.Session ? "session" : "weekly";
        if (Providers.TryGetValue(providerId, out var byWindow) && byWindow.TryGetValue(key, out var samples))
            return samples;
        return Array.Empty<HistorySample>();
    }

    private bool RecordWindow(string providerId, string key, RateWindow? window, DateTimeOffset now, int defaultMinutes)
    {
        if (window is null) return false;

        if (!Providers.TryGetValue(providerId, out var byWindow))
        {
            byWindow = new Dictionary<string, List<HistorySample>>(StringComparer.OrdinalIgnoreCase);
            Providers[providerId] = byWindow;
        }
        if (!byWindow.TryGetValue(key, out var series))
        {
            series = new List<HistorySample>();
            byWindow[key] = series;
        }

        var changed = false;
        if (series.Count == 0 || (now - series[^1].CapturedAt).TotalSeconds >= MinSampleIntervalSeconds)
        {
            series.Add(new HistorySample { CapturedAt = now, UsedPercent = Math.Clamp(window.UsedPercent, 0, 100) });
            changed = true;
        }

        // Prune to the window length plus a hard count cap.
        var windowMinutes = window.WindowMinutes is > 0 ? window.WindowMinutes!.Value : defaultMinutes;
        var cutoff = now.AddMinutes(-windowMinutes);
        var removed = series.RemoveAll(s => s.CapturedAt < cutoff);
        if (removed > 0) changed = true;
        if (series.Count > MaxPerSeries)
        {
            series.RemoveRange(0, series.Count - MaxPerSeries);
            changed = true;
        }

        return changed;
    }
}

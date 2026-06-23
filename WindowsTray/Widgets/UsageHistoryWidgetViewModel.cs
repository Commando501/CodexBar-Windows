namespace CodexBarTray;

/// <summary>
/// Content for a Usage-History widget: a line chart of a provider's sampled
/// session or weekly utilization over time, read from <see cref="UsageHistoryStore"/>.
/// </summary>
public sealed class UsageHistoryWidgetViewModel : WidgetContentViewModel
{
    private const int MaxRenderPoints = 80;

    private readonly QuotaWindowKind _window;
    private List<HistPoint> _points = new();
    private double _current;
    private double _peak;
    private int _count;

    public UsageHistoryWidgetViewModel(string providerId, string fallbackName, QuotaWindowKind window)
        : base(providerId, fallbackName)
    {
        _window = window;
    }

    public override string Title =>
        $"{FallbackName} · {(_window == QuotaWindowKind.Session ? "session" : "weekly")} history";

    public IReadOnlyList<HistPoint> Points => _points;
    public bool HasChart => _points.Count >= 2;
    public bool NoChart => !HasChart;
    public string EmptyText => _count == 0 ? "Collecting history…" : "Collecting history… (1 sample)";
    public string StatusText => $"now {Math.Round(_current)}% · peak {Math.Round(_peak)}%";

    public override void Update(WidgetData data)
    {
        var samples = data.History.Series(ProviderId, _window);
        _count = samples.Count;
        _points = BuildPoints(samples);
        _current = samples.Count > 0 ? samples[^1].UsedPercent : 0;
        _peak = samples.Count > 0 ? samples.Max(s => s.UsedPercent) : 0;
        RaiseAll();
    }

    private static List<HistPoint> BuildPoints(IReadOnlyList<HistorySample> samples)
    {
        if (samples.Count < 2) return new List<HistPoint>();

        var start = samples[0].CapturedAt;
        var end = samples[^1].CapturedAt;
        var span = (end - start).TotalSeconds;
        if (span <= 0) return new List<HistPoint>();

        var reduced = Downsample(samples, MaxRenderPoints);
        var points = new List<HistPoint>(reduced.Count);
        foreach (var sample in reduced)
        {
            var x = (sample.CapturedAt - start).TotalSeconds / span;
            points.Add(new HistPoint(Math.Clamp(x, 0, 1), Math.Clamp(sample.UsedPercent, 0, 100)));
        }
        return points;
    }

    private static List<HistorySample> Downsample(IReadOnlyList<HistorySample> samples, int max)
    {
        if (samples.Count <= max) return samples.ToList();

        // Keep endpoints; pick the rest at an even stride.
        var result = new List<HistorySample>(max);
        var step = (double)(samples.Count - 1) / (max - 1);
        for (var i = 0; i < max; i++)
            result.Add(samples[(int)Math.Round(i * step)]);
        return result;
    }
}

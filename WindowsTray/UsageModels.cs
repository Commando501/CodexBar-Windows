using System.Text.Json;
using System.Text.Json.Serialization;

namespace CodexBarTray;

// DTOs matching the `codexbar serve` /usage JSON (an array of provider results).
// Only the fields the tray currently renders are modeled; unknown fields are ignored.

public sealed class ProviderResult
{
    [JsonPropertyName("provider")] public string Provider { get; set; } = "";
    [JsonPropertyName("account")] public string? Account { get; set; }
    [JsonPropertyName("source")] public string? Source { get; set; }
    [JsonPropertyName("usage")] public UsageData? Usage { get; set; }
    [JsonPropertyName("error")] public ProviderError? Error { get; set; }
}

public sealed class ProviderError
{
    [JsonPropertyName("message")] public string Message { get; set; } = "";
    [JsonPropertyName("kind")] public string? Kind { get; set; }
}

public sealed class UsageData
{
    [JsonPropertyName("primary")] public RateWindow? Primary { get; set; }
    [JsonPropertyName("secondary")] public RateWindow? Secondary { get; set; }
    [JsonPropertyName("tertiary")] public RateWindow? Tertiary { get; set; }
    [JsonPropertyName("extraRateWindows")] public List<NamedRateWindow>? ExtraRateWindows { get; set; }
    [JsonPropertyName("providerCost")] public ProviderCost? ProviderCost { get; set; }
    [JsonPropertyName("updatedAt")] public DateTimeOffset? UpdatedAt { get; set; }
    [JsonPropertyName("identity")] public Identity? Identity { get; set; }
}

/// <summary>Spend/budget snapshot (e.g. Cursor On-Demand usage beyond the included plan).</summary>
public sealed class ProviderCost
{
    [JsonPropertyName("used")] public double Used { get; set; }
    [JsonPropertyName("limit")] public double Limit { get; set; }
    [JsonPropertyName("currencyCode")] public string? CurrencyCode { get; set; }
    [JsonPropertyName("period")] public string? Period { get; set; }
    [JsonPropertyName("resetsAt")] public DateTimeOffset? ResetsAt { get; set; }
    [JsonPropertyName("personalUsed")] public double? PersonalUsed { get; set; }
}

public sealed class Identity
{
    [JsonPropertyName("accountEmail")] public string? AccountEmail { get; set; }
    [JsonPropertyName("loginMethod")] public string? LoginMethod { get; set; }
}

public sealed class RateWindow
{
    [JsonPropertyName("usedPercent")] public double UsedPercent { get; set; }
    [JsonPropertyName("windowMinutes")] public int? WindowMinutes { get; set; }
    [JsonPropertyName("resetsAt")] public DateTimeOffset? ResetsAt { get; set; }
    [JsonPropertyName("resetDescription")] public string? ResetDescription { get; set; }
}

public sealed class NamedRateWindow
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("title")] public string? Title { get; set; }
    [JsonPropertyName("window")] public RateWindow? Window { get; set; }
}

public static class UsageJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public static List<ProviderResult> Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new List<ProviderResult>();
        return JsonSerializer.Deserialize<List<ProviderResult>>(json, Options)
            ?? new List<ProviderResult>();
    }
}

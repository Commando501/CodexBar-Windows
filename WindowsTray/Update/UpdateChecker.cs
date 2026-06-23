using System.Net.Http;
using System.Reflection;
using System.Text.Json;

namespace CodexBarTray;

/// <summary>A newer release found on GitHub.</summary>
public sealed record UpdateInfo(Version Version, string TagName, string ReleaseUrl, string? AssetUrl);

/// <summary>The tray's own version, taken from the assembly (set via the csproj &lt;Version&gt;).</summary>
public static class AppVersion
{
    public static Version Current { get; } = Resolve();

    public static string DisplayString => Current.ToString(3);

    private static Version Resolve()
    {
        var v = Assembly.GetEntryAssembly()?.GetName().Version;
        // Normalize to Major.Minor.Build; the assembly revision is noise here.
        return v is null ? new Version(0, 0, 0) : new Version(v.Major, v.Minor, Math.Max(0, v.Build));
    }
}

/// <summary>
/// Polls the project's GitHub Releases for a newer version. This is the "check"
/// half of an updater — it never replaces files; the app surfaces the result and
/// links the user to the download (the Windows build ships as a portable zip).
/// </summary>
public sealed class UpdateChecker : IDisposable
{
    private const string Owner = "Commando501";
    private const string Repo = "CodexBar-Windows";
    private static readonly Uri LatestReleaseUrl =
        new($"https://api.github.com/repos/{Owner}/{Repo}/releases/latest");

    private readonly HttpClient _http;

    public UpdateChecker()
    {
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        // GitHub's API requires a User-Agent; the JSON Accept header is recommended.
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("CodexBarTray");
        _http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
    }

    /// <summary>Returns update info when the latest release is newer than <paramref name="current"/>, else null.</summary>
    public async Task<UpdateInfo?> CheckAsync(Version current, CancellationToken ct = default)
    {
        var json = await _http.GetStringAsync(LatestReleaseUrl, ct).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var tag = root.TryGetProperty("tag_name", out var t) ? t.GetString() : null;
        if (string.IsNullOrWhiteSpace(tag)) return null;

        var latest = ParseVersion(tag);
        if (latest is null || latest <= current) return null;

        var releaseUrl = root.TryGetProperty("html_url", out var h) ? h.GetString() ?? "" : "";
        var assetUrl = FindZipAsset(root);
        return new UpdateInfo(latest, tag!, releaseUrl, assetUrl);
    }

    private static string? FindZipAsset(JsonElement root)
    {
        if (!root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
            return null;
        foreach (var asset in assets.EnumerateArray())
        {
            var name = asset.TryGetProperty("name", out var n) ? n.GetString() : null;
            if (name is not null && name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                return asset.TryGetProperty("browser_download_url", out var d) ? d.GetString() : null;
        }
        return null;
    }

    /// <summary>Parses a release tag like "v0.37.4" or "0.37.4-rc1" into a Version (leading number part).</summary>
    internal static Version? ParseVersion(string tag)
    {
        var cleaned = tag.Trim().TrimStart('v', 'V');
        var end = 0;
        while (end < cleaned.Length && (char.IsDigit(cleaned[end]) || cleaned[end] == '.')) end++;
        if (!Version.TryParse(cleaned[..end], out var v)) return null;
        // Normalize to Major.Minor.Build so comparisons and ToString(3) are safe.
        return new Version(v.Major, v.Minor, Math.Max(0, v.Build));
    }

    public void Dispose() => _http.Dispose();
}

using System.Text;

namespace CodexBarTray;

/// <summary>The outcome of a cookie import attempt for one provider.</summary>
public sealed record CookieImportResult(bool Found, string? Header, int CookieCount, string Detail);

/// <summary>
/// Builds a Cookie header for a provider by extracting cookies from the user's
/// browsers (Chromium family first, then Firefox) for that provider's domains.
/// Runs file I/O + crypto, so callers should invoke it off the UI thread.
/// </summary>
public static class BrowserCookieImporter
{
    public static CookieImportResult ImportForProvider(string providerId)
    {
        var domains = ProviderCookieDomains.DomainsFor(providerId);
        if (domains is null)
            return new CookieImportResult(false, null, 0, "Provider does not use browser cookies.");

        var cookies = new List<CookieRecord>();
        try { cookies.AddRange(ChromiumCookieExtractor.Extract(domains)); }
        catch { /* fall through to Firefox */ }
        try { cookies.AddRange(FirefoxCookieExtractor.Extract(domains)); }
        catch { /* tolerate */ }

        if (cookies.Count == 0)
            return new CookieImportResult(false, null, 0, "No matching cookies found. Sign in to the provider in your browser first.");

        var header = BuildHeader(cookies);
        if (string.IsNullOrEmpty(header))
            return new CookieImportResult(false, null, 0, "Cookies were found but could not be read (newer Chrome encryption may be unsupported).");

        var count = header.Count(c => c == '=');
        return new CookieImportResult(true, header, count, $"Imported {count} cookie(s).");
    }

    private static string BuildHeader(IEnumerable<CookieRecord> cookies)
    {
        // First occurrence of each name wins (browser order: Chromium before Firefox).
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var sb = new StringBuilder();
        foreach (var cookie in cookies)
        {
            if (string.IsNullOrEmpty(cookie.Name) || string.IsNullOrEmpty(cookie.Value)) continue;
            if (!seen.Add(cookie.Name)) continue;
            if (sb.Length > 0) sb.Append("; ");
            sb.Append(cookie.Name).Append('=').Append(cookie.Value);
        }
        return sb.ToString();
    }
}

namespace CodexBarTray;

/// <summary>A single decrypted cookie.</summary>
public sealed record CookieRecord(string Host, string Name, string Value);

/// <summary>
/// Cookie domains per provider, mirroring the macOS importers' cookieDomains.
/// Drives which cookies the tray extracts for "Import browser cookies".
/// </summary>
public static class ProviderCookieDomains
{
    public static readonly IReadOnlyDictionary<string, string[]> Map =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["grok"] = new[] { "grok.com" },
            ["perplexity"] = new[] { "perplexity.ai", "www.perplexity.ai" },
            ["manus"] = new[] { "manus.im", "www.manus.im" },
            ["mistral"] = new[] { "mistral.ai", "admin.mistral.ai", "auth.mistral.ai" },
            ["abacus"] = new[] { "abacus.ai", "apps.abacus.ai" },
            ["commandcode"] = new[] { "commandcode.ai", "www.commandcode.ai" },
            ["opencode"] = new[] { "opencode.ai", "app.opencode.ai" },
            ["kimi"] = new[] { "kimi.com", "www.kimi.com" },
            ["t3chat"] = new[] { "t3.chat", "www.t3.chat" },
            ["amp"] = new[] { "ampcode.com", "www.ampcode.com" },
            ["ollama"] = new[] { "ollama.com", "www.ollama.com" },
            ["mimo"] = new[] { "platform.xiaomimimo.com", "xiaomimimo.com" },
            ["minimax"] = new[]
            {
                "platform.minimax.io", "openplatform.minimax.io", "minimax.io",
                "platform.minimaxi.com", "openplatform.minimaxi.com", "minimaxi.com",
            },
            ["cursor"] = new[] { "cursor.com", "www.cursor.com", "cursor.sh", "authenticator.cursor.sh" },
            ["factory"] = new[] { "factory.ai", "app.factory.ai", "auth.factory.ai" },
            ["alibaba"] = new[]
            {
                "bailian-singapore-cs.alibabacloud.com", "bailian-cs.console.aliyun.com",
                "bailian-beijing-cs.aliyuncs.com", "modelstudio.console.alibabacloud.com",
                "bailian.console.aliyun.com", "free.aliyun.com", "account.aliyun.com",
                "signin.aliyun.com", "passport.alibabacloud.com",
                "console.alibabacloud.com", "console.aliyun.com",
            },
        };

    public static bool IsCookieProvider(string providerId) => Map.ContainsKey(providerId);

    public static string[]? DomainsFor(string providerId) =>
        Map.TryGetValue(providerId, out var domains) ? domains : null;

    /// <summary>True when a cookie host (with any leading dot) belongs to one of the target domains.</summary>
    public static bool HostMatches(string host, IReadOnlyCollection<string> domains)
    {
        var h = host.TrimStart('.');
        foreach (var d in domains)
            if (h.Equals(d, StringComparison.OrdinalIgnoreCase) ||
                h.EndsWith("." + d, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }
}

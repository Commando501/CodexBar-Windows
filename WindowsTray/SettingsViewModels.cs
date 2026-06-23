using System.ComponentModel;

namespace CodexBarTray;

public enum AuthKind
{
    ApiKey,
    OAuthOrCli,
    CookieOrOther,
}

/// <summary>One row in the Settings list: a provider's enable state, auth kind,
/// and (for key-based providers) whether a key is stored.</summary>
public sealed class ProviderSettingRow : INotifyPropertyChanged
{
    public string Id { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public AuthKind AuthKind { get; init; }

    public bool ShowKeyField => AuthKind == AuthKind.ApiKey;

    public string AuthLabel => AuthKind switch
    {
        AuthKind.OAuthOrCli => "OAuth / CLI",
        AuthKind.CookieOrOther => "cookie / other — limited on Windows",
        _ => "",
    };

    public bool ShowAuthLabel => AuthKind != AuthKind.ApiKey;

    private bool _enabled;
    public bool Enabled
    {
        get => _enabled;
        set { if (_enabled != value) { _enabled = value; OnChanged(nameof(Enabled)); } }
    }

    private bool _hasKey;
    public bool HasKey
    {
        get => _hasKey;
        set { if (_hasKey != value) { _hasKey = value; OnChanged(nameof(HasKey)); OnChanged(nameof(KeyHint)); } }
    }

    public string KeyHint => HasKey ? "key set — type to replace" : "enter API key";

    private string _status = "";
    public string Status
    {
        get => _status;
        set { _status = value; OnChanged(nameof(Status)); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>Classifies a provider id into how it authenticates, which drives the UI
/// (key field vs. an informational label).</summary>
public static class AuthClassifier
{
    // Providers that accept a direct API key (ProviderConfigEnvironment.supportsAPIKeyOverride).
    private static readonly HashSet<string> ApiKeyIds = new(StringComparer.OrdinalIgnoreCase)
    {
        "amp", "openai", "azureopenai", "zai", "minimax", "alibaba", "kilo", "synthetic",
        "openrouter", "elevenlabs", "moonshot", "kimi", "ollama", "venice", "deepgram", "groq",
        "llmproxy", "litellm", "chutes", "poe", "copilot", "kimik2", "warp", "codebuff", "crof", "doubao",
    };

    // Providers that work on Windows today via OAuth or local CLI/files (no key field).
    // Claude authenticates via the OAuth credentials file (~/.claude/.credentials.json) and
    // self-refreshes on Windows, so it needs no API key here (its Admin API key remains
    // settable via `codexbar config set-api-key` for advanced users).
    private static readonly HashSet<string> OAuthOrCliIds = new(StringComparer.OrdinalIgnoreCase)
    {
        "codex", "claude", "gemini", "vertexai", "kiro",
    };

    public static AuthKind Classify(string id)
    {
        if (ApiKeyIds.Contains(id)) return AuthKind.ApiKey;
        if (OAuthOrCliIds.Contains(id)) return AuthKind.OAuthOrCli;
        return AuthKind.CookieOrOther;
    }
}

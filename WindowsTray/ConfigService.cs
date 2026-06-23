using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CodexBarTray;

/// <summary>
/// Drives the Swift <c>codexbar config …</c> subcommands as the backend for the
/// Settings UI (read provider list + key presence, toggle enable, store keys).
/// Uses the same executable + Swift-runtime resolution as <see cref="ServeProcess"/>.
/// </summary>
public sealed class ConfigService
{
    private readonly string _exePath;
    private readonly string? _swiftRuntimeDir;

    public ConfigService()
    {
        _exePath = AppPaths.ResolveCodexBarExe();
        _swiftRuntimeDir = AppPaths.ResolveSwiftRuntimeDir();
    }

    public async Task<List<ProviderInfo>> GetProvidersAsync()
    {
        var json = await RunAsync(new[] { "config", "providers", "--json" });
        return JsonSerializer.Deserialize<List<ProviderInfo>>(json, JsonOptions) ?? new();
    }

    /// <summary>Map of provider id → whether an API key is already stored.</summary>
    public async Task<Dictionary<string, bool>> GetKeyPresenceAsync()
    {
        var json = await RunAsync(new[] { "config", "dump", "--json" });
        var dump = JsonSerializer.Deserialize<ConfigDump>(json, JsonOptions);
        var map = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        if (dump?.Providers is not null)
        {
            foreach (var p in dump.Providers)
            {
                if (p.Id is not null)
                {
                    map[p.Id] = !string.IsNullOrEmpty(p.ApiKey);
                }
            }
        }
        return map;
    }

    public Task SetEnabledAsync(string id, bool enabled) =>
        RunAsync(new[] { "config", enabled ? "enable" : "disable", "--provider", id });

    public Task SetApiKeyAsync(string id, string key) =>
        RunAsync(new[] { "config", "set-api-key", "--provider", id, "--stdin" }, stdin: key);

    private async Task<string> RunAsync(string[] args, string? stdin = null)
    {
        if (!File.Exists(_exePath))
        {
            throw new FileNotFoundException($"codexbar executable not found at: {_exePath}", _exePath);
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = _exePath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = stdin is not null,
            StandardOutputEncoding = Encoding.UTF8,
        };
        foreach (var arg in args) startInfo.ArgumentList.Add(arg);

        if (!string.IsNullOrEmpty(_swiftRuntimeDir) && Directory.Exists(_swiftRuntimeDir))
        {
            var existingPath = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            startInfo.Environment["PATH"] = _swiftRuntimeDir + Path.PathSeparator + existingPath;
        }

        using var process = new Process { StartInfo = startInfo };
        process.Start();

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        if (stdin is not null)
        {
            await process.StandardInput.WriteAsync(stdin);
            process.StandardInput.Close();
        }

        await process.WaitForExitAsync();
        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        if (process.ExitCode != 0)
        {
            var detail = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
            throw new InvalidOperationException(
                $"codexbar {string.Join(' ', args)} failed (exit {process.ExitCode}): {detail.Trim()}");
        }
        return stdout;
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public sealed class ProviderInfo
    {
        [JsonPropertyName("provider")] public string Provider { get; set; } = "";
        [JsonPropertyName("displayName")] public string DisplayName { get; set; } = "";
        [JsonPropertyName("enabled")] public bool Enabled { get; set; }
        [JsonPropertyName("defaultEnabled")] public bool DefaultEnabled { get; set; }
    }

    private sealed class ConfigDump
    {
        [JsonPropertyName("providers")] public List<DumpProvider>? Providers { get; set; }
    }

    private sealed class DumpProvider
    {
        [JsonPropertyName("id")] public string? Id { get; set; }
        [JsonPropertyName("apiKey")] public string? ApiKey { get; set; }
    }
}

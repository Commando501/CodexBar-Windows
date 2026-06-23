using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace CodexBarTray;

/// <summary>
/// Extracts cookies from Chromium-family browsers (Chrome, Edge, Brave). Reads the
/// AES key from the browser's "Local State" (DPAPI-wrapped), then decrypts each
/// cookie's value: v10/v11 are AES-256-GCM; legacy values are raw DPAPI. v20
/// (app-bound, Chrome 127+) can't be decrypted without elevation and is skipped.
/// </summary>
public static class ChromiumCookieExtractor
{
    private readonly record struct Browser(string Name, string UserDataDir);

    public static List<CookieRecord> Extract(IReadOnlyCollection<string> domains)
    {
        var result = new List<CookieRecord>();
        foreach (var browser in DiscoverBrowsers())
        {
            try { ExtractBrowser(browser, domains, result); }
            catch { /* skip a browser we can't read (not installed / locked / format change) */ }
        }
        return result;
    }

    private static IEnumerable<Browser> DiscoverBrowsers()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var candidates = new[]
        {
            new Browser("Chrome", Path.Combine(local, "Google", "Chrome", "User Data")),
            new Browser("Edge", Path.Combine(local, "Microsoft", "Edge", "User Data")),
            new Browser("Brave", Path.Combine(local, "BraveSoftware", "Brave-Browser", "User Data")),
        };
        foreach (var c in candidates)
            if (Directory.Exists(c.UserDataDir))
                yield return c;
    }

    private static void ExtractBrowser(Browser browser, IReadOnlyCollection<string> domains, List<CookieRecord> sink)
    {
        var key = LoadAesKey(browser.UserDataDir);
        foreach (var cookiesPath in FindCookieDbs(browser.UserDataDir))
        {
            try { ExtractDb(cookiesPath, key, domains, sink); }
            catch { /* one profile failing shouldn't stop the others */ }
        }
    }

    private static byte[]? LoadAesKey(string userDataDir)
    {
        var localStatePath = Path.Combine(userDataDir, "Local State");
        if (!File.Exists(localStatePath)) return null;

        using var doc = JsonDocument.Parse(File.ReadAllText(localStatePath));
        if (!doc.RootElement.TryGetProperty("os_crypt", out var osCrypt) ||
            !osCrypt.TryGetProperty("encrypted_key", out var encKeyEl) ||
            encKeyEl.GetString() is not { } encKeyB64)
            return null;

        var encrypted = Convert.FromBase64String(encKeyB64);
        // Strip the "DPAPI" prefix (5 bytes) then unprotect.
        if (encrypted.Length <= 5) return null;
        return ProtectedData.Unprotect(encrypted[5..], null, DataProtectionScope.CurrentUser);
    }

    private static IEnumerable<string> FindCookieDbs(string userDataDir)
    {
        // Each profile keeps cookies at <profile>\Network\Cookies (newer) or <profile>\Cookies (older).
        foreach (var profile in Directory.EnumerateDirectories(userDataDir))
        {
            var networkDb = Path.Combine(profile, "Network", "Cookies");
            if (File.Exists(networkDb)) { yield return networkDb; continue; }
            var legacyDb = Path.Combine(profile, "Cookies");
            if (File.Exists(legacyDb)) yield return legacyDb;
        }
    }

    private static void ExtractDb(string cookiesPath, byte[]? key, IReadOnlyCollection<string> domains, List<CookieRecord> sink)
    {
        var temp = Path.Combine(Path.GetTempPath(), $"cb-cookies-{Guid.NewGuid():N}.db");
        try
        {
            CopyShared(cookiesPath, temp);
            using var conn = new SqliteConnection($"Data Source={temp};Mode=ReadOnly;Cache=Private");
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT host_key, name, encrypted_value, value FROM cookies";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var host = reader.GetString(0);
                if (!ProviderCookieDomains.HostMatches(host, domains)) continue;

                var name = reader.GetString(1);
                var encrypted = reader.IsDBNull(2) ? Array.Empty<byte>() : (byte[])reader.GetValue(2);
                var plainValue = reader.IsDBNull(3) ? "" : reader.GetString(3);

                var value = DecryptValue(encrypted, key) ?? (plainValue.Length > 0 ? plainValue : null);
                if (value is not null)
                    sink.Add(new CookieRecord(host, name, value));
            }
        }
        finally
        {
            try { File.Delete(temp); } catch { /* best effort */ }
        }
    }

    private static string? DecryptValue(byte[] encrypted, byte[]? key)
    {
        if (encrypted.Length == 0) return null;

        // Version-prefixed (v10/v11 = AES-GCM, v20 = app-bound and unsupported here).
        if (encrypted.Length >= 3 && encrypted[0] == (byte)'v' && encrypted[1] == (byte)'1' &&
            (encrypted[2] == (byte)'0' || encrypted[2] == (byte)'1'))
        {
            if (key is null || encrypted.Length < 3 + 12 + 16) return null;
            try
            {
                var nonce = encrypted.AsSpan(3, 12);
                var tag = encrypted.AsSpan(encrypted.Length - 16, 16);
                var cipher = encrypted.AsSpan(15, encrypted.Length - 15 - 16);
                var plain = new byte[cipher.Length];
                using var gcm = new AesGcm(key, 16);
                gcm.Decrypt(nonce, cipher, tag, plain);
                return Encoding.UTF8.GetString(plain);
            }
            catch { return null; }
        }

        // v20: app-bound encryption (Chrome 127+) — not decryptable without elevation.
        if (encrypted.Length >= 3 && encrypted[0] == (byte)'v' && encrypted[1] == (byte)'2' && encrypted[2] == (byte)'0')
            return null;

        // Legacy: the value is wrapped directly with DPAPI.
        try { return Encoding.UTF8.GetString(ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser)); }
        catch { return null; }
    }

    private static void CopyShared(string source, string dest)
    {
        // Copy with shared access so a running browser holding the DB doesn't block us.
        using var src = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var dst = new FileStream(dest, FileMode.Create, FileAccess.Write, FileShare.None);
        src.CopyTo(dst);
    }
}

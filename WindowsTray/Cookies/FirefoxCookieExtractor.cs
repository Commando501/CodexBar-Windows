using System.IO;
using Microsoft.Data.Sqlite;

namespace CodexBarTray;

/// <summary>
/// Extracts cookies from Firefox profiles. Firefox stores cookies unencrypted in
/// <c>cookies.sqlite</c> (moz_cookies), so no key handling is needed.
/// </summary>
public static class FirefoxCookieExtractor
{
    public static List<CookieRecord> Extract(IReadOnlyCollection<string> domains)
    {
        var result = new List<CookieRecord>();
        var profilesRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Mozilla", "Firefox", "Profiles");
        if (!Directory.Exists(profilesRoot)) return result;

        foreach (var profile in Directory.EnumerateDirectories(profilesRoot))
        {
            var dbPath = Path.Combine(profile, "cookies.sqlite");
            if (!File.Exists(dbPath)) continue;
            try { ExtractDb(dbPath, domains, result); }
            catch { /* skip an unreadable profile */ }
        }
        return result;
    }

    private static void ExtractDb(string dbPath, IReadOnlyCollection<string> domains, List<CookieRecord> sink)
    {
        var temp = Path.Combine(Path.GetTempPath(), $"cb-ffcookies-{Guid.NewGuid():N}.db");
        try
        {
            using (var src = new FileStream(dbPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var dst = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
                src.CopyTo(dst);

            using var conn = new SqliteConnection($"Data Source={temp};Mode=ReadOnly;Cache=Private");
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT host, name, value FROM moz_cookies";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var host = reader.GetString(0);
                if (!ProviderCookieDomains.HostMatches(host, domains)) continue;
                sink.Add(new CookieRecord(host, reader.GetString(1), reader.GetString(2)));
            }
        }
        finally
        {
            try { File.Delete(temp); } catch { /* best effort */ }
        }
    }
}

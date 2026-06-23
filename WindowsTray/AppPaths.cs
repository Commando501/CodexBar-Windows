using System.IO;

namespace CodexBarTray;

/// <summary>
/// Resolves the location of the Swift-built <c>codexbar</c> executable and the
/// Swift runtime DLLs it needs on PATH. Resolution order favors an explicit env
/// var, then files shipped next to the tray app (release layout), then the local
/// dev build output (so it "just works" while developing in the repo).
/// </summary>
public static class AppPaths
{
    public static string ResolveCodexBarExe()
    {
        var fromEnv = Environment.GetEnvironmentVariable("CODEXBAR_EXE");
        if (!string.IsNullOrWhiteSpace(fromEnv) && File.Exists(fromEnv))
        {
            return fromEnv;
        }

        var appDir = AppContext.BaseDirectory;
        foreach (var name in new[] { "codexbar.exe", "CodexBarCLI.exe" })
        {
            var beside = Path.Combine(appDir, name);
            if (File.Exists(beside)) return beside;
        }

        // Dev fallback: the SwiftPM debug build output within the repo.
        var devBuild = FindUpwards(appDir,
            Path.Combine(".build", "x86_64-unknown-windows-msvc", "debug", "CodexBarCLI.exe"));
        if (devBuild is not null) return devBuild;

        // Last resort: return the expected release name beside the app so the
        // resulting error message points at where the file should live.
        return Path.Combine(appDir, "codexbar.exe");
    }

    public static string? ResolveSwiftRuntimeDir()
    {
        var fromEnv = Environment.GetEnvironmentVariable("CODEXBAR_SWIFT_RUNTIME");
        if (!string.IsNullOrWhiteSpace(fromEnv) && Directory.Exists(fromEnv))
        {
            return fromEnv;
        }

        // If a release ships the runtime DLLs beside the exe, no extra dir needed.
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var runtimesRoot = Path.Combine(localAppData, "Programs", "Swift", "Runtimes");
        if (Directory.Exists(runtimesRoot))
        {
            // Pick the newest installed runtime version directory.
            var candidate = Directory.GetDirectories(runtimesRoot)
                .OrderByDescending(d => d)
                .Select(d => Path.Combine(d, "usr", "bin"))
                .FirstOrDefault(Directory.Exists);
            if (candidate is not null) return candidate;
        }

        return null;
    }

    private static string? FindUpwards(string startDir, string relativePath)
    {
        var dir = new DirectoryInfo(startDir);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, relativePath);
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        return null;
    }
}

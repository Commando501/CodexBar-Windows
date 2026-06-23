using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;

namespace CodexBarTray;

/// <summary>
/// Owns the lifecycle of a <c>codexbar serve</c> child process. Launches it with
/// <c>--port 0</c> so the OS picks a free port, then parses the actual port from
/// the server's stderr ("listening on http://127.0.0.1:PORT"). Exposes the base
/// URL the <see cref="UsageClient"/> talks to.
/// </summary>
public sealed class ServeProcess : IDisposable
{
    private static readonly Regex ListeningRegex =
        new(@"listening on http://127\.0\.0\.1:(\d+)", RegexOptions.Compiled);

    private readonly string _exePath;
    private readonly string? _swiftRuntimeDir;
    private Process? _process;
    private readonly JobObject _job = new();
    private readonly TaskCompletionSource<int> _portReady =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public ServeProcess(string exePath, string? swiftRuntimeDir)
    {
        _exePath = exePath;
        _swiftRuntimeDir = swiftRuntimeDir;
    }

    /// <summary>The port the server bound to (valid after <see cref="StartAsync"/> completes).</summary>
    public int Port { get; private set; }

    /// <summary>Base URL, e.g. http://127.0.0.1:55730 .</summary>
    public string BaseUrl => $"http://127.0.0.1:{Port}";

    public bool IsRunning => _process is { HasExited: false };

    /// <summary>
    /// Starts the server and resolves once it reports its listening port (or throws on
    /// timeout / early exit).
    /// </summary>
    public async Task StartAsync(TimeSpan timeout)
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
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        };
        startInfo.ArgumentList.Add("serve");
        startInfo.ArgumentList.Add("--port");
        startInfo.ArgumentList.Add("0");

        // The Swift-built codexbar.exe needs the Swift runtime DLLs on PATH.
        if (!string.IsNullOrEmpty(_swiftRuntimeDir) && Directory.Exists(_swiftRuntimeDir))
        {
            var existingPath = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            startInfo.Environment["PATH"] = _swiftRuntimeDir + Path.PathSeparator + existingPath;
        }

        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        process.ErrorDataReceived += OnStderr;
        process.OutputDataReceived += (_, _) => { /* drain stdout to avoid blocking */ };
        process.Exited += (_, _) =>
            _portReady.TrySetException(new InvalidOperationException(
                $"codexbar serve exited (code {SafeExitCode(process)}) before reporting a port."));

        _process = process;
        process.Start();

        // Tie the engine's lifetime to ours: if the tray dies (even a forced
        // kill), the job closes and Windows terminates the serve process.
        try
        {
            _job.AssignProcess(process.Handle);
        }
        catch
        {
            // Non-fatal: fall back to best-effort kill on Dispose.
        }

        process.BeginErrorReadLine();
        process.BeginOutputReadLine();

        var completed = await Task.WhenAny(_portReady.Task, Task.Delay(timeout)).ConfigureAwait(false);
        if (completed != _portReady.Task)
        {
            throw new TimeoutException("codexbar serve did not report a listening port in time.");
        }

        Port = await _portReady.Task.ConfigureAwait(false);
    }

    private void OnStderr(object sender, DataReceivedEventArgs e)
    {
        if (string.IsNullOrEmpty(e.Data)) return;
        var match = ListeningRegex.Match(e.Data);
        if (match.Success && int.TryParse(match.Groups[1].Value, out var port))
        {
            _portReady.TrySetResult(port);
        }
    }

    private static int SafeExitCode(Process p)
    {
        try { return p.ExitCode; } catch { return -1; }
    }

    public void Dispose()
    {
        try
        {
            if (_process is { HasExited: false })
            {
                _process.Kill(entireProcessTree: true);
                _process.WaitForExit(2000);
            }
        }
        catch
        {
            // best-effort shutdown
        }
        finally
        {
            _process?.Dispose();
            _process = null;
            _job.Dispose();
        }
    }
}

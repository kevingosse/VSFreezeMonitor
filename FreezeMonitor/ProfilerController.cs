using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Profiler.SelfApi;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Threading;

namespace FreezeMonitor;

internal sealed class ProfilerController : IDisposable
{
    private readonly MetricsService _metrics;
    private readonly ProfilerOptions _options;
    private readonly JoinableTaskFactory _jtf;

    private CancellationTokenSource _cts;
    private JoinableTask _watchdog;

    // Only written/read on the watchdog thread.
    private bool _isProfiling;
    private long _profilingStartTick;

    // Written from two threads:
    //   • watchdog thread  — while the UI is currently overdue (ongoing freeze)
    //   • UI thread        — via SampleReceived, when a sample reports ≥100 ms latency
    // Both use Volatile so the watchdog always sees the latest value.
    private long _lastHighLatencyTick;

    private static readonly long OneSecondTicks = Stopwatch.Frequency;
    private static readonly long HundredMsTicks = (long)(0.100 * Stopwatch.Frequency);

    // Raised on the watchdog thread; subscribers must marshal to UI if needed.
    public event Action<string> StatusChanged;
    public string CurrentStatus { get; private set; } = "Idle";

    // Fired (on the watchdog thread) when a snapshot is successfully saved.
    // Argument is the display string: "filename.dtp (12.3 s)".
    public event Action<string> SnapshotSaved;

    public ProfilerController(MetricsService metrics, ProfilerOptions options, JoinableTaskFactory jtf)
    {
        _metrics = metrics;
        _options = options;
        _jtf = jtf;

        // UI-thread callback: every sample with ≥100 ms latency resets the recovery clock.
        metrics.SampleReceived += OnSampleReceived;
    }

    private string GetSnapshotDir()
    {
        var folder = _options.SnapshotFolder;
        if (string.IsNullOrWhiteSpace(folder))
            folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "FreezeMonitor", "Snapshots");
        Directory.CreateDirectory(folder);
        return folder;
    }

    // Called on the UI thread.
    private void OnSampleReceived(TimeSpan latency)
    {
        if (latency.TotalMilliseconds >= 100)
            Volatile.Write(ref _lastHighLatencyTick, Stopwatch.GetTimestamp());
    }

    // Must be called on the UI thread.
    public void Start()
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        // Initialise so the watchdog doesn't think there's already been a freeze.
        _lastHighLatencyTick = Stopwatch.GetTimestamp();

        _cts = new CancellationTokenSource();
        _watchdog = _jtf.RunAsync(() => WatchdogLoopAsync(_cts.Token));
    }

    public async Task StopAsync()
    {
        _metrics.SampleReceived -= OnSampleReceived;
        _cts?.Cancel();
        try { if (_watchdog != null) await _watchdog; }
        catch (OperationCanceledException) { }
        finally { _cts?.Dispose(); }
    }

    private async Task WatchdogLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            await Task.Delay(200, token).ConfigureAwait(false);

            var now = Stopwatch.GetTimestamp();
            var timeSinceLastSample = now - _metrics.LastSampleTicks;

            // If the UI is currently stalled (no sample for ≥100 ms), keep
            // _lastHighLatencyTick fresh. This covers ongoing freezes where no
            // sample callbacks are arriving at all.
            if (timeSinceLastSample >= HundredMsTicks)
            {
                Volatile.Write(ref _lastHighLatencyTick, now);

                var opts = _options;

                // Only profile if the freeze has lasted at least StartDelaySeconds.
                long startDelayTicks = (long)(opts.StartDelaySeconds * (double)Stopwatch.Frequency);

                if (!_isProfiling && opts.ProfilingMode != ProfilingMode.Off
                    && timeSinceLastSample >= startDelayTicks)
                {
                    _isProfiling = true;
                    StartProfiling();
                }
            }
            else if (_isProfiling)
            {
                // UI is currently responsive. Stop once _lastHighLatencyTick has
                // been untouched for 1 s — meaning no freeze of ≥100 ms has
                // occurred in the past second (from either source).
                if ((now - Volatile.Read(ref _lastHighLatencyTick)) >= OneSecondTicks)
                {
                    _isProfiling = false;
                    await StopProfilingAsync().ConfigureAwait(false);
                }
            }
        }
    }

    private void StartProfiling()
    {
        try
        {
            _ = Task.Run(() => SetStatus("● Recording..."));
            var config = new DotTrace.Config()
                .UseTimelineProfilingType()
                .WithCommandLineArgument("--download-symbols")
                .SaveToDir(GetSnapshotDir());

            DotTrace.Attach(config);
            _profilingStartTick = Stopwatch.GetTimestamp();
            DotTrace.StartCollectingData();
        }
        catch (Exception ex)
        {
            _isProfiling = false;
            SetStatus($"Attach error: {ex.Message}");
        }
    }

    private async Task StopProfilingAsync()
    {
        SetStatus("Saving snapshot...");
        var snapshotDir = GetSnapshotDir();
        try
        {
            var elapsed = TimeSpan.Zero;

            string savedFile = await Task.Run(() =>
            {
                elapsed = TimeSpan.FromSeconds(
                    (double)(Stopwatch.GetTimestamp() - _profilingStartTick) / Stopwatch.Frequency);
                DotTrace.SaveData();
                DotTrace.Detach();
                return FindLatestSnapshot(snapshotDir);
            }).ConfigureAwait(false);
            string duration = elapsed.TotalMinutes >= 1
                ? $"{(int)elapsed.TotalMinutes}:{elapsed.Seconds:D2}"
                : $"{elapsed.TotalSeconds:F1} s";

            SetStatus(savedFile != null
                ? $"Saved: {Path.GetFileName(savedFile)} ({duration})"
                : $"Saved ({duration})");

            if (savedFile != null)
                SnapshotSaved?.Invoke($"{Path.GetFileName(savedFile)} ({duration})");
        }
        catch (Exception ex)
        {
            SetStatus($"Save error: {ex.Message}");
        }
    }

    private static string FindLatestSnapshot(string dir)
    {
        string latest = null;
        DateTime latestTime = DateTime.MinValue;
        foreach (var file in Directory.GetFiles(dir))
        {
            var t = File.GetLastWriteTime(file);
            if (t > latestTime) { latestTime = t; latest = file; }
        }
        return latest;
    }

    private void SetStatus(string status)
    {
        CurrentStatus = status;
        StatusChanged?.Invoke(status);
    }

    public void Dispose() => _ = StopAsync();
}

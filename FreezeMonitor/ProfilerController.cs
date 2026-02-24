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

    // Written from two threads:
    //   • watchdog thread  — while the UI is currently overdue (ongoing freeze)
    //   • UI thread        — via SampleReceived, when a sample reports ≥100 ms latency
    // Both use Volatile so the watchdog always sees the latest value.
    private long _lastHighLatencyTick;

    // 0 = solution not yet fully loaded; 1 = fully loaded (or gating is disabled).
    // Written on the UI thread via UIContextChanged; read on the watchdog thread.
    private int _solutionLoaded;

    // Stopwatch timestamp of the moment the profiling gate opened (solution loaded, or
    // Start() called when gating is disabled / solution was already loaded).
    // The watchdog will only trigger on freezes whose last sample arrived after this tick,
    // preventing the solution-load freeze itself from immediately starting the profiler.
    private long _gateOpenedTick;

    private static readonly long OneSecondTicks = Stopwatch.Frequency;
    private static readonly long HundredMsTicks = (long)(0.100 * Stopwatch.Frequency);

    // Raised on the watchdog thread; subscribers must marshal to UI if needed.
    public event Action<string> StatusChanged;
    public string CurrentStatus { get; private set; } = "Idle";

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

        // Seed the solution-loaded flag from the current context state.
        var ctx = KnownUIContexts.SolutionExistsAndFullyLoadedContext;
        bool isLoaded = ctx.IsActive;
        Volatile.Write(ref _solutionLoaded, isLoaded ? 1 : 0);

        // Open the gate now if gating is disabled or the solution is already loaded;
        // otherwise the gate opens when OnSolutionLoadContextChanged fires.
        var mode = _options.ProfilingMode;
        if (mode == ProfilingMode.AlwaysOn || (mode == ProfilingMode.OnlyWhenSolutionLoaded && isLoaded))
            Volatile.Write(ref _gateOpenedTick, Stopwatch.GetTimestamp());

        ctx.UIContextChanged += OnSolutionLoadContextChanged;

        // Initialise so the watchdog doesn't think there's already been a freeze.
        _lastHighLatencyTick = Stopwatch.GetTimestamp();

        _cts = new CancellationTokenSource();
        _watchdog = _jtf.RunAsync(() => WatchdogLoopAsync(_cts.Token));
    }

    // Called on the UI thread when the solution-loaded context flips.
    private void OnSolutionLoadContextChanged(object sender, UIContextChangedEventArgs e)
    {
        if (e.Activated)
        {
            // Record when the gate opened so the watchdog can ignore any freeze
            // that was already in progress during solution load.
            Volatile.Write(ref _gateOpenedTick, Stopwatch.GetTimestamp());
            Volatile.Write(ref _solutionLoaded, 1);
        }
    }

    public async Task StopAsync()
    {
        KnownUIContexts.SolutionExistsAndFullyLoadedContext.UIContextChanged
            -= OnSolutionLoadContextChanged;
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

                // Only start profiling if the solution-load gate is satisfied.
                bool gateOpen = opts.ProfilingMode != ProfilingMode.Off
                    && (opts.ProfilingMode == ProfilingMode.AlwaysOn
                        || Volatile.Read(ref _solutionLoaded) == 1);

                // Also require that the last sample arrived after the gate opened.
                // This prevents the solution-load freeze itself from triggering the
                // profiler the moment the context flips to "fully loaded".
                bool freezeStartedAfterGate =
                    _metrics.LastSampleTicks >= Volatile.Read(ref _gateOpenedTick);

                // Only profile if the freeze has lasted at least StartDelaySeconds.
                long startDelayTicks = (long)(opts.StartDelaySeconds * (double)Stopwatch.Frequency);

                if (!_isProfiling && gateOpen && freezeStartedAfterGate
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
            string savedFile = await Task.Run(() =>
            {
                DotTrace.SaveData();
                DotTrace.Detach();
                return FindLatestSnapshot(snapshotDir);
            }).ConfigureAwait(false);

            SetStatus(savedFile != null
                ? $"Saved: {Path.GetFileName(savedFile)}"
                : "Saved");
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

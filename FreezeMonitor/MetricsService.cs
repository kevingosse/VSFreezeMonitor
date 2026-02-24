using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace FreezeMonitor;

public sealed class MetricsService : IDisposable
{
    private readonly UiThreadSampler _sampler;
    private readonly object _gate = new object();
    private Timer _refreshTimer;

    // ── Sliding window ──────────────────────────────────────────────────────
    private static readonly long WindowTicks =
        (long)(5.0 * Stopwatch.Frequency); // 5 seconds

    // Each entry: (Stopwatch timestamp, latency ms). At 20 ms period this is
    // at most ~250 entries — cheap to keep verbatim.
    private readonly Queue<(long ticks, double ms)> _window =
        new Queue<(long, double)>();

    private double _lastSampleMs;

    // Read by ProfilerController's background watchdog without the lock.
    private long _lastSampleTicks = Stopwatch.GetTimestamp();
    internal long LastSampleTicks => Volatile.Read(ref _lastSampleTicks);

    // ── Session accumulators (exact) ────────────────────────────────────────
    private long   _sessionCount;
    private double _sessionMax;
    private long   _sessionOver100;
    private long   _sessionOver250;
    private long   _sessionOver1000;

    // Fired on the UI thread for every raw sample — used by ProfilerController.
    internal event Action<TimeSpan> SampleReceived;

    public event Action<MetricsSnapshot> SnapshotUpdated;

    public MetricsService()
    {
        _sampler = new UiThreadSampler(OnSample);
    }

    // Must be called on the UI thread (UiThreadSampler captures Dispatcher there).
    public void Start()
    {
        _sampler.Start();
        _refreshTimer = new Timer(
            _ => SnapshotUpdated?.Invoke(GetSnapshot()),
            state: null,
            dueTime:  TimeSpan.FromSeconds(1),
            period:   TimeSpan.FromSeconds(1));
    }

    public async System.Threading.Tasks.Task StopAsync()
    {
        _refreshTimer?.Dispose();
        _refreshTimer = null;
        await _sampler.StopAsync().ConfigureAwait(false);
    }

    private void OnSample(TimeSpan latency)
    {
        var ms    = latency.TotalMilliseconds;
        var ticks = Stopwatch.GetTimestamp();

        // Update before the lock so ProfilerController's watchdog sees it promptly.
        Volatile.Write(ref _lastSampleTicks, ticks);

        SampleReceived?.Invoke(latency);

        lock (_gate)
        {
            _lastSampleMs = ms;

            // Sliding window
            _window.Enqueue((ticks, ms));
            TrimWindow(ticks);

            // Session exact accumulators
            _sessionCount++;
            if (ms > _sessionMax)  _sessionMax = ms;
            if (ms > 100)          _sessionOver100++;
            if (ms > 250)          _sessionOver250++;
            if (ms > 1000)         _sessionOver1000++;
        }
    }

    // Must be called under _gate.
    private void TrimWindow(long nowTicks)
    {
        long cutoff = nowTicks - WindowTicks;
        while (_window.Count > 0 && _window.Peek().ticks < cutoff)
            _window.Dequeue();
    }

    public MetricsSnapshot GetSnapshot()
    {
        double[] windowMs;
        double   latest;
        long     sessionCount;
        double   sessionMax;
        long     sessionOver100, sessionOver250, sessionOver1000;

        long nowTicks = Stopwatch.GetTimestamp();

        lock (_gate)
        {
            TrimWindow(nowTicks);

            latest   = _lastSampleMs;
            windowMs = new double[_window.Count];
            int wi = 0;
            foreach (var (_, ms) in _window)
                windowMs[wi++] = ms;

            sessionCount    = _sessionCount;
            sessionMax      = _sessionMax;
            sessionOver100  = _sessionOver100;
            sessionOver250  = _sessionOver250;
            sessionOver1000 = _sessionOver1000;
        }

        return new MetricsSnapshot(
            WindowMetrics.From(windowMs, latest),
            SessionMetrics.From(sessionCount, sessionMax,
                                sessionOver100, sessionOver250, sessionOver1000));
    }

    public void Dispose()
    {
        _refreshTimer?.Dispose();
        _sampler.Dispose();
    }
}

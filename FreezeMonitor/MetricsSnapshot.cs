using System;

namespace FreezeMonitor;

// ---------------------------------------------------------------------------
// Window metrics  (exact, covers the last N seconds)
// ---------------------------------------------------------------------------

public sealed class WindowMetrics
{
    public int    SampleCount { get; }
    public double LatestMs   { get; }
    public double MaxMs      { get; }
    public int    Over100Ms  { get; }
    public int    Over250Ms  { get; }
    public int    Over1000Ms { get; }

    private WindowMetrics() { }
    private WindowMetrics(int n, double latest, double max, int o100, int o250, int o1000)
    {
        SampleCount = n;   LatestMs = latest; MaxMs = max;
        Over100Ms = o100;  Over250Ms = o250;  Over1000Ms = o1000;
    }

    public static WindowMetrics Empty { get; } = new WindowMetrics();

    public static WindowMetrics From(double[] samples, double latestMs)
    {
        int n = samples.Length;
        if (n == 0) return Empty;

        double max = 0;
        int o100 = 0, o250 = 0, o1000 = 0;
        for (int i = 0; i < n; i++)
        {
            double v = samples[i];
            if (v > max)  max = v;
            if (v > 100)  o100++;
            if (v > 250)  o250++;
            if (v > 1000) o1000++;
        }

        return new WindowMetrics(n, latestMs, max, o100, o250, o1000);
    }
}

// ---------------------------------------------------------------------------
// Session metrics  (bounded memory)
// ---------------------------------------------------------------------------

public sealed class SessionMetrics
{
    public long   SampleCount { get; }
    public double MaxMs      { get; }
    public long   Over100Ms  { get; }
    public long   Over250Ms  { get; }
    public long   Over1000Ms { get; }

    private SessionMetrics() { }
    private SessionMetrics(long n, double max, long o100, long o250, long o1000)
    {
        SampleCount = n;   MaxMs = max;
        Over100Ms = o100;  Over250Ms = o250;  Over1000Ms = o1000;
    }

    public static SessionMetrics Empty { get; } = new SessionMetrics();

    public static SessionMetrics From(long count, double max, long o100, long o250, long o1000)
    {
        if (count == 0) return Empty;
        return new SessionMetrics(count, max, o100, o250, o1000);
    }
}

// ---------------------------------------------------------------------------
// Combined snapshot
// ---------------------------------------------------------------------------

public sealed class MetricsSnapshot
{
    public WindowMetrics  Window  { get; }
    public SessionMetrics Session { get; }

    public MetricsSnapshot(WindowMetrics window, SessionMetrics session)
    {
        Window  = window;
        Session = session;
    }

    public static MetricsSnapshot Empty { get; } =
        new MetricsSnapshot(WindowMetrics.Empty, SessionMetrics.Empty);
}

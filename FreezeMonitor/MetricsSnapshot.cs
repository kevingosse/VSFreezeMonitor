using System;

namespace FreezeMonitor;

// ---------------------------------------------------------------------------
// Window metrics  (exact, covers the last N seconds)
// ---------------------------------------------------------------------------

public sealed class WindowMetrics
{
    public int    SampleCount { get; }
    public double LatestMs   { get; }
    public double MeanMs     { get; }
    public double P50Ms      { get; }
    public double P95Ms      { get; }
    public double P99Ms      { get; }
    public double MaxMs      { get; }
    public int    Over100Ms  { get; }
    public int    Over250Ms  { get; }
    public int    Over1000Ms { get; }

    private WindowMetrics() { }
    private WindowMetrics(int n, double latest, double mean,
                          double p50, double p95, double p99, double max,
                          int o100, int o250, int o1000)
    {
        SampleCount = n;   LatestMs = latest; MeanMs = mean;
        P50Ms = p50;       P95Ms = p95;       P99Ms = p99;   MaxMs = max;
        Over100Ms = o100;  Over250Ms = o250;  Over1000Ms = o1000;
    }

    public static WindowMetrics Empty { get; } = new WindowMetrics();

    /// <param name="sorted">Ascending-sorted latency values (ms).</param>
    public static WindowMetrics FromSorted(double[] sorted, double latestMs)
    {
        int n = sorted.Length;
        if (n == 0) return Empty;

        double sum = 0;
        int o100 = 0, o250 = 0, o1000 = 0;
        for (int i = 0; i < n; i++)
        {
            double v = sorted[i];
            sum += v;
            if (v > 100)  o100++;
            if (v > 250)  o250++;
            if (v > 1000) o1000++;
        }

        return new WindowMetrics(n, latestMs, sum / n,
            Percentile(sorted, 0.50), Percentile(sorted, 0.95), Percentile(sorted, 0.99),
            sorted[n - 1], o100, o250, o1000);
    }

    private static double Percentile(double[] sorted, double p)
    {
        double rank = p * (sorted.Length - 1);
        int lo = (int)rank;
        int hi = Math.Min(lo + 1, sorted.Length - 1);
        return sorted[lo] + (rank - lo) * (sorted[hi] - sorted[lo]);
    }
}

// ---------------------------------------------------------------------------
// Session metrics  (bounded memory; percentiles via reservoir sampling)
// ---------------------------------------------------------------------------

public sealed class SessionMetrics
{
    public long   SampleCount { get; }
    public double MeanMs     { get; }
    public double MaxMs      { get; }
    /// <summary>Approximate — computed from a 10 000-sample reservoir.</summary>
    public double P50Ms      { get; }
    /// <inheritdoc cref="P50Ms"/>
    public double P95Ms      { get; }
    /// <inheritdoc cref="P50Ms"/>
    public double P99Ms      { get; }
    public long   Over100Ms  { get; }
    public long   Over250Ms  { get; }
    public long   Over1000Ms { get; }

    private SessionMetrics() { }
    private SessionMetrics(long n, double mean, double max,
                           double p50, double p95, double p99,
                           long o100, long o250, long o1000)
    {
        SampleCount = n;   MeanMs = mean;     MaxMs = max;
        P50Ms = p50;       P95Ms = p95;       P99Ms = p99;
        Over100Ms = o100;  Over250Ms = o250;  Over1000Ms = o1000;
    }

    public static SessionMetrics Empty { get; } = new SessionMetrics();

    /// <param name="sortedReservoir">Sorted copy of the reservoir (may be shorter than full session).</param>
    public static SessionMetrics From(long count, double mean, double max,
                                      long o100, long o250, long o1000,
                                      double[] sortedReservoir)
    {
        if (count == 0) return Empty;

        double p50 = 0, p95 = 0, p99 = 0;
        if (sortedReservoir.Length > 0)
        {
            p50 = Percentile(sortedReservoir, 0.50);
            p95 = Percentile(sortedReservoir, 0.95);
            p99 = Percentile(sortedReservoir, 0.99);
        }

        return new SessionMetrics(count, mean, max, p50, p95, p99, o100, o250, o1000);
    }

    private static double Percentile(double[] sorted, double p)
    {
        double rank = p * (sorted.Length - 1);
        int lo = (int)rank;
        int hi = Math.Min(lo + 1, sorted.Length - 1);
        return sorted[lo] + (rank - lo) * (sorted[hi] - sorted[lo]);
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

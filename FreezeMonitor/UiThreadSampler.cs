using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace FreezeMonitor;

public sealed class UiThreadSampler : IDisposable
{
    private readonly Action<TimeSpan> _onSample;
    private Dispatcher _dispatcher;
    private CancellationTokenSource _cts;
    private Task _loop;

    public TimeSpan Period { get; set; } = TimeSpan.FromMilliseconds(20);

    public UiThreadSampler(Action<TimeSpan> onSample)
    {
        _onSample = onSample ?? throw new ArgumentNullException(nameof(onSample));
    }

    // Must be called on the UI thread so we can capture its Dispatcher.
    public void Start()
    {
        if (_cts != null) return;
        _dispatcher = Dispatcher.CurrentDispatcher;
        _cts = new CancellationTokenSource();
        _loop = Task.Run(() => RunLoopAsync(_cts.Token));
    }

    public async Task StopAsync()
    {
        var cts = _cts;
        if (cts == null) return;
        _cts = null;
        cts.Cancel();
        try { if (_loop != null) await _loop.ConfigureAwait(false); }
        catch (OperationCanceledException) { }
        finally { cts.Dispose(); _loop = null; }
    }

    private async Task RunLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            await Task.Delay(Period, token).ConfigureAwait(false);

            // Capture timestamp right before posting to the message queue.
            // The delta to when the callback runs is the true UI-thread queue latency.
            var scheduledAt = Stopwatch.GetTimestamp();

            _dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() =>
            {
                var elapsed = Stopwatch.GetTimestamp() - scheduledAt;
                _onSample(TimeSpan.FromSeconds((double)elapsed / Stopwatch.Frequency));
            }));
        }
    }

    public void Dispose() => _ = StopAsync();
}

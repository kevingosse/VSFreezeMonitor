using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Threading;

namespace FreezeMonitor
{
    public partial class MetricsToolWindowControl : UserControl
    {
        private MetricsService _service;
        private JoinableTaskFactory _jtf;

        public MetricsToolWindowControl()
        {
            InitializeComponent();
        }

        internal void Initialize(MetricsService service, JoinableTaskFactory jtf)
        {
            _service = service;
            _jtf = jtf;
            service.SnapshotUpdated += OnSnapshotUpdated;
        }

        internal void InitializeProfiler(ProfilerController controller)
        {
            ProfilerStatusText.Text = controller.CurrentStatus;
            controller.StatusChanged += status =>
                _ = _jtf.RunAsync(async () =>
                {
                    await _jtf.SwitchToMainThreadAsync();
                    ProfilerStatusText.Text = status;
                });
        }

        private void OnSnapshotUpdated(MetricsSnapshot snapshot)
        {
            // Timer fires on a thread-pool thread; marshal to the UI thread.
            _ = _jtf.RunAsync(async () =>
            {
                await _jtf.SwitchToMainThreadAsync();
                ApplySnapshot(snapshot);
            });
        }

        private void ApplySnapshot(MetricsSnapshot snap)
        {
            var w = snap.Window;
            WLatestText.Text   = FormatMs(w.LatestMs);
            WMeanText.Text     = FormatMs(w.MeanMs);
            WP50Text.Text      = FormatMs(w.P50Ms);
            WP95Text.Text      = FormatMs(w.P95Ms);
            WP99Text.Text      = FormatMs(w.P99Ms);
            WMaxText.Text      = FormatMs(w.MaxMs);
            WOver100Text.Text  = FormatCount(w.Over100Ms,  w.SampleCount);
            WOver250Text.Text  = FormatCount(w.Over250Ms,  w.SampleCount);
            WOver1000Text.Text = FormatCount(w.Over1000Ms, w.SampleCount);

            var s = snap.Session;
            SMeanText.Text     = FormatMs(s.MeanMs);
            SP50Text.Text      = FormatMs(s.P50Ms);
            SP95Text.Text      = FormatMs(s.P95Ms);
            SP99Text.Text      = FormatMs(s.P99Ms);
            SMaxText.Text      = FormatMs(s.MaxMs);
            SOver100Text.Text  = FormatCount(s.Over100Ms,  s.SampleCount);
            SOver250Text.Text  = FormatCount(s.Over250Ms,  s.SampleCount);
            SOver1000Text.Text = FormatCount(s.Over1000Ms, s.SampleCount);

            // Status dot colour driven by the window P95 (current responsiveness).
            Color dot;
            if      (w.P95Ms <  50) dot = Color.FromRgb(0x4E, 0xC9, 0xB0); // green
            else if (w.P95Ms < 200) dot = Color.FromRgb(0xFF, 0xC6, 0x6D); // amber
            else                    dot = Color.FromRgb(0xF4, 0x47, 0x47); // red
            StatusDot.Fill = new SolidColorBrush(dot);
        }

        private static string FormatMs(double ms)
            => ms == 0 ? "–" : $"{ms:F1} ms";

        private static string FormatCount(long count, long total)
        {
            if (total == 0) return "–";
            return $"{count:N0} ({100.0 * count / total:F1}%)";
        }

        private static string FormatCount(int count, int total)
            => FormatCount((long)count, (long)total);
    }
}

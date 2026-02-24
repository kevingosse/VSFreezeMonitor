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
            if (service == null)
            {
                DisabledOverlay.Visibility = Visibility.Visible;
                return;
            }
            DisabledOverlay.Visibility = Visibility.Collapsed;
            _service = service;
            _jtf = jtf;
            service.SnapshotUpdated += OnSnapshotUpdated;
        }

        internal void SetDownloadStatus(string status)
        {
            DownloadProgressText.Visibility = status != null ? Visibility.Visible : Visibility.Collapsed;
            if (status != null) DownloadProgressText.Text = status;
        }

        internal void Disable()
        {
            if (_service != null)
            {
                _service.SnapshotUpdated -= OnSnapshotUpdated;
                _service = null;
            }
            DisabledOverlay.Visibility = Visibility.Visible;
        }

        internal void InitializeProfiler(ProfilerController controller)
        {
            if (controller == null) return;
            ProfilerStatusText.Text = controller.CurrentStatus;
            controller.StatusChanged += status =>
                _ = _jtf.RunAsync(async () =>
                {
                    await _jtf.SwitchToMainThreadAsync();
                    ProfilerStatusText.Text = status;
                });
            controller.SnapshotSaved += entry =>
                _ = _jtf.RunAsync(async () =>
                {
                    await _jtf.SwitchToMainThreadAsync();
                    SnapshotListBox.Text = SnapshotListBox.Text.Length == 0
                        ? entry
                        : entry + "\n" + SnapshotListBox.Text;
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
            WMaxText.Text      = FormatMs(w.MaxMs);
            WOver100Text.Text  = FormatCount(w.Over100Ms,  w.SampleCount);
            WOver250Text.Text  = FormatCount(w.Over250Ms,  w.SampleCount);
            WOver1000Text.Text = FormatCount(w.Over1000Ms, w.SampleCount);

            var s = snap.Session;
            SMaxText.Text      = FormatMs(s.MaxMs);
            SOver100Text.Text  = FormatCount(s.Over100Ms,  s.SampleCount);
            SOver250Text.Text  = FormatCount(s.Over250Ms,  s.SampleCount);
            SOver1000Text.Text = FormatCount(s.Over1000Ms, s.SampleCount);

            // Status dot colour driven by the window Max (current responsiveness).
            Color dot;
            if      (w.MaxMs <  50) dot = Color.FromRgb(0x4E, 0xC9, 0xB0); // green
            else if (w.MaxMs < 200) dot = Color.FromRgb(0xFF, 0xC6, 0x6D); // amber
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

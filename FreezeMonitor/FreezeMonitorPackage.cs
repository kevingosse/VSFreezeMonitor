using Microsoft.VisualStudio.Shell;
using System;
using System.Runtime.InteropServices;
using System.Threading;
using JetBrains.Profiler.SelfApi;
using Microsoft.VisualStudio.Threading;
using Task = System.Threading.Tasks.Task;

namespace FreezeMonitor
{
    /// <summary>
    /// This is the class that implements the package exposed by this assembly.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The minimum requirement for a class to be considered a valid package for Visual Studio
    /// is to implement the IVsPackage interface and register itself with the shell.
    /// This package uses the helper classes defined inside the Managed Package Framework (MPF)
    /// to do it: it derives from the Package class that provides the implementation of the
    /// IVsPackage interface and uses the registration attributes defined in the framework to
    /// register itself and its components with the shell. These attributes tell the pkgdef creation
    /// utility what data to put into .pkgdef file.
    /// </para>
    /// <para>
    /// To get loaded into VS, the package must be referred by &lt;Asset Type="Microsoft.VisualStudio.VsPackage" ...&gt; in .vsixmanifest file.
    /// </para>
    /// </remarks>
    [PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
    [Guid(PackageGuidString)]
    [ProvideMenuResource("Menus.ctmenu", 1)]
    [ProvideToolWindow(typeof(MetricsToolWindow))]
    [ProvideToolWindow(typeof(SettingsToolWindow))]
    [ProvideOptionPage(typeof(ProfilerOptions),
        "Freeze Monitor", "Profiler", 0, 0, true)]
    public sealed class FreezeMonitorPackage : AsyncPackage
    {
        public const string PackageGuidString = "e3de9e0e-d97a-4b32-a475-00340bf94ea0";

        internal MetricsService MetricsService { get; private set; }
        internal ProfilerController ProfilerController { get; private set; }

        private bool _dotTraceInitialized;

        // All fired on the UI thread.
        internal event Action MonitoringStarted;
        internal event Action MonitoringStopped;
        internal event Action<string> DownloadStatusChanged;

        private async Task EnsureDotTraceInitAsync(CancellationToken cancellationToken)
        {
            if (_dotTraceInitialized) return;
            var progress = new CoalescingProgress(JoinableTaskFactory,
                pct => DownloadStatusChanged?.Invoke($"Downloading profiler tools... {pct}%"));
            await DotTrace.InitAsync(cancellationToken, progress);
            _dotTraceInitialized = true;
        }

        internal async Task StartMonitoringAsync()
        {
            if (MetricsService != null) return;

            await EnsureDotTraceInitAsync(CancellationToken.None);
            await JoinableTaskFactory.SwitchToMainThreadAsync();
            DownloadStatusChanged?.Invoke(null); // clear any download message

            var options = (ProfilerOptions)GetDialogPage(typeof(ProfilerOptions));
            MetricsService = new MetricsService();
            MetricsService.Start();

            ProfilerController = new ProfilerController(MetricsService, options, JoinableTaskFactory);
            ProfilerController.Start();

            MonitoringStarted?.Invoke();
        }

        internal async Task StopMonitoringAsync()
        {
            if (MetricsService == null) return;

            if (ProfilerController != null)
            {
                await ProfilerController.StopAsync().ConfigureAwait(false);
                ProfilerController = null;
            }
            await MetricsService.StopAsync().ConfigureAwait(false);
            MetricsService.Dispose();
            MetricsService = null;

            await JoinableTaskFactory.SwitchToMainThreadAsync();
            MonitoringStopped?.Invoke();
        }

        protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
        {
            // Read options on the UI thread first so we know whether to start anything.
            await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
            var options = (ProfilerOptions)GetDialogPage(typeof(ProfilerOptions));
            bool enabled = options.ProfilingMode != ProfilingMode.Off;

            if (enabled)
            {
                await EnsureDotTraceInitAsync(cancellationToken);
                await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

                // Start must run on the UI thread so UiThreadSampler can capture its Dispatcher.
                MetricsService = new MetricsService();
                MetricsService.Start();

                ProfilerController = new ProfilerController(MetricsService, options, JoinableTaskFactory);
                ProfilerController.Start();
            }

            await MetricsToolWindowCommand.InitializeAsync(this);
            await SettingsToolWindowCommand.InitializeAsync(this);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                ProfilerController?.Dispose();
                MetricsService?.Dispose();
            }
            base.Dispose(disposing);
        }

        // IProgress<double> implementation that coalesces rapid callbacks: only one UI
        // dispatch is in-flight at a time, always showing the most recent value.
        private sealed class CoalescingProgress : IProgress<double>
        {
            private readonly JoinableTaskFactory _jtf;
            private readonly Action<int> _report;
            private int _latest;
            private int _pending;

            public CoalescingProgress(JoinableTaskFactory jtf, Action<int> report)
            {
                _jtf = jtf;
                _report = report;
            }

            void IProgress<double>.Report(double value)
            {
                Volatile.Write(ref _latest, (int)value);
                if (Interlocked.Exchange(ref _pending, 1) == 0)
                {
                    _ = _jtf.RunAsync(async () =>
                    {
                        await _jtf.SwitchToMainThreadAsync();
                        Interlocked.Exchange(ref _pending, 0);
                        _report(Volatile.Read(ref _latest));
                    });
                }
            }
        }
    }
}

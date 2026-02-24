using Microsoft.VisualStudio.Shell;
using System;
using System.Runtime.InteropServices;
using System.Threading;
using JetBrains.Profiler.SelfApi;
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

        // Both fired on the UI thread.
        internal event Action MonitoringStarted;
        internal event Action MonitoringStopped;

        internal async Task StartMonitoringAsync()
        {
            if (MetricsService != null) return;

            if (!_dotTraceInitialized)
            {
                await DotTrace.InitAsync(CancellationToken.None);
                _dotTraceInitialized = true;
            }
            await JoinableTaskFactory.SwitchToMainThreadAsync();

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
                // DotTrace.InitAsync may leave us on a background thread.
                await DotTrace.InitAsync(cancellationToken);
                _dotTraceInitialized = true;
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
    }
}

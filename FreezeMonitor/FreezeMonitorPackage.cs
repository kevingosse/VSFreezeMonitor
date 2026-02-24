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

        protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
        {
            await DotTrace.InitAsync(cancellationToken);

            await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

            // Start must run on the UI thread so UiThreadSampler can capture its Dispatcher.
            MetricsService = new MetricsService();
            MetricsService.Start();

            ProfilerController = new ProfilerController(
                MetricsService,
                (ProfilerOptions)GetDialogPage(typeof(ProfilerOptions)));
            ProfilerController.Start();

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

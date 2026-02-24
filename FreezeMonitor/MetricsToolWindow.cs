using System;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio.Shell;

namespace FreezeMonitor
{
    [Guid(MetricsToolWindow.WindowGuidString)]
    public class MetricsToolWindow : ToolWindowPane
    {
        public const string WindowGuidString = "8d4b14db-1825-4b72-b3cd-77bfb77f4d22";

        public MetricsToolWindow() : base(null)
        {
            Caption = "Freeze Monitor";
            Content = new MetricsToolWindowControl();
        }

        public override void OnToolWindowCreated()
        {
            base.OnToolWindowCreated();
            var pkg  = (FreezeMonitorPackage)Package;
            var ctrl = (MetricsToolWindowControl)Content;
            ctrl.Initialize(pkg.MetricsService, pkg.JoinableTaskFactory);
            ctrl.InitializeProfiler(pkg.ProfilerController);

            pkg.MonitoringStarted += () =>
            {
                ctrl.Initialize(pkg.MetricsService, pkg.JoinableTaskFactory);
                ctrl.InitializeProfiler(pkg.ProfilerController);
            };
            pkg.MonitoringStopped += () => ctrl.Disable();
            pkg.DownloadStatusChanged += status => ctrl.SetDownloadStatus(status);
        }
    }
}

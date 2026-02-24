using System;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio.Shell;

namespace FreezeMonitor
{
    [Guid(SettingsToolWindow.WindowGuidString)]
    public class SettingsToolWindow : ToolWindowPane
    {
        public const string WindowGuidString = "f8e1c2a3-6b4d-4f7e-9c1a-2b5e8d3f7a0b";

        public SettingsToolWindow() : base(null)
        {
            Caption = "Freeze Monitor Settings";
            Content = new SettingsToolWindowControl();
        }

        public override void OnToolWindowCreated()
        {
            base.OnToolWindowCreated();
            var pkg  = (FreezeMonitorPackage)Package;
            var ctrl = (SettingsToolWindowControl)Content;
            ctrl.Initialize(pkg);
        }
    }
}

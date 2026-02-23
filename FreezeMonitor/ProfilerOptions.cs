using System.ComponentModel;
using Microsoft.VisualStudio.Shell;

namespace FreezeMonitor;

[DesignerCategory("")]
internal sealed class ProfilerOptions : DialogPage
{
    [Category("Profiler")]
    [DisplayName("Wait for solution load")]
    [Description(
        "When enabled, automatic profiling will not start until the solution is " +
        "fully loaded. Disable to profile from the moment the package initialises.")]
    public bool WaitForSolutionLoad { get; set; } = true;
}

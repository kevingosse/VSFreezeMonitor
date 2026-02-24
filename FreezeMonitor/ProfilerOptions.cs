using System.ComponentModel;
using Microsoft.VisualStudio.Shell;

namespace FreezeMonitor;

internal enum ProfilingMode
{
    Off,
    OnlyWhenSolutionLoaded,
    AlwaysOn,
}

[DesignerCategory("")]
internal sealed class ProfilerOptions : DialogPage
{
    [Category("Profiler")]
    [DisplayName("Profiling mode")]
    [Description(
        "Off: automatic profiling is disabled. " +
        "OnlyWhenSolutionLoaded: profiling starts only after the solution is fully loaded. " +
        "AlwaysOn: profiling starts as soon as the package initialises.")]
    public ProfilingMode ProfilingMode { get; set; } = ProfilingMode.OnlyWhenSolutionLoaded;

    [Category("Profiler")]
    [DisplayName("Delay before profiling (seconds)")]
    [Description(
        "Minimum number of seconds the UI must be unresponsive before automatic " +
        "profiling starts. Increase to avoid capturing short freezes.")]
    public int StartDelaySeconds { get; set; } = 1;

    [Category("Profiler")]
    [DisplayName("Snapshot folder")]
    [Description(
        "Folder where DotTrace snapshots are saved. Leave empty to use the default " +
        "location: %LOCALAPPDATA%\\FreezeMonitor\\Snapshots.")]
    public string SnapshotFolder { get; set; } = "";
}

using System.ComponentModel;
using Microsoft.VisualStudio.Shell;

namespace FreezeMonitor;

internal enum ProfilingMode
{
    Off,
    OnlyThisSession,
    AlwaysOn,
}

[DesignerCategory("")]
internal sealed class ProfilerOptions : DialogPage
{
    [Category("Profiler")]
    [DisplayName("Profiling mode")]
    [Description(
        "Off: automatic profiling is disabled. " +
        "OnlyThisSession: profiling is enabled for this VS session only (resets to Off on restart). " +
        "AlwaysOn: profiling starts as soon as the package initialises.")]
    public ProfilingMode ProfilingMode { get; set; } = ProfilingMode.Off;

    public override void SaveSettingsToStorage()
    {
        // OnlyThisSession must not persist — write Off to storage while keeping
        // the in-memory value so the watchdog stays active for this session.
        var saved = ProfilingMode;
        if (saved == ProfilingMode.OnlyThisSession)
            ProfilingMode = ProfilingMode.Off;
        base.SaveSettingsToStorage();
        ProfilingMode = saved;
    }

    [Category("Profiler")]
    [DisplayName("Delay before profiling (seconds)")]
    [Description(
        "Minimum number of seconds the UI must be unresponsive before automatic " +
        "profiling starts. Increase to avoid capturing short freezes.")]
    public int StartDelaySeconds { get; set; } = 5;

    [Category("Profiler")]
    [DisplayName("Snapshot folder")]
    [Description(
        "Folder where DotTrace snapshots are saved. Leave empty to use the default " +
        "location: %LOCALAPPDATA%\\FreezeMonitor\\Snapshots.")]
    public string SnapshotFolder { get; set; } = "";
}

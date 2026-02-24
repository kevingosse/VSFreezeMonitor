using System;
using System.Windows;
using System.Windows.Controls;
using WinForms = System.Windows.Forms;

namespace FreezeMonitor
{
    public partial class SettingsToolWindowControl : UserControl
    {
        private FreezeMonitorPackage _package;

        public SettingsToolWindowControl()
        {
            InitializeComponent();
        }

        internal void Initialize(FreezeMonitorPackage package)
        {
            _package = package;
            LoadSettings();
        }

        private ProfilerOptions Options
            => (ProfilerOptions)_package.GetDialogPage(typeof(ProfilerOptions));

        private static readonly string DefaultSnapshotFolder = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FreezeMonitor", "Snapshots");

        private void LoadSettings()
        {
            var opts = Options;
            ModeOff.IsChecked = opts.ProfilingMode == ProfilingMode.Off;
            ModeOnlyWhenSolutionLoaded.IsChecked = opts.ProfilingMode == ProfilingMode.OnlyWhenSolutionLoaded;
            ModeAlwaysOn.IsChecked = opts.ProfilingMode == ProfilingMode.AlwaysOn;
            StartDelayTextBox.Text = opts.StartDelaySeconds.ToString();
            SnapshotFolderTextBox.Text = string.IsNullOrWhiteSpace(opts.SnapshotFolder)
                ? DefaultSnapshotFolder
                : opts.SnapshotFolder;
        }

        private void AutoSave(object sender, RoutedEventArgs e) => SaveSettings();

        private void SaveSettings()
        {
            var opts = Options;
            if (ModeOff.IsChecked == true)
                opts.ProfilingMode = ProfilingMode.Off;
            else if (ModeAlwaysOn.IsChecked == true)
                opts.ProfilingMode = ProfilingMode.AlwaysOn;
            else
                opts.ProfilingMode = ProfilingMode.OnlyWhenSolutionLoaded;

            if (int.TryParse(StartDelayTextBox.Text, out int delay) && delay >= 0)
                opts.StartDelaySeconds = delay;

            opts.SnapshotFolder = SnapshotFolderTextBox.Text.Trim();
            opts.SaveSettingsToStorage();
        }

        private void BrowseButton_Click(object sender, RoutedEventArgs e)
        {
            using (var dialog = new WinForms.FolderBrowserDialog())
            {
                if (!string.IsNullOrWhiteSpace(SnapshotFolderTextBox.Text))
                    dialog.SelectedPath = SnapshotFolderTextBox.Text;

                if (dialog.ShowDialog() == WinForms.DialogResult.OK)
                {
                    SnapshotFolderTextBox.Text = dialog.SelectedPath;
                    SaveSettings();
                }
            }
        }
    }
}

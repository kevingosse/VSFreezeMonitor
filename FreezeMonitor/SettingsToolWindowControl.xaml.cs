using System;
using System.Windows;
using System.Windows.Controls;
using WinForms = System.Windows.Forms;

namespace FreezeMonitor
{
    public partial class SettingsToolWindowControl : UserControl
    {
        private FreezeMonitorPackage _package;
        private bool _loading;

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
            _loading = true;
            try
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
            finally
            {
                _loading = false;
            }
        }

        private void AutoSave(object sender, RoutedEventArgs e)
        {
            if (!_loading) SaveSettings();
        }

        private void SaveSettings()
        {
            var opts = Options;
            if (ModeOff.IsChecked == true)
                opts.ProfilingMode = ProfilingMode.Off;
            else if (ModeAlwaysOn.IsChecked == true)
                opts.ProfilingMode = ProfilingMode.AlwaysOn;
            else
                opts.ProfilingMode = ProfilingMode.OnlyWhenSolutionLoaded;

            if (int.TryParse(StartDelayTextBox.Text, out int delay) && delay >= 1)
            {
                opts.StartDelaySeconds = delay;
                StartDelayError.Visibility = Visibility.Collapsed;
            }
            else
            {
                StartDelayError.Text = "Value must be a whole number ≥ 1.";
                StartDelayError.Visibility = Visibility.Visible;
            }

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

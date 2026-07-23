using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace KeyMapper
{
    public partial class EditMappingWindow : Window
    {
        private readonly ShortcutMapping _mapping;
        private readonly bool _isAction;

        public EditMappingWindow(ShortcutMapping mapping, bool isAction)
        {
            InitializeComponent();
            _mapping = mapping;
            _isAction = isAction;

            // Populate fields
            ShortcutTxt.Text = mapping.Shortcut;
            TargetTxt.Text = mapping.Target;
            AllowedProcessTxt.Text = mapping.AllowedProcess;
            ExcludedProcessTxt.Text = mapping.ExcludedProcess;

            if (_isAction)
            {
                TargetLabel.Text = "App Path or Command (e.g. calc.exe)";
                AutoExpandChk.Visibility = Visibility.Collapsed;
                TargetActionButtons.Visibility = Visibility.Visible;
            }
            else
            {
                TargetLabel.Text = "Expanded Text";
                AutoExpandChk.IsChecked = mapping.IsAutoExpand;
                TargetActionButtons.Visibility = Visibility.Collapsed;
            }

            ShortcutTxt.Focus();
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            string shortcut = ShortcutTxt.Text.Trim().ToLower();
            string target = TargetTxt.Text.Trim();
            string allowedApp = AllowedProcessTxt.Text.Trim().ToLower();
            string excludedApp = ExcludedProcessTxt.Text.Trim().ToLower();

            if (string.IsNullOrEmpty(shortcut) || string.IsNullOrEmpty(target))
            {
                MessageBox.Show("Both abbreviation and target text must be specified.", "Invalid Data", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (allowedApp.EndsWith(".exe")) allowedApp = allowedApp.Substring(0, allowedApp.Length - 4);
            if (excludedApp.EndsWith(".exe")) excludedApp = excludedApp.Substring(0, excludedApp.Length - 4);

            // Save values back to the mapping
            _mapping.Shortcut = shortcut;
            _mapping.Target = target;
            _mapping.AllowedProcess = allowedApp;
            _mapping.ExcludedProcess = excludedApp;

            if (!_isAction)
            {
                _mapping.IsAutoExpand = AutoExpandChk.IsChecked ?? false;
            }

            DialogResult = true;
            Close();
        }

        private void InsertEditMacro_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string macro)
            {
                int caretIndex = TargetTxt.CaretIndex;
                TargetTxt.Text = TargetTxt.Text.Insert(caretIndex, macro);
                TargetTxt.CaretIndex = caretIndex + macro.Length;
                TargetTxt.Focus();
            }
        }

        private void PickAllowedApp_Click(object sender, RoutedEventArgs e)
        {
            var picker = new ProcessPickerDialog { Owner = this };
            if (picker.ShowDialog() == true && picker.SelectedProcesses.Count > 0)
            {
                string proc = picker.SelectedProcesses[0];
                if (proc.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    proc = proc.Substring(0, proc.Length - 4);
                }
                AllowedProcessTxt.Text = proc;
            }
        }

        private void BrowseAllowedApp_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Executables (*.exe)|*.exe|All Files (*.*)|*.*",
                Title = "Select Application"
            };
            if (dialog.ShowDialog() == true)
            {
                string name = System.IO.Path.GetFileNameWithoutExtension(dialog.FileName).ToLower();
                AllowedProcessTxt.Text = name;
            }
        }

        private void PickExcludedApp_Click(object sender, RoutedEventArgs e)
        {
            var picker = new ProcessPickerDialog { Owner = this };
            if (picker.ShowDialog() == true && picker.SelectedProcesses.Count > 0)
            {
                string proc = picker.SelectedProcesses[0];
                if (proc.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    proc = proc.Substring(0, proc.Length - 4);
                }
                ExcludedProcessTxt.Text = proc;
            }
        }

        private void BrowseExcludedApp_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Executables (*.exe)|*.exe|All Files (*.*)|*.*",
                Title = "Select Application"
            };
            if (dialog.ShowDialog() == true)
            {
                string name = System.IO.Path.GetFileNameWithoutExtension(dialog.FileName).ToLower();
                ExcludedProcessTxt.Text = name;
            }
        }

        private void PickTargetApp_Click(object sender, RoutedEventArgs e)
        {
            var picker = new ProcessPickerDialog { Owner = this };
            if (picker.ShowDialog() == true && picker.SelectedProcesses.Count > 0)
            {
                string proc = picker.SelectedProcesses[0];
                TargetTxt.Text = ResolveProcessExecutablePath(proc);
            }
        }

        private void BrowseTargetApp_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Executables (*.exe)|*.exe|All Files (*.*)|*.*",
                Title = "Select Application to Launch"
            };
            if (dialog.ShowDialog() == true)
            {
                TargetTxt.Text = dialog.FileName;
            }
        }

        private string ResolveProcessExecutablePath(string processNameWithExe)
        {
            try
            {
                string nameOnly = processNameWithExe;
                if (nameOnly.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    nameOnly = nameOnly.Substring(0, nameOnly.Length - 4);
                }

                var procs = System.Diagnostics.Process.GetProcessesByName(nameOnly);
                if (procs.Length > 0)
                {
                    try
                    {
                        string? path = procs[0].MainModule?.FileName;
                        if (!string.IsNullOrEmpty(path) && System.IO.File.Exists(path))
                        {
                            return path;
                        }
                    }
                    catch { }
                }
            }
            catch { }

            return processNameWithExe;
        }
    }
}

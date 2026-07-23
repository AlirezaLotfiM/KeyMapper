using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;

namespace KeyMapper
{
    public class ProcessItem : INotifyPropertyChanged
    {
        private bool _isChecked;
        public string Name { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;

        public bool IsChecked
        {
            get => _isChecked;
            set
            {
                if (_isChecked != value)
                {
                    _isChecked = value;
                    OnPropertyChanged(nameof(IsChecked));
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public partial class ProcessPickerDialog : Window
    {
        public List<string> SelectedProcesses { get; } = new List<string>();
        private readonly List<ProcessItem> _items = new List<ProcessItem>();
        private readonly ICollectionView _view;

        public ProcessPickerDialog()
        {
            InitializeComponent();

            // Load unique running GUI processes
            try
            {
                var processes = Process.GetProcesses()
                    .Where(p => p.MainWindowHandle != IntPtr.Zero && !string.IsNullOrEmpty(p.MainWindowTitle))
                    .Select(p => p.ProcessName.ToLower())
                    .Distinct()
                    .OrderBy(n => n);

                foreach (var name in processes)
                {
                    _items.Add(new ProcessItem 
                    { 
                        Name = name, 
                        DisplayName = name + ".exe" 
                    });
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to load processes: {ex.Message}");
            }

            _view = CollectionViewSource.GetDefaultView(_items);
            ProcessesList.ItemsSource = _view;

            SearchBox.Focus();
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            string query = SearchBox.Text.Trim().ToLower();
            _view.Filter = item =>
            {
                var pi = (ProcessItem)item;
                return string.IsNullOrEmpty(query) || pi.DisplayName.Contains(query);
            };
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void Add_Click(object sender, RoutedEventArgs e)
        {
            foreach (var item in _items)
            {
                if (item.IsChecked)
                {
                    SelectedProcesses.Add(item.DisplayName);
                }
            }

            DialogResult = true;
            Close();
        }
    }
}

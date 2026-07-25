using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace KeyMapper
{
    public partial class ManageMusicFoldersWindow : Window
    {
        public ManageMusicFoldersWindow()
        {
            InitializeComponent();
            RefreshList();
        }

        private void RefreshList()
        {
            FoldersListBox.ItemsSource = LocalAudioPlayerService.Instance.GetFolders();
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                DragMove();
            }
        }

        private void CloseBtn_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private async void AddFolderBtn_Click(object sender, RoutedEventArgs e)
        {
            using var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Select music folder..."
            };

            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                LocalAudioPlayerService.Instance.AddFolder(dialog.SelectedPath);
                RefreshList();
                await LocalAudioPlayerService.Instance.ScanLibraryAsync();
            }
        }

        private async void RemoveFolder_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string path)
            {
                LocalAudioPlayerService.Instance.RemoveFolder(path);
                RefreshList();
                await LocalAudioPlayerService.Instance.ScanLibraryAsync();
            }
        }
    }
}

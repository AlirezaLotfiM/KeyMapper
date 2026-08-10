using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace KeyMapper
{
    public partial class SupportDonationWindow : Window
    {
        public SupportDonationWindow(Window owner)
        {
            InitializeComponent();
            Owner = owner;
            Topmost = owner.Topmost;
            WalletsList.ItemsSource = MainWindow.DonationWallets;
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                DragMove();
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

        private void CopyAddress_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { Tag: DonationWalletOption wallet })
                return;

            try
            {
                Clipboard.SetText(wallet.Address);
                CopyStatus.Text = $"{wallet.AssetName} address copied. Verify the {wallet.Network} network before sending.";
                CopyStatus.Foreground = (System.Windows.Media.Brush)FindResource("AppAccentBrush");
            }
            catch
            {
                CopyStatus.Text = "Windows could not access the clipboard. Select the address manually and copy it.";
                CopyStatus.Foreground = (System.Windows.Media.Brush)FindResource("AppMutedTextBrush");
            }
        }
    }
}

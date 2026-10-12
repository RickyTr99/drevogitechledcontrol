using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using DrevogitechLedControl.Core;

namespace DrevogitechLedControl.Pages
{
    public partial class SettingsPage : UserControl
    {
        public SettingsPage()
        {
            InitializeComponent();
            StartupSwitch.IsChecked = Controller.StartupEnabled;
        }

        void Startup_Click(object sender, RoutedEventArgs e)
        {
            Controller.StartupEnabled = StartupSwitch.IsChecked == true;
            App.Controller.Settings.Save();
        }

        async void Restore_Click(object sender, RoutedEventArgs e)
        {
            var owner = Window.GetWindow(this);
            if (MessageBox.Show(owner, "Ripristinare i profili del mouse come erano prima delle modifiche fatte con questa app?",
                    "Drevogitech Led Control", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            try
            {
                await App.Controller.RestoreMouseBackupAsync();
                MessageBox.Show(owner, "Profili del mouse ripristinati.", "Drevogitech Led Control");
            }
            catch (Exception ex)
            {
                MessageBox.Show(owner, "Ripristino non riuscito: " + ex.Message, "Drevogitech Led Control", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        void OpenFolder_Click(object sender, RoutedEventArgs e)
        {
            Directory.CreateDirectory(AppSettings.Folder);
            Process.Start(new ProcessStartInfo("explorer.exe", AppSettings.Folder) { UseShellExecute = true });
        }
    }
}

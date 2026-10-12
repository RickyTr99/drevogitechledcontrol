using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using DrevogitechLedControl.Core;
using DrevogitechLedControl.Pages;

namespace DrevogitechLedControl
{
    public partial class MainWindow : Window
    {
        readonly Dictionary<string, UserControl> pages = new Dictionary<string, UserControl>();
        readonly DispatcherTimer statusClear = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        readonly DispatcherTimer devicePoll = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        static readonly Brush Online = new SolidColorBrush(Color.FromRgb(0x6C, 0xCB, 0x5F));
        static readonly Brush Offline = new SolidColorBrush(Color.FromRgb(0x9A, 0x9A, 0x9A));
        bool? kbWasConnected, mouseWasConnected;

        public MainWindow()
        {
            InitializeComponent();
            var c = App.Controller;
            c.Status += (msg, err) => Dispatcher.BeginInvoke(() => ShowStatus(msg, err));
            statusClear.Tick += (s, e) => { statusClear.Stop(); StatusText.Text = ""; };
            devicePoll.Tick += (s, e) => PollDevices();
            devicePoll.Start();
            PollDevices();
            c.ApplyAll();
            Nav.SelectedIndex = 0;
            RestoreWindowBounds(c.Settings.Window);
            Closing += (s, e) => c.Settings.Window = CaptureWindowBounds();
        }

        void RestoreWindowBounds(WindowBounds b)
        {
            if (b == null || b.Width < MinWidth || b.Height < MinHeight) return;
            // ignore positions on a monitor that is no longer connected
            var screen = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
                SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
            var r = new Rect(b.Left, b.Top, b.Width, b.Height);
            Width = b.Width;
            Height = b.Height;
            if (screen.IntersectsWith(r) && screen.Contains(new Point(b.Left + 50, b.Top + 20)))
            {
                WindowStartupLocation = WindowStartupLocation.Manual;
                Left = b.Left;
                Top = b.Top;
            }
            if (b.Maximized) WindowState = WindowState.Maximized;
        }

        WindowBounds CaptureWindowBounds()
        {
            var r = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
            return new WindowBounds
            {
                Left = r.Left, Top = r.Top, Width = r.Width, Height = r.Height,
                Maximized = WindowState == WindowState.Maximized,
            };
        }

        void ShowStatus(string msg, bool error)
        {
            StatusText.Text = msg;
            if (error) StatusText.SetResourceReference(TextBlock.ForegroundProperty, "SystemFillColorCriticalBrush");
            else StatusText.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
            statusClear.Stop();
            statusClear.Start();
        }

        /// <summary>Updates the connection dots and re-applies lighting when a device is plugged in.</summary>
        void PollDevices()
        {
            bool kb = Drevo.IsConnected, mouse = G502.IsConnected;
            KbDot.Fill = kb ? Online : Offline;
            MouseDot.Fill = mouse ? Online : Offline;
            if (kb && kbWasConnected == false) App.Controller.ApplyKeyboard();
            if (mouse && mouseWasConnected == false)
            {
                App.Controller.ConnectMouseAsync();
                App.Controller.ApplyMouseLighting();
            }
            kbWasConnected = kb;
            mouseWasConnected = mouse;
        }

        void Nav_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (Nav.SelectedItem is not ListBoxItem item) return;
            string key = (string)item.Tag;
            if (!pages.TryGetValue(key, out var page))
            {
                page = key switch
                {
                    "keyboard" => new KeyboardPage(),
                    "mouse" => new MousePage(),
                    _ => new SettingsPage(),
                };
                pages[key] = page;
            }
            PageHost.Content = page;
        }
    }
}

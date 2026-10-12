using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DrevogitechLedControl.Core;

namespace DrevogitechLedControl.Controls
{
    public static class Dialogs
    {
        static Window Make(Window owner, string title, UIElement body, out Button ok)
        {
            var w = new Window
            {
                Title = title, Owner = owner, SizeToContent = SizeToContent.WidthAndHeight, ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false,
            };
            ok = new Button { Content = "OK", MinWidth = 110, IsDefault = true, Style = (Style)Application.Current.FindResource("AccentButtonStyle") };
            var cancel = new Button { Content = "Annulla", MinWidth = 110, IsCancel = true, Margin = new Thickness(8, 0, 0, 0) };
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 24, 0, 0) };
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);
            var root = new StackPanel { Margin = new Thickness(24), Width = 360 };
            root.Children.Add(body);
            root.Children.Add(buttons);
            w.Content = root;
            var okRef = ok;
            okRef.Click += (s, e) => w.DialogResult = true;
            return w;
        }

        public static string Prompt(Window owner, string title, string label, string initial)
        {
            var box = new TextBox { Text = initial, Margin = new Thickness(0, 8, 0, 0) };
            var body = new StackPanel();
            body.Children.Add(new TextBlock { Text = label });
            body.Children.Add(box);
            var w = Make(owner, title, body, out _);
            w.Loaded += (s, e) => { box.Focus(); box.SelectAll(); };
            return w.ShowDialog() == true ? box.Text.Trim() : null;
        }

        /// <summary>Asks the user to press a key combination; returns (modifiers, HID usage) or null.</summary>
        public static (byte mods, byte usage)? CaptureKey(Window owner)
        {
            byte mods = 0, usage = 0;
            var display = new TextBlock { Text = "…", FontSize = 22, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 16, 0, 0), HorizontalAlignment = HorizontalAlignment.Center };
            var body = new StackPanel();
            body.Children.Add(new TextBlock { Text = "Premi il tasto o la combinazione (es. Ctrl + C) da assegnare al pulsante del mouse.", TextWrapping = TextWrapping.Wrap });
            body.Children.Add(display);
            var w = Make(owner, "Assegna tasto", body, out var ok);
            ok.IsEnabled = false;
            ok.IsDefault = false;
            w.PreviewKeyDown += (s, e) =>
            {
                var key = e.Key == Key.System ? e.SystemKey : e.Key;
                e.Handled = true;
                mods = HidKeys.CurrentModifiers();
                if (HidKeys.TryGetUsage(key, out var u))
                {
                    usage = u;
                    ok.IsEnabled = true;
                }
                else if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin)
                {
                    usage = 0;
                    ok.IsEnabled = true; // a modifier alone is a valid binding too
                }
                display.Text = HidKeys.Describe(mods, usage).Replace("Tasto: ", "");
            };
            return w.ShowDialog() == true ? (mods, usage) : null;
        }
    }
}

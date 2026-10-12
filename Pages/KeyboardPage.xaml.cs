using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DrevogitechLedControl.Controls;
using DrevogitechLedControl.Core;

namespace DrevogitechLedControl.Pages
{
    public partial class KeyboardPage : UserControl
    {
        record ModeItem(string Name, EffectMode Mode) { public override string ToString() => Name; }

        static readonly ModeItem[] Modes =
        {
            new ModeItem("Statico", EffectMode.Static),
            new ModeItem("Personalizzato per tasto", EffectMode.PerKey),
            new ModeItem("Respiro", EffectMode.Breath),
            new ModeItem("Onda", EffectMode.Stream),
            new ModeItem("Radar", EffectMode.Radar),
            new ModeItem("Memoria (reagisce mentre scrivi)", EffectMode.Memory),
        };

        static readonly string[] StreamDirections = { "Verso destra", "Verso sinistra", "Verso il basso", "Verso l'alto" };
        static readonly string[] RadarDirections = { "Orario", "Antiorario" };

        readonly Controller controller = App.Controller;
        KeyboardSettings S => controller.Settings.Keyboard;
        bool loading;

        public KeyboardPage()
        {
            InitializeComponent();
            foreach (var m in Modes) ModeBox.Items.Add(m);

            foreach (var c in ColorPicker.Swatches.Append(Colors.Black))
            {
                var b = new Border
                {
                    Width = 22, Height = 22, CornerRadius = new CornerRadius(11), Margin = new Thickness(0, 0, 5, 0),
                    Background = new SolidColorBrush(c), Cursor = Cursors.Hand, BorderThickness = new Thickness(1),
                    ToolTip = c == Colors.Black ? "Spento" : ColorUtil.ToHex(c),
                };
                b.SetResourceReference(Border.BorderBrushProperty, "ControlStrokeColorDefaultBrush");
                var col = c;
                b.MouseLeftButtonUp += (s, e) => SetBrush(col);
                QuickSwatches.Children.Add(b);
            }

            BrushButton.Color = Colors.Red;
            BrushButton.ColorChanged += SetBrush;
            Board.PaintFinished += (s, e) => controller.ApplyKeyboard();
            Board.ColorPicked += SetBrush;
            Color1Button.ColorChanged += c => { S.Color1 = c; Changed(); };
            Color2Button.ColorChanged += c => { S.Color2 = c; Changed(); };

            LoadFromSettings();
            RefreshProfiles();
        }

        void LoadFromSettings()
        {
            loading = true;
            ModeBox.SelectedItem = Modes.FirstOrDefault(m => m.Mode == S.Mode) ?? Modes[0];
            Color1Button.Color = S.Color1;
            Color2Button.Color = S.Color2;
            RainbowSwitch.IsChecked = S.Rainbow;
            BrightnessSlider.Value = Drevo.HardwareLevel(S.Brightness);
            SpeedSlider.Value = S.Speed;
            UpdateVisibility();
            if (DirectionBox.Items.Count > 0) DirectionBox.SelectedIndex = Math.Min(S.Direction, DirectionBox.Items.Count - 1);
            loading = false;
            UpdatePreview();
        }

        void SetBrush(Color c)
        {
            Board.BrushColor = c;
            BrushButton.Color = c;
        }

        void Setting_Changed(object sender, RoutedEventArgs e)
        {
            if (loading) return;
            var mode = ((ModeItem)ModeBox.SelectedItem).Mode;
            bool modeChanged = mode != S.Mode;
            S.Mode = mode;
            S.Rainbow = RainbowSwitch.IsChecked == true;
            if (modeChanged)
            {
                loading = true;
                UpdateVisibility();
                DirectionBox.SelectedIndex = DirectionBox.Items.Count > 0 ? 0 : -1;
                loading = false;
            }
            S.Direction = Math.Max(0, DirectionBox.SelectedIndex);
            Changed();
        }

        void Slider_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (BrightnessValue == null || SpeedValue == null) return; // during InitializeComponent
            BrightnessValue.Text = BrightnessSlider.Value == 0 ? "Spenta" : $"{(int)BrightnessSlider.Value} di 6";
            SpeedValue.Text = ((int)SpeedSlider.Value).ToString();
            if (loading) return;
            S.Brightness = (int)Math.Round(BrightnessSlider.Value * 100 / Drevo.HardwareMaxBrightness);
            S.Speed = (int)SpeedSlider.Value;
            Changed();
        }

        void Changed()
        {
            UpdatePreview();
            controller.ApplyKeyboard();
        }

        void UpdateVisibility()
        {
            var m = S.Mode;
            bool perKey = m == EffectMode.PerKey;
            PerKeyTools.Visibility = perKey ? Visibility.Visible : Visibility.Collapsed;
            Board.Editable = perKey;
            Color1Row.Visibility = perKey ? Visibility.Collapsed : Visibility.Visible;
            Color2Row.Visibility = Drevo.UsesSecondColor(m) ? Visibility.Visible : Visibility.Collapsed;
            RainbowRow.Visibility = Drevo.UsesRainbow(m) ? Visibility.Visible : Visibility.Collapsed;
            SpeedRow.Visibility = Drevo.UsesSpeed(m) ? Visibility.Visible : Visibility.Collapsed;

            DirectionBox.Items.Clear();
            var dirs = m == EffectMode.Stream ? StreamDirections : m == EffectMode.Radar ? RadarDirections : Array.Empty<string>();
            foreach (var d in dirs) DirectionBox.Items.Add(d);
            DirectionRow.Visibility = dirs.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        void UpdatePreview()
        {
            bool rainbow = S.Rainbow && Drevo.UsesRainbow(S.Mode);

            if (S.Mode == EffectMode.PerKey)
            {
                Board.KeyColors = S.Keys;
            }
            else
            {
                // Approximation of the built-in effects
                var preview = new Dictionary<byte, Color>();
                foreach (var k in KeyLayout.Keys)
                {
                    double t = k.Rect.X / KeyLayout.Width;
                    Color c = rainbow ? ColorUtil.FromHsv(t * 360, 1, 1)
                        : Drevo.UsesSecondColor(S.Mode) ? ColorUtil.Lerp(S.Color1, S.Color2, t)
                        : S.Color1;
                    preview[k.Code] = c;
                }
                Board.KeyColors = preview;
            }
            Board.Refresh();
        }

        void FillAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var k in KeyLayout.Keys) S.Keys[k.Code] = Board.BrushColor;
            Changed();
        }

        // ---------------- Saved profiles ----------------

        void RefreshProfiles(string select = null)
        {
            ProfileBox.Items.Clear();
            if (Directory.Exists(AppSettings.KeyboardProfilesFolder))
                foreach (var f in Directory.GetFiles(AppSettings.KeyboardProfilesFolder, "*.json").OrderBy(f => f))
                    ProfileBox.Items.Add(Path.GetFileNameWithoutExtension(f));
            ProfileBox.SelectedItem = select;
            if (ProfileBox.SelectedIndex < 0 && ProfileBox.Items.Count > 0) ProfileBox.SelectedIndex = 0;
        }

        string ProfilePath(string name) => Path.Combine(AppSettings.KeyboardProfilesFolder, name + ".json");

        void LoadProfile_Click(object sender, RoutedEventArgs e)
        {
            if (ProfileBox.SelectedItem is not string name) return;
            try
            {
                var loaded = JsonSerializer.Deserialize<KeyboardSettings>(File.ReadAllText(ProfilePath(name)), AppSettings.Json);
                controller.Settings.Keyboard = loaded;
                LoadFromSettings();
                controller.ApplyKeyboard();
            }
            catch (Exception ex)
            {
                MessageBox.Show(Window.GetWindow(this), "Impossibile leggere il profilo: " + ex.Message, "Drevogitech Led Control");
            }
        }

        void SaveProfile_Click(object sender, RoutedEventArgs e)
        {
            var name = Dialogs.Prompt(Window.GetWindow(this), "Salva profilo", "Nome del profilo", ProfileBox.SelectedItem as string ?? "Il mio profilo");
            if (string.IsNullOrWhiteSpace(name)) return;
            foreach (var ch in Path.GetInvalidFileNameChars()) name = name.Replace(ch, '_');
            Directory.CreateDirectory(AppSettings.KeyboardProfilesFolder);
            File.WriteAllText(ProfilePath(name), JsonSerializer.Serialize(S, AppSettings.Json));
            RefreshProfiles(name);
        }

        void DeleteProfile_Click(object sender, RoutedEventArgs e)
        {
            if (ProfileBox.SelectedItem is not string name) return;
            if (MessageBox.Show(Window.GetWindow(this), $"Eliminare il profilo \"{name}\"?", "Drevogitech Led Control",
                    MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            File.Delete(ProfilePath(name));
            RefreshProfiles();
        }
    }
}

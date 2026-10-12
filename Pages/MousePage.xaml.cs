using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DrevogitechLedControl.Controls;
using DrevogitechLedControl.Core;

namespace DrevogitechLedControl.Pages
{
    public partial class MousePage : UserControl
    {
        class ActionItem
        {
            public string Name; public ButtonAction Action; public bool IsCapture;
            public override string ToString() => Name;
        }

        record DpiChoice(int Slot, string Name) { public override string ToString() => Name; }

        // G502 HERO onboard button order (libratbag data + factory bindings read from the mouse)
        static readonly (string name, string hint)[] ButtonNames =
        {
            ("G1 · Clic sinistro", "Non modificabile, per non rischiare di bloccare il mouse"),
            ("G2 · Clic destro", ""),
            ("G3 · Clic della rotella", ""),
            ("G4 · Laterale indietro", "Sul fianco, il più vicino al palmo"),
            ("G5 · Laterale avanti", "Sul fianco, verso la punta"),
            ("G6 · Tasto sniper", "Il tasto per il pollice davanti ai laterali"),
            ("G7", "Di fabbrica: DPI giù"),
            ("G8", "Di fabbrica: DPI su"),
            ("G9", "Di fabbrica: cambio profilo"),
            ("Rotella inclinata a destra", ""),
            ("Rotella inclinata a sinistra", ""),
        };

        static readonly (int ms, string name)[] Rates = { (1, "1000 Hz"), (2, "500 Hz"), (4, "250 Hz"), (8, "125 Hz") };
        static readonly int[] PlaceholderDpi = { 400, 800, 1600, 3200, 6400 };

        readonly Controller controller = App.Controller;
        MouseProfile edit;
        bool loading;
        readonly int[] dpiValues = new int[MouseProfile.MaxDpiLevels];
        readonly bool[] dpiEnabled = new bool[MouseProfile.MaxDpiLevels];
        readonly List<(CheckBox on, Slider slider, TextBox box)> dpiControls = new();

        public MousePage()
        {
            InitializeComponent();
            foreach (var r in Rates) RateBox.Items.Add(r.name);
            SyncSwitch.IsChecked = controller.Settings.SyncMouse;
            BuildZones();
            controller.MouseStateChanged += () => Dispatcher.BeginInvoke(Reload);
            Loaded += async (s, e) => { await controller.ConnectMouseAsync(); Reload(); };
        }

        void Reload()
        {
            var device = controller.MouseDeviceProfile;
            bool connected = device != null;
            NotConnected.Visibility = connected ? Visibility.Collapsed : Visibility.Visible;
            Body.Visibility = SaveBar.Visibility = connected ? Visibility.Visible : Visibility.Collapsed;
            if (!connected) { edit = null; return; }
            Subtitle.Text = $"{controller.MouseName} · profilo integrato {device.Number} in uso";
            edit = device.Clone();
            BuildFromProfile();
            _ = LoadDpiIndicatorAsync();
            Scroller.ScrollToTop();
        }

        // ---------------- Lighting ----------------

        void Balance_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (controller.Settings.SyncBrightnessBalance == (int)BalanceSlider.Value) return;
            controller.Settings.SyncBrightnessBalance = (int)BalanceSlider.Value;
            controller.ApplyMouseLighting();
        }

        async Task LoadDpiIndicatorAsync()
        {
            var on = await controller.GetDpiIndicatorAlwaysOnAsync();
            DpiAlwaysOnRow.Visibility = on == null ? Visibility.Collapsed : Visibility.Visible;
            DpiAlwaysOnSwitch.IsChecked = on == true;
        }

        async void DpiAlwaysOn_Click(object sender, RoutedEventArgs e)
        {
            try { await controller.SetDpiIndicatorAlwaysOnAsync(DpiAlwaysOnSwitch.IsChecked == true); }
            catch (Exception ex)
            {
                MessageBox.Show(Window.GetWindow(this), "Impostazione non riuscita: " + ex.Message, "Drevogitech Led Control");
                await LoadDpiIndicatorAsync();
            }
        }

        void Sync_Click(object sender, RoutedEventArgs e)
        {
            var s = controller.Settings;
            s.SyncMouse = SyncSwitch.IsChecked == true;
            BuildZones();
            controller.ApplyMouseLighting();
        }

        void BuildZones()
        {
            ZonesPanel.Children.Clear();
            ZonesPanel.Visibility = controller.Settings.SyncMouse ? Visibility.Collapsed : Visibility.Visible;
            BalanceRow.Visibility = controller.Settings.SyncMouse ? Visibility.Visible : Visibility.Collapsed;
            BalanceSlider.Value = controller.Settings.SyncBrightnessBalance;
            if (controller.Settings.SyncMouse) return;
            ZonesPanel.Children.Add(ZoneCard("Logo G", "", controller.Settings.MouseLogo));
            ZonesPanel.Children.Add(ZoneCard("Indicatore DPI", "", controller.Settings.MouseDpi));
        }

        UIElement ZoneCard(string title, string icon, ZoneSettings z)
        {
            var effect = new ComboBox { Width = 160 };
            foreach (var n in new[] { "Spento", "Fisso", "Respiro", "Ciclo colori" }) effect.Items.Add(n);
            effect.SelectedIndex = (int)z.Effect;
            var color = new ColorButton { Color = z.Color, Margin = new Thickness(8, 0, 0, 0) };
            var bright = new Slider { Width = 160, Minimum = 0, Maximum = 100, Value = z.Brightness, IsSnapToTickEnabled = true, TickFrequency = 1, VerticalAlignment = VerticalAlignment.Center };
            var brightText = new TextBlock { Width = 40, Text = z.Brightness + "%", Style = (Style)FindResource("SettingLabel"), TextAlignment = TextAlignment.Right };
            var speedLabel = new TextBlock { Text = "Velocità", Margin = new Thickness(16, 0, 6, 0), Style = (Style)FindResource("SettingHint"), VerticalAlignment = VerticalAlignment.Center };
            var speed = new Slider { Width = 120, Minimum = 0, Maximum = 9, Value = z.Speed, IsSnapToTickEnabled = true, TickFrequency = 1, VerticalAlignment = VerticalAlignment.Center };

            void Update()
            {
                color.Visibility = z.Effect is MouseEffect.Fixed or MouseEffect.Breathing ? Visibility.Visible : Visibility.Collapsed;
                speed.Visibility = speedLabel.Visibility = z.Effect is MouseEffect.Breathing or MouseEffect.Cycle ? Visibility.Visible : Visibility.Collapsed;
                bright.IsEnabled = z.Effect != MouseEffect.Off;
            }
            effect.SelectionChanged += (s, e) => { z.Effect = (MouseEffect)effect.SelectedIndex; Update(); controller.ApplyMouseLighting(); };
            color.ColorChanged += c => { z.Color = c; controller.ApplyMouseLighting(); };
            bright.ValueChanged += (s, e) => { z.Brightness = (int)bright.Value; brightText.Text = z.Brightness + "%"; controller.ApplyMouseLighting(); };
            speed.ValueChanged += (s, e) => { z.Speed = (int)speed.Value; controller.ApplyMouseLighting(); };
            Update();

            var controls = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            controls.Children.Add(effect);
            controls.Children.Add(color);
            controls.Children.Add(speedLabel);
            controls.Children.Add(speed);
            controls.Children.Add(new TextBlock { Text = "Luminosità", Margin = new Thickness(16, 0, 6, 0), Style = (Style)FindResource("SettingHint"), VerticalAlignment = VerticalAlignment.Center });
            controls.Children.Add(bright);
            controls.Children.Add(brightText);

            return Row(icon, title, "", controls);
        }

        // ---------------- Profile (DPI, rate, buttons) ----------------

        void BuildFromProfile()
        {
            loading = true;
            for (int i = 0; i < MouseProfile.MaxDpiLevels; i++)
            {
                int v = edit.GetDpi(i);
                dpiEnabled[i] = v > 0;
                dpiValues[i] = v > 0 ? v : PlaceholderDpi[i];
            }
            BuildDpiRows();
            RefreshDpiChoices(edit.DefaultDpiIndex, edit.ShiftDpiIndex);
            RateBox.SelectedIndex = Math.Max(0, Array.FindIndex(Rates, r => r.ms == edit.ReportRateMs));
            BuildButtonRows();
            loading = false;
            UpdateDirty();
        }

        void BuildDpiRows()
        {
            DpiRows.Children.Clear();
            dpiControls.Clear();
            int min = controller.MouseDpiMin, max = controller.MouseDpiMax, step = controller.MouseDpiStep;
            for (int i = 0; i < MouseProfile.MaxDpiLevels; i++)
            {
                int idx = i;
                var on = new CheckBox { Content = $"Livello {i + 1}", Width = 110, IsChecked = dpiEnabled[i], VerticalAlignment = VerticalAlignment.Center };
                var slider = new Slider
                {
                    Minimum = min, Maximum = max, TickFrequency = step, IsSnapToTickEnabled = true, SmallChange = step, LargeChange = step * 10,
                    Value = dpiValues[i], VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 12, 0),
                };
                var box = new TextBox { Width = 90, Text = dpiValues[i].ToString(), VerticalContentAlignment = VerticalAlignment.Center };
                slider.ValueChanged += (s, e) =>
                {
                    dpiValues[idx] = (int)slider.Value;
                    if (box.Text != dpiValues[idx].ToString()) box.Text = dpiValues[idx].ToString();
                    DpiChanged();
                };
                box.LostFocus += (s, e) => CommitBox(idx);
                box.KeyDown += (s, e) => { if (e.Key == System.Windows.Input.Key.Enter) CommitBox(idx); };
                on.Click += (s, e) =>
                {
                    dpiEnabled[idx] = on.IsChecked == true;
                    if (!dpiEnabled.Any(x => x)) { dpiEnabled[idx] = true; on.IsChecked = true; return; } // keep at least one level
                    slider.IsEnabled = box.IsEnabled = dpiEnabled[idx];
                    slider.Opacity = box.Opacity = dpiEnabled[idx] ? 1 : 0.4;
                    DpiChanged();
                };
                slider.IsEnabled = box.IsEnabled = dpiEnabled[i];
                slider.Opacity = box.Opacity = dpiEnabled[i] ? 1 : 0.4;

                var g = new Grid { Margin = new Thickness(0, 4, 0, 4) };
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var unit = new TextBlock { Text = "DPI", Margin = new Thickness(8, 0, 0, 0), Style = (Style)FindResource("SettingHint"), VerticalAlignment = VerticalAlignment.Center };
                Grid.SetColumn(slider, 1); Grid.SetColumn(box, 2); Grid.SetColumn(unit, 3);
                g.Children.Add(on); g.Children.Add(slider); g.Children.Add(box); g.Children.Add(unit);
                DpiRows.Children.Add(g);
                dpiControls.Add((on, slider, box));
            }
        }

        void CommitBox(int i)
        {
            var (_, slider, box) = dpiControls[i];
            int min = controller.MouseDpiMin, max = controller.MouseDpiMax, step = controller.MouseDpiStep;
            if (int.TryParse(box.Text, out int v))
            {
                v = Math.Clamp((int)Math.Round((double)v / step) * step, min, max);
                slider.Value = v;
            }
            box.Text = ((int)slider.Value).ToString();
        }

        void DpiChanged()
        {
            if (loading) return;
            var oldDefault = (DefaultDpiBox.SelectedItem as DpiChoice)?.Slot ?? 0;
            var oldShift = (ShiftDpiBox.SelectedItem as DpiChoice)?.Slot ?? 0;
            loading = true;
            RefreshDpiChoices(oldDefault, oldShift, bySlot: true);
            loading = false;
            Commit();
        }

        /// <summary>Fills the default/shift combos with the enabled levels.</summary>
        void RefreshDpiChoices(int defaultSel, int shiftSel, bool bySlot = false)
        {
            DefaultDpiBox.Items.Clear();
            ShiftDpiBox.Items.Clear();
            var enabled = Enumerable.Range(0, MouseProfile.MaxDpiLevels).Where(i => dpiEnabled[i]).ToList();
            foreach (int slot in enabled)
            {
                DefaultDpiBox.Items.Add(new DpiChoice(slot, $"Livello {slot + 1} · {dpiValues[slot]} DPI"));
                ShiftDpiBox.Items.Add(new DpiChoice(slot, $"Livello {slot + 1} · {dpiValues[slot]} DPI"));
            }
            // stored indexes refer to the compacted list of enabled levels
            int d = bySlot ? enabled.IndexOf(defaultSel) : defaultSel;
            int s = bySlot ? enabled.IndexOf(shiftSel) : shiftSel;
            DefaultDpiBox.SelectedIndex = d >= 0 && d < enabled.Count ? d : 0;
            ShiftDpiBox.SelectedIndex = s >= 0 && s < enabled.Count ? s : 0;
        }

        void Profile_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (!loading) Commit();
        }

        /// <summary>Writes the UI state into the edited profile (not yet into the mouse).</summary>
        void Commit()
        {
            if (edit == null) return;
            // levels are stored in ascending order so "DPI su" always increases the sensitivity
            var slots = Enumerable.Range(0, MouseProfile.MaxDpiLevels).Where(i => dpiEnabled[i]).OrderBy(i => dpiValues[i]).ToList();
            for (int i = 0; i < MouseProfile.MaxDpiLevels; i++) edit.SetDpi(i, i < slots.Count ? dpiValues[slots[i]] : 0);
            int defSlot = (DefaultDpiBox.SelectedItem as DpiChoice)?.Slot ?? slots[0];
            int shiftSlot = (ShiftDpiBox.SelectedItem as DpiChoice)?.Slot ?? slots[0];
            edit.DefaultDpiIndex = Math.Max(0, slots.IndexOf(defSlot));
            edit.ShiftDpiIndex = Math.Max(0, slots.IndexOf(shiftSlot));
            if (RateBox.SelectedIndex >= 0) edit.ReportRateMs = Rates[RateBox.SelectedIndex].ms;
            UpdateDirty();
        }

        static readonly byte[] DpiSpecials = { 0x03, 0x04, 0x05 };

        /// <summary>Warns when several DPI levels exist but no button can switch between them.</summary>
        void UpdateDpiWarning()
        {
            if (edit == null) return;
            bool multiple = dpiEnabled.Count(x => x) > 1;
            bool hasButton = Enumerable.Range(0, edit.ButtonCount)
                .Select(edit.GetButton)
                .Any(b => b.Bytes[0] == 0x90 && DpiSpecials.Contains(b.Bytes[1]));
            DpiWarning.Visibility = multiple && !hasButton ? Visibility.Visible : Visibility.Collapsed;
        }

        void AssignDpiButtons_Click(object sender, RoutedEventArgs e)
        {
            edit.SetButton(8, ButtonAction.Special(0x05)); // G9: DPI a ciclo
            RebuildButtons();
        }

        void FactoryButtons_Click(object sender, RoutedEventArgs e)
        {
            var factory = new[]
            {
                ButtonAction.Mouse(0x01), ButtonAction.Mouse(0x02), ButtonAction.Mouse(0x04), ButtonAction.Mouse(0x08),
                ButtonAction.Mouse(0x10), ButtonAction.Special(0x07), ButtonAction.Special(0x04), ButtonAction.Special(0x03),
                ButtonAction.Special(0x0A), ButtonAction.Special(0x02), ButtonAction.Special(0x01),
            };
            for (int i = 0; i < Math.Min(factory.Length, edit.ButtonCount); i++) edit.SetButton(i, factory[i]);
            RebuildButtons();
        }

        void RebuildButtons()
        {
            loading = true;
            BuildButtonRows();
            loading = false;
            UpdateDirty();
        }

        void BuildButtonRows()
        {
            ButtonRows.Children.Clear();
            int count = Math.Min(edit.ButtonCount, ButtonNames.Length);
            for (int i = 0; i < count; i++)
            {
                int idx = i;
                var combo = new ComboBox { Width = 260, MaxDropDownHeight = 420 };
                FillActions(combo, edit.GetButton(i));
                combo.IsEnabled = i != 0;
                object previous = combo.SelectedItem;
                combo.SelectionChanged += (s, e) =>
                {
                    if (loading || combo.SelectedItem is not ActionItem item) return;
                    if (item.IsCapture)
                    {
                        var key = Dialogs.CaptureKey(Window.GetWindow(this));
                        loading = true;
                        if (key == null) combo.SelectedItem = previous;
                        else
                        {
                            var custom = new ActionItem { Action = ButtonAction.Key(key.Value.mods, key.Value.usage) };
                            custom.Name = custom.Action.Describe();
                            combo.Items.Insert(0, custom);
                            combo.SelectedItem = custom;
                        }
                        loading = false;
                        item = combo.SelectedItem as ActionItem;
                    }
                    previous = combo.SelectedItem;
                    edit.SetButton(idx, item.Action);
                    UpdateDirty();
                };

                var (name, hint) = ButtonNames[i];
                if (i > 0) ButtonRows.Children.Add(new Separator { Margin = new Thickness(0, 4, 0, 4), Opacity = 0.5 });
                ButtonRows.Children.Add(Row(null, name, hint, combo, padding: new Thickness(0, 4, 0, 4)));
            }
        }

        static void FillActions(ComboBox combo, ButtonAction current)
        {
            var presets = ButtonAction.Catalog.Select(p => new ActionItem { Name = p.Group + " · " + p.Name, Action = p.Action }).ToList();
            var match = presets.FirstOrDefault(p => p.Action.SameAs(current));
            if (match == null)
            {
                // keyboard shortcut, macro or other binding: keep it selectable as-is
                match = new ActionItem { Name = current.Describe(), Action = current };
                combo.Items.Add(match);
            }
            foreach (var p in presets) combo.Items.Add(p);
            combo.Items.Add(new ActionItem { Name = "Tastiera · Assegna un tasto…", IsCapture = true });
            combo.SelectedItem = match;
        }

        UIElement Row(string icon, string title, string hint, UIElement right, Thickness? padding = null)
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 150 });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            if (icon != null)
                g.Children.Add(new TextBlock { Text = icon, Style = (Style)FindResource("Icon"), Margin = new Thickness(0, 0, 16, 0) });
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(new TextBlock { Text = title, Style = (Style)FindResource("SettingLabel") });
            if (!string.IsNullOrEmpty(hint)) text.Children.Add(new TextBlock { Text = hint, Style = (Style)FindResource("SettingHint") });
            Grid.SetColumn(text, 1);
            Grid.SetColumn(right, 2);
            g.Children.Add(text);
            g.Children.Add(right);
            if (padding != null) { g.Margin = padding.Value; return g; }
            return new Border { Style = (Style)FindResource("Card"), Child = g };
        }

        // ---------------- Save ----------------

        /// <summary>Settings and buttons area of the profile (excludes the lighting, which is saved automatically).</summary>
        const int EditableBytes = 0xD0;

        void UpdateDirty()
        {
            var device = controller.MouseDeviceProfile;
            bool dirty = edit != null && device != null && !edit.Raw.Take(EditableBytes).SequenceEqual(device.Raw.Take(EditableBytes));
            SaveButton.IsEnabled = RevertButton.IsEnabled = dirty;
            UpdateDpiWarning();
            DirtyText.Text = dirty
                ? "Ci sono modifiche non salvate."
                : "Le modifiche a DPI, polling e pulsanti vengono salvate nella memoria del mouse e funzionano anche senza questa app.";
        }

        async void Save_Click(object sender, RoutedEventArgs e)
        {
            SaveButton.IsEnabled = false;
            try
            {
                await controller.SaveMouseProfileAsync(edit);
                Reload();
                DirtyText.Text = "Salvato nel mouse ✓";
            }
            catch (Exception ex)
            {
                MessageBox.Show(Window.GetWindow(this), "Salvataggio non riuscito: " + ex.Message, "Drevogitech Led Control", MessageBoxButton.OK, MessageBoxImage.Error);
                UpdateDirty();
            }
        }

        void Revert_Click(object sender, RoutedEventArgs e) => Reload();
    }
}

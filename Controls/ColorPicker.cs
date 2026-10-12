using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using DrevogitechLedControl.Core;

namespace DrevogitechLedControl.Controls
{
    /// <summary>Saturation/value square + hue bar + hex field + swatches.</summary>
    public class ColorPicker : StackPanel
    {
        const double W = 268, H = 160;
        public static readonly Color[] Swatches =
        {
            Colors.White, Color.FromRgb(255, 0, 0), Color.FromRgb(255, 96, 0), Color.FromRgb(255, 200, 0),
            Color.FromRgb(0, 255, 0), Color.FromRgb(0, 255, 160), Color.FromRgb(0, 192, 255), Color.FromRgb(0, 0, 255),
            Color.FromRgb(160, 0, 255), Color.FromRgb(255, 0, 160),
        };

        public event Action<Color> ColorChanged;

        double hue, sat, val;
        readonly Border svArea = new Border { Width = W, Height = H, CornerRadius = new CornerRadius(6), ClipToBounds = true, Cursor = Cursors.Cross };
        readonly Canvas svCanvas = new Canvas { Width = W, Height = H };
        readonly Ellipse svThumb = new Ellipse { Width = 14, Height = 14, Stroke = Brushes.White, StrokeThickness = 2, IsHitTestVisible = false };
        readonly Canvas hueCanvas = new Canvas { Width = W, Height = 14, Cursor = Cursors.Hand, Margin = new Thickness(0, 12, 0, 0) };
        readonly Border hueThumb = new Border { Width = 8, Height = 18, CornerRadius = new CornerRadius(4), BorderBrush = Brushes.White, BorderThickness = new Thickness(2), IsHitTestVisible = false };
        readonly Border preview = new Border { Width = 32, Height = 32, CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(1) };
        readonly TextBox hex = new TextBox { Width = 120, Margin = new Thickness(10, 0, 0, 0), VerticalContentAlignment = VerticalAlignment.Center };
        bool updating;

        public ColorPicker()
        {
            Width = W;
            preview.SetResourceReference(Border.BorderBrushProperty, "CardStrokeColorDefaultBrush");

            var white = new LinearGradientBrush(Colors.White, Color.FromArgb(0, 255, 255, 255), 0);
            var black = new LinearGradientBrush(Color.FromArgb(0, 0, 0, 0), Colors.Black, 90);
            svCanvas.Children.Add(new Rectangle { Width = W, Height = H, Fill = white });
            svCanvas.Children.Add(new Rectangle { Width = W, Height = H, Fill = black });
            svCanvas.Children.Add(svThumb);
            svArea.Child = svCanvas;
            svArea.MouseLeftButtonDown += (s, e) => { svArea.CaptureMouse(); PickSv(e.GetPosition(svCanvas)); };
            svArea.MouseMove += (s, e) => { if (svArea.IsMouseCaptured) PickSv(e.GetPosition(svCanvas)); };
            svArea.MouseLeftButtonUp += (s, e) => svArea.ReleaseMouseCapture();

            var hueBrush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
            for (int i = 0; i <= 6; i++) hueBrush.GradientStops.Add(new GradientStop(ColorUtil.FromHsv(i * 60, 1, 1), i / 6.0));
            hueCanvas.Children.Add(new Border { Width = W, Height = 14, CornerRadius = new CornerRadius(7), Background = hueBrush });
            Canvas.SetTop(hueThumb, -2);
            hueCanvas.Children.Add(hueThumb);
            hueCanvas.MouseLeftButtonDown += (s, e) => { hueCanvas.CaptureMouse(); PickHue(e.GetPosition(hueCanvas).X); };
            hueCanvas.MouseMove += (s, e) => { if (hueCanvas.IsMouseCaptured) PickHue(e.GetPosition(hueCanvas).X); };
            hueCanvas.MouseLeftButtonUp += (s, e) => hueCanvas.ReleaseMouseCapture();

            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 14, 0, 0) };
            row.Children.Add(preview);
            row.Children.Add(hex);
            hex.TextChanged += (s, e) =>
            {
                if (!updating && ColorUtil.TryParse(hex.Text, out var c)) SetColor(c, raise: true, updateHex: false);
            };

            var swatches = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
            foreach (var c in Swatches)
            {
                var b = new Border
                {
                    Width = 22, Height = 22, CornerRadius = new CornerRadius(11), Margin = new Thickness(0, 0, 4.5, 0),
                    Background = new SolidColorBrush(c), Cursor = Cursors.Hand, BorderThickness = new Thickness(1),
                    ToolTip = ColorUtil.ToHex(c),
                };
                b.SetResourceReference(Border.BorderBrushProperty, "CardStrokeColorDefaultBrush");
                var col = c;
                b.MouseLeftButtonUp += (s, e) => SetColor(col, raise: true);
                swatches.Children.Add(b);
            }

            Children.Add(svArea);
            Children.Add(hueCanvas);
            Children.Add(row);
            Children.Add(swatches);
        }

        public Color Color => ColorUtil.FromHsv(hue, sat, val);

        public void SetColor(Color c, bool raise = false, bool updateHex = true)
        {
            ColorUtil.ToHsv(c, out double h, out double s, out double v);
            // keep the current hue for greys, otherwise the hue bar would jump to red
            if (s > 0 && v > 0) hue = h;
            sat = s; val = v;
            UpdateVisuals(updateHex);
            if (raise) ColorChanged?.Invoke(Color);
        }

        void PickSv(Point p)
        {
            sat = Math.Clamp(p.X / W, 0, 1);
            val = 1 - Math.Clamp(p.Y / H, 0, 1);
            UpdateVisuals(true);
            ColorChanged?.Invoke(Color);
        }

        void PickHue(double x)
        {
            hue = Math.Clamp(x / W, 0, 1) * 359.9;
            UpdateVisuals(true);
            ColorChanged?.Invoke(Color);
        }

        void UpdateVisuals(bool updateHex)
        {
            svArea.Background = new SolidColorBrush(ColorUtil.FromHsv(hue, 1, 1));
            Canvas.SetLeft(svThumb, sat * W - 7);
            Canvas.SetTop(svThumb, (1 - val) * H - 7);
            svThumb.Stroke = val > 0.5 && sat < 0.4 ? Brushes.Black : Brushes.White;
            Canvas.SetLeft(hueThumb, hue / 360 * W - 4);
            var c = Color;
            preview.Background = new SolidColorBrush(c);
            if (updateHex)
            {
                updating = true;
                hex.Text = ColorUtil.ToHex(c);
                updating = false;
            }
        }
    }

    /// <summary>Button showing a color; opens a <see cref="ColorPicker"/> flyout.</summary>
    public class ColorButton : Button
    {
        readonly Border swatch = new Border { Width = 18, Height = 18, CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(1) };
        readonly TextBlock label = new TextBlock { Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        readonly Popup popup;
        readonly ColorPicker picker = new ColorPicker();
        Color color = Colors.White;

        public event Action<Color> ColorChanged;

        public ColorButton()
        {
            // subclasses do not pick up the implicit Fluent Button style on their own
            SetResourceReference(StyleProperty, typeof(Button));
            swatch.SetResourceReference(Border.BorderBrushProperty, "ControlStrokeColorDefaultBrush");
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            sp.Children.Add(swatch);
            sp.Children.Add(label);
            Content = sp;
            MinWidth = 120;
            HorizontalContentAlignment = HorizontalAlignment.Left;

            var card = new Border { Padding = new Thickness(16), CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Child = picker };
            card.SetResourceReference(Border.BackgroundProperty, "SolidBackgroundFillColorQuarternaryBrush");
            card.SetResourceReference(Border.BorderBrushProperty, "CardStrokeColorDefaultBrush");
            card.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 24, ShadowDepth = 6, Opacity = 0.25 };
            popup = new Popup
            {
                Child = new Border { Padding = new Thickness(12), Child = card },
                StaysOpen = false, AllowsTransparency = true, PlacementTarget = this, Placement = PlacementMode.Bottom,
                PopupAnimation = PopupAnimation.Fade,
            };
            picker.ColorChanged += c => { SetColor(c); ColorChanged?.Invoke(c); };
            Click += (s, e) => { picker.SetColor(color); popup.IsOpen = true; };
            UpdateLook();
        }

        public Color Color
        {
            get => color;
            set => SetColor(value);
        }

        void SetColor(Color c)
        {
            color = c;
            UpdateLook();
        }

        void UpdateLook()
        {
            swatch.Background = new SolidColorBrush(color);
            label.Text = ColorUtil.ToHex(color);
        }
    }
}

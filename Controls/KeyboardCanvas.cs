using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using DrevogitechLedControl.Core;

namespace DrevogitechLedControl.Controls
{
    /// <summary>
    /// Draws the keyboard with its LED colors. When editable, left click/drag paints keys with
    /// <see cref="BrushColor"/> and right click picks a key's color.
    /// </summary>
    public class KeyboardCanvas : FrameworkElement
    {
        public IDictionary<byte, Color> KeyColors { get; set; } = new Dictionary<byte, Color>();
        public Color BrushColor { get; set; } = Colors.Red;
        public bool Editable { get; set; }

        public event EventHandler PaintFinished;
        public event Action<Color> ColorPicked;

        bool painting, changed;
        KeyDef hover;

        public KeyboardCanvas()
        {
            Focusable = false;
            SnapsToDevicePixels = true;
        }

        public void Refresh() => InvalidateVisual();

        double Unit => Math.Min(ActualWidth / KeyLayout.Width, ActualHeight / KeyLayout.Height);
        Point Origin => new Point((ActualWidth - Unit * KeyLayout.Width) / 2, (ActualHeight - Unit * KeyLayout.Height) / 2);

        protected override Size MeasureOverride(Size available)
        {
            double w = double.IsInfinity(available.Width) ? 900 : available.Width;
            return new Size(w, w * KeyLayout.Height / KeyLayout.Width);
        }

        Rect KeyRect(KeyDef k)
        {
            double u = Unit; var o = Origin; double pad = Math.Max(1.5, u * 0.06);
            return new Rect(o.X + k.Rect.X * u + pad, o.Y + k.Rect.Y * u + pad, k.Rect.Width * u - 2 * pad, k.Rect.Height * u - 2 * pad);
        }

        KeyDef HitTest(Point p) => KeyLayout.Keys.FirstOrDefault(k => KeyRect(k).Contains(p));

        protected override void OnRender(DrawingContext dc)
        {
            double u = Unit;
            if (u <= 0) return;
            var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            var keycap = new SolidColorBrush(Color.FromRgb(36, 36, 40));
            var outline = new Pen(new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)), 1);
            var hoverPen = new Pen(new SolidColorBrush(Color.FromArgb(200, 255, 255, 255)), 2);
            var plate = new Rect(Origin.X - u * 0.15, Origin.Y - u * 0.15, u * KeyLayout.Width + u * 0.3, u * KeyLayout.Height + u * 0.3);
            dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(18, 18, 20)), null, plate, u * 0.18, u * 0.18);

            foreach (var k in KeyLayout.Keys)
            {
                var r = KeyRect(k);
                if (!KeyColors.TryGetValue(k.Code, out var c)) c = Colors.Black;
                double radius = u * 0.12;
                dc.DrawRoundedRectangle(keycap, outline, r, radius, radius);
                if (c.R + c.G + c.B > 0)
                {
                    // LED glow: solid center fading towards the edges of the keycap
                    var glow = new RadialGradientBrush(Color.FromArgb(255, c.R, c.G, c.B), Color.FromArgb(150, c.R, c.G, c.B))
                    {
                        RadiusX = 0.75, RadiusY = 0.75,
                    };
                    var inner = r; inner.Inflate(-1, -1);
                    dc.DrawRoundedRectangle(glow, null, inner, radius, radius);
                }
                if (Editable && k == hover) dc.DrawRoundedRectangle(null, hoverPen, r, radius, radius);
                if (!string.IsNullOrEmpty(k.Label))
                {
                    var text = new FormattedText(k.Label, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                        new Typeface("Segoe UI Variable Text, Segoe UI"), Math.Max(7, u * 0.22),
                        ColorUtil.Luminance(c) > 150 ? new SolidColorBrush(Color.FromRgb(25, 25, 25)) : Brushes.White, dpi);
                    text.MaxTextWidth = Math.Max(1, r.Width - 2);
                    text.TextAlignment = TextAlignment.Center;
                    dc.DrawText(text, new Point(r.X + 1, r.Y + (r.Height - text.Height) / 2));
                }
            }
        }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            if (!Editable) return;
            painting = true; changed = false;
            CaptureMouse();
            Paint(HitTest(e.GetPosition(this)));
        }

        protected override void OnMouseRightButtonDown(MouseButtonEventArgs e)
        {
            if (!Editable) return;
            var k = HitTest(e.GetPosition(this));
            if (k != null && KeyColors.TryGetValue(k.Code, out var c)) ColorPicked?.Invoke(c);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            var k = HitTest(e.GetPosition(this));
            Cursor = Editable && k != null ? Cursors.Hand : null;
            if (k != hover) { hover = k; InvalidateVisual(); }
            if (painting) Paint(k);
        }

        protected override void OnMouseLeave(MouseEventArgs e)
        {
            hover = null;
            InvalidateVisual();
        }

        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            if (!painting) return;
            painting = false;
            ReleaseMouseCapture();
            if (changed) PaintFinished?.Invoke(this, EventArgs.Empty);
        }

        void Paint(KeyDef k)
        {
            if (k == null) return;
            if (KeyColors.TryGetValue(k.Code, out var old) && old == BrushColor) return;
            KeyColors[k.Code] = BrushColor;
            changed = true;
            InvalidateVisual();
        }
    }
}

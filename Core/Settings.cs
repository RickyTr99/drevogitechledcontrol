using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Media;

namespace DrevogitechLedControl.Core
{
    public enum EffectMode
    {
        Static = 0x01,
        Breath = 0x02,
        Stream = 0x03,
        Memory = 0x0D,
        PerKey = 0x0F,
        Radar = 0x10,
    }

    public enum MouseEffect { Off, Fixed, Breathing, Cycle }

    public class KeyboardSettings
    {
        public EffectMode Mode { get; set; } = EffectMode.Static;
        public Color Color1 { get; set; } = Color.FromRgb(0, 192, 255);
        public Color Color2 { get; set; } = Color.FromRgb(255, 0, 0);
        /// <summary>0-100 %, applied by scaling colors (finer than the keyboard's 6 hardware levels).</summary>
        public int Brightness { get; set; } = 100;
        public int Speed { get; set; } = 5;
        public int Direction { get; set; }
        public bool Rainbow { get; set; }
        public Dictionary<byte, Color> Keys { get; set; } = KeyLayout.Keys.ToDictionary(k => k.Code, k => Colors.White);

        public KeyboardSettings Clone()
        {
            var c = (KeyboardSettings)MemberwiseClone();
            c.Keys = new Dictionary<byte, Color>(Keys);
            return c;
        }
    }

    public class ZoneSettings
    {
        public MouseEffect Effect { get; set; } = MouseEffect.Fixed;
        public Color Color { get; set; } = Color.FromRgb(0, 192, 255);
        public int Brightness { get; set; } = 100;
        /// <summary>0 (slow) - 9 (fast), same scale as the keyboard.</summary>
        public int Speed { get; set; } = 5;

        public ZoneSettings Clone() { return (ZoneSettings)MemberwiseClone(); }
    }

    public class WindowBounds
    {
        public double Left { get; set; }
        public double Top { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public bool Maximized { get; set; }
    }

    public class AppSettings
    {
        public WindowBounds Window { get; set; }
        public KeyboardSettings Keyboard { get; set; } = new KeyboardSettings();
        public ZoneSettings MouseLogo { get; set; } = new ZoneSettings();
        public ZoneSettings MouseDpi { get; set; } = new ZoneSettings();
        public bool SyncMouse { get; set; } = true;
        /// <summary>0 (mouse darker) - 100 (mouse brighter): matches the mouse to the keyboard when synced.</summary>
        public int SyncBrightnessBalance { get; set; } = 50;

        public static string Folder =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DrevogitechLedControl");

        /// <summary>Moves the data folder used before the rename (settings, profiles, mouse backup).</summary>
        public static void MigrateLegacyFolder()
        {
            var legacy = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DrevoControl");
            try
            {
                if (Directory.Exists(legacy) && !Directory.Exists(Folder)) Directory.Move(legacy, Folder);
            }
            catch (IOException) { /* keep using defaults if the folder is locked */ }
        }
        public static string FilePath => Path.Combine(Folder, "impostazioni.json");
        public static string KeyboardProfilesFolder => Path.Combine(Folder, "profili-tastiera");

        public static readonly JsonSerializerOptions Json = new JsonSerializerOptions
        {
            WriteIndented = true,
            Converters = { new ColorConverter(), new JsonStringEnumConverter() },
        };

        public static AppSettings Load()
        {
            try
            {
                if (File.Exists(FilePath))
                    return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Json) ?? new AppSettings();
            }
            catch (Exception) { /* corrupt file: start from defaults */ }
            return new AppSettings();
        }

        public void Save()
        {
            Directory.CreateDirectory(Folder);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Json));
        }

        class ColorConverter : JsonConverter<Color>
        {
            public override Color Read(ref Utf8JsonReader reader, Type t, JsonSerializerOptions o)
            {
                return ColorUtil.Parse(reader.GetString());
            }
            public override void Write(Utf8JsonWriter writer, Color value, JsonSerializerOptions o)
            {
                writer.WriteStringValue(ColorUtil.ToHex(value));
            }
            public override Color ReadAsPropertyName(ref Utf8JsonReader reader, Type t, JsonSerializerOptions o)
            {
                return ColorUtil.Parse(reader.GetString());
            }
            public override void WriteAsPropertyName(Utf8JsonWriter writer, Color value, JsonSerializerOptions o)
            {
                writer.WritePropertyName(ColorUtil.ToHex(value));
            }
        }
    }

    public static class ColorUtil
    {
        public static string ToHex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

        public static Color Parse(string s)
        {
            s = (s ?? "").Trim().TrimStart('#');
            if (s.Length != 6 || !int.TryParse(s, System.Globalization.NumberStyles.HexNumber, null, out int v))
                return Colors.White;
            return Color.FromRgb((byte)(v >> 16), (byte)(v >> 8), (byte)v);
        }

        public static bool TryParse(string s, out Color c)
        {
            s = (s ?? "").Trim().TrimStart('#');
            c = Colors.White;
            if (s.Length != 6 || !int.TryParse(s, System.Globalization.NumberStyles.HexNumber, null, out int v)) return false;
            c = Color.FromRgb((byte)(v >> 16), (byte)(v >> 8), (byte)v);
            return true;
        }

        /// <summary>
        /// Scales a color by a perceptual brightness percentage. LEDs are linear PWM while the
        /// eye is not, so the slider is squared to make each step look evenly spaced.
        /// </summary>
        public static Color Dim(Color c, int percent)
        {
            if (percent >= 100) return c;
            if (percent <= 0) return Colors.Black;
            double f = Math.Pow(percent / 100.0, 2.0);
            return Color.FromRgb(Ch(c.R, f), Ch(c.G, f), Ch(c.B, f));
        }

        static byte Ch(byte v, double f)
        {
            if (v == 0) return 0;
            return (byte)Math.Max(1, Math.Round(v * f));
        }

        public static Color Lerp(Color a, Color b, double t) =>
            Color.FromRgb((byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));

        public static Color FromHsv(double h, double s, double v)
        {
            h = ((h % 360) + 360) % 360;
            double c = v * s, x = c * (1 - Math.Abs((h / 60) % 2 - 1)), m = v - c;
            double r = 0, g = 0, b = 0;
            if (h < 60) { r = c; g = x; } else if (h < 120) { r = x; g = c; } else if (h < 180) { g = c; b = x; }
            else if (h < 240) { g = x; b = c; } else if (h < 300) { r = x; b = c; } else { r = c; b = x; }
            return Color.FromRgb((byte)Math.Round((r + m) * 255), (byte)Math.Round((g + m) * 255), (byte)Math.Round((b + m) * 255));
        }

        public static void ToHsv(Color col, out double h, out double s, out double v)
        {
            double r = col.R / 255.0, g = col.G / 255.0, b = col.B / 255.0;
            double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), d = max - min;
            h = 0;
            if (d > 0)
            {
                if (max == r) h = 60 * (((g - b) / d) % 6);
                else if (max == g) h = 60 * ((b - r) / d + 2);
                else h = 60 * ((r - g) / d + 4);
            }
            if (h < 0) h += 360;
            s = max == 0 ? 0 : d / max;
            v = max;
        }

        public static double Luminance(Color c) => 0.299 * c.R + 0.587 * c.G + 0.114 * c.B;
    }
}

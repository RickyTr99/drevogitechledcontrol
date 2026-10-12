using System.Windows;

namespace DrevogitechLedControl.Core
{
    public class KeyDef
    {
        public string Label { get; }
        public byte Code { get; }     // HID usage ID, also addresses the key's LED
        public Rect Rect { get; }     // in key units (1U = one standard key)

        public KeyDef(string label, byte code, double x, double y, double w = 1, double h = 1)
        {
            Label = label; Code = code; Rect = new Rect(x, y, w, h);
        }
    }

    /// <summary>Tenkeyless ISO layout with Italian legends (Drevo Tyrfing V2, 87 keys).</summary>
    public static class KeyLayout
    {
        public const double Width = 18.25, Height = 6.25;
        public static readonly IReadOnlyList<KeyDef> Keys = Build();

        static List<KeyDef> Build()
        {
            var k = new List<KeyDef>();
            k.Add(new KeyDef("Esc", 0x29, 0, 0));
            for (int i = 0; i < 12; i++)
                k.Add(new KeyDef("F" + (i + 1), (byte)(0x3A + i), 2 + i + (i / 4) * 0.5, 0));
            k.Add(new KeyDef("Stamp", 0x46, 15.25, 0));
            k.Add(new KeyDef("Bloc", 0x47, 16.25, 0));
            k.Add(new KeyDef("Pausa", 0x48, 17.25, 0));

            double y = 1.25;
            k.Add(new KeyDef("\\", 0x35, 0, y));
            for (int i = 0; i < 10; i++) k.Add(new KeyDef(((i + 1) % 10).ToString(), (byte)(0x1E + i), 1 + i, y));
            k.Add(new KeyDef("'", 0x2D, 11, y));
            k.Add(new KeyDef("ì", 0x2E, 12, y));
            k.Add(new KeyDef("⟵", 0x2A, 13, y, 2));
            k.Add(new KeyDef("Ins", 0x49, 15.25, y));
            k.Add(new KeyDef("Home", 0x4A, 16.25, y));
            k.Add(new KeyDef("PgSu", 0x4B, 17.25, y));

            y = 2.25;
            k.Add(new KeyDef("Tab", 0x2B, 0, y, 1.5));
            AddRow(k, "QWERTYUIOP", new byte[] { 0x14, 0x1A, 0x08, 0x15, 0x17, 0x1C, 0x18, 0x0C, 0x12, 0x13 }, 1.5, y);
            k.Add(new KeyDef("è", 0x2F, 11.5, y));
            k.Add(new KeyDef("+", 0x30, 12.5, y));
            k.Add(new KeyDef("Invio", 0x28, 13.75, y, 1.25, 2));
            k.Add(new KeyDef("Canc", 0x4C, 15.25, y));
            k.Add(new KeyDef("Fine", 0x4D, 16.25, y));
            k.Add(new KeyDef("PgGiù", 0x4E, 17.25, y));

            y = 3.25;
            k.Add(new KeyDef("Caps", 0x39, 0, y, 1.75));
            AddRow(k, "ASDFGHJKL", new byte[] { 0x04, 0x16, 0x07, 0x09, 0x0A, 0x0B, 0x0D, 0x0E, 0x0F }, 1.75, y);
            k.Add(new KeyDef("ò", 0x33, 10.75, y));
            k.Add(new KeyDef("à", 0x34, 11.75, y));
            k.Add(new KeyDef("ù", 0x32, 12.75, y));

            y = 4.25;
            k.Add(new KeyDef("Shift", 0xE1, 0, y, 1.25));
            k.Add(new KeyDef("<", 0x64, 1.25, y));
            AddRow(k, "ZXCVBNM", new byte[] { 0x1D, 0x1B, 0x06, 0x19, 0x05, 0x11, 0x10 }, 2.25, y);
            k.Add(new KeyDef(",", 0x36, 9.25, y));
            k.Add(new KeyDef(".", 0x37, 10.25, y));
            k.Add(new KeyDef("-", 0x38, 11.25, y));
            k.Add(new KeyDef("Shift", 0xE5, 12.25, y, 2.75));
            k.Add(new KeyDef("↑", 0x52, 16.25, y));

            y = 5.25;
            k.Add(new KeyDef("Ctrl", 0xE0, 0, y, 1.25));
            k.Add(new KeyDef("Win", 0xE3, 1.25, y, 1.25));
            k.Add(new KeyDef("Alt", 0xE2, 2.5, y, 1.25));
            k.Add(new KeyDef("", 0x2C, 3.75, y, 6.25));
            k.Add(new KeyDef("AltGr", 0xE6, 10, y, 1.25));
            k.Add(new KeyDef("Fn", 0xED, 11.25, y, 1.25));
            k.Add(new KeyDef("Menu", 0x65, 12.5, y, 1.25));
            k.Add(new KeyDef("Ctrl", 0xE4, 13.75, y, 1.25));
            k.Add(new KeyDef("←", 0x50, 15.25, y));
            k.Add(new KeyDef("↓", 0x51, 16.25, y));
            k.Add(new KeyDef("→", 0x4F, 17.25, y));
            return k;
        }

        static void AddRow(List<KeyDef> k, string labels, byte[] codes, double x, double y)
        {
            for (int i = 0; i < codes.Length; i++)
                k.Add(new KeyDef(labels[i].ToString(), codes[i], x + i, y));
        }
    }
}

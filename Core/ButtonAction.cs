using System.Windows.Input;

namespace DrevogitechLedControl.Core
{
    /// <summary>A 4-byte onboard button binding (libratbag: union hidpp20_button_binding).</summary>
    public class ButtonAction
    {
        const byte TypeHid = 0x80, TypeSpecial = 0x90, TypeDisabled = 0xFF;
        const byte HidMouse = 0x01, HidKeyboard = 0x02, HidConsumer = 0x03;

        public byte[] Bytes { get; }
        public ButtonAction(byte[] bytes) { Bytes = bytes; }

        public static ButtonAction Mouse(int mask) => new ButtonAction(new byte[] { TypeHid, HidMouse, (byte)(mask >> 8), (byte)mask });
        public static ButtonAction Special(byte code) => new ButtonAction(new byte[] { TypeSpecial, code, 0xFF, 0xFF });
        public static ButtonAction Consumer(int usage) => new ButtonAction(new byte[] { TypeHid, HidConsumer, (byte)(usage >> 8), (byte)usage });
        public static ButtonAction Key(byte modifiers, byte usage) => new ButtonAction(new byte[] { TypeHid, HidKeyboard, modifiers, usage });
        public static ButtonAction Disabled() => new ButtonAction(new byte[] { TypeDisabled, TypeDisabled, TypeDisabled, TypeDisabled });

        public bool IsKeyboard => Bytes[0] == TypeHid && Bytes[1] == HidKeyboard;
        public byte KeyModifiers => Bytes[2];
        public byte KeyUsage => Bytes[3];

        public bool SameAs(ButtonAction o) => o != null && Bytes.SequenceEqual(o.Bytes);

        public string Describe()
        {
            if (IsKeyboard) return HidKeys.Describe(KeyModifiers, KeyUsage);
            var preset = Catalog.FirstOrDefault(c => c.Action.SameAs(this));
            if (preset != null) return preset.Name;
            if (Bytes[0] == 0x00 || Bytes[0] == 0x01) return "Macro (creata con G HUB)";
            return "Personalizzato (" + BitConverter.ToString(Bytes) + ")";
        }

        public class Preset
        {
            public string Group { get; init; }
            public string Name { get; init; }
            public ButtonAction Action { get; init; }
            public override string ToString() => Name;
        }

        public static readonly IReadOnlyList<Preset> Catalog = new List<Preset>
        {
            new Preset { Group = "Mouse", Name = "Clic sinistro", Action = Mouse(0x0001) },
            new Preset { Group = "Mouse", Name = "Clic destro", Action = Mouse(0x0002) },
            new Preset { Group = "Mouse", Name = "Clic centrale", Action = Mouse(0x0004) },
            new Preset { Group = "Mouse", Name = "Indietro", Action = Mouse(0x0008) },
            new Preset { Group = "Mouse", Name = "Avanti", Action = Mouse(0x0010) },
            new Preset { Group = "Mouse", Name = "Scorri a sinistra", Action = Special(0x01) },
            new Preset { Group = "Mouse", Name = "Scorri a destra", Action = Special(0x02) },
            new Preset { Group = "DPI", Name = "DPI su", Action = Special(0x03) },
            new Preset { Group = "DPI", Name = "DPI giù", Action = Special(0x04) },
            new Preset { Group = "DPI", Name = "DPI a ciclo", Action = Special(0x05) },
            new Preset { Group = "DPI", Name = "DPI predefinito", Action = Special(0x06) },
            new Preset { Group = "DPI", Name = "DPI Shift (sniper)", Action = Special(0x07) },
            new Preset { Group = "Profili", Name = "Profilo successivo", Action = Special(0x08) },
            new Preset { Group = "Profili", Name = "Profilo precedente", Action = Special(0x09) },
            new Preset { Group = "Profili", Name = "Profili a ciclo", Action = Special(0x0A) },
            new Preset { Group = "Profili", Name = "G-Shift", Action = Special(0x0B) },
            new Preset { Group = "Multimedia", Name = "Volume su", Action = Consumer(0x00E9) },
            new Preset { Group = "Multimedia", Name = "Volume giù", Action = Consumer(0x00EA) },
            new Preset { Group = "Multimedia", Name = "Muto", Action = Consumer(0x00E2) },
            new Preset { Group = "Multimedia", Name = "Play / Pausa", Action = Consumer(0x00CD) },
            new Preset { Group = "Multimedia", Name = "Traccia successiva", Action = Consumer(0x00B5) },
            new Preset { Group = "Multimedia", Name = "Traccia precedente", Action = Consumer(0x00B6) },
            new Preset { Group = "Multimedia", Name = "Stop", Action = Consumer(0x00B7) },
            new Preset { Group = "Altro", Name = "Disattivato", Action = Disabled() },
        };
    }

    /// <summary>Converts WPF keys to USB HID keyboard usages (and back to readable names).</summary>
    public static class HidKeys
    {
        public const byte ModCtrl = 0x01, ModShift = 0x02, ModAlt = 0x04, ModWin = 0x08;

        static readonly Dictionary<Key, byte> Map = BuildMap();
        static readonly Dictionary<byte, string> Names = BuildNames();

        static Dictionary<Key, byte> BuildMap()
        {
            var m = new Dictionary<Key, byte>();
            for (int i = 0; i < 26; i++) m[Key.A + i] = (byte)(0x04 + i);
            for (int i = 1; i <= 9; i++) m[Key.D0 + i] = (byte)(0x1E + i - 1);
            m[Key.D0] = 0x27;
            for (int i = 0; i < 12; i++) m[Key.F1 + i] = (byte)(0x3A + i);
            for (int i = 0; i < 12; i++) m[Key.F13 + i] = (byte)(0x68 + i);
            for (int i = 1; i <= 9; i++) m[Key.NumPad0 + i] = (byte)(0x59 + i - 1);
            m[Key.NumPad0] = 0x62;
            m[Key.Enter] = 0x28; m[Key.Escape] = 0x29; m[Key.Back] = 0x2A; m[Key.Tab] = 0x2B; m[Key.Space] = 0x2C;
            m[Key.OemMinus] = 0x2D; m[Key.OemPlus] = 0x2E; m[Key.OemOpenBrackets] = 0x2F; m[Key.Oem6] = 0x30;
            m[Key.Oem5] = 0x31; m[Key.Oem1] = 0x33; m[Key.Oem7] = 0x34; m[Key.Oem3] = 0x35;
            m[Key.OemComma] = 0x36; m[Key.OemPeriod] = 0x37; m[Key.Oem2] = 0x38; m[Key.OemBackslash] = 0x64;
            m[Key.CapsLock] = 0x39; m[Key.PrintScreen] = 0x46; m[Key.Scroll] = 0x47; m[Key.Pause] = 0x48;
            m[Key.Insert] = 0x49; m[Key.Home] = 0x4A; m[Key.PageUp] = 0x4B; m[Key.Delete] = 0x4C; m[Key.End] = 0x4D;
            m[Key.PageDown] = 0x4E; m[Key.Right] = 0x4F; m[Key.Left] = 0x50; m[Key.Down] = 0x51; m[Key.Up] = 0x52;
            m[Key.NumLock] = 0x53; m[Key.Divide] = 0x54; m[Key.Multiply] = 0x55; m[Key.Subtract] = 0x56;
            m[Key.Add] = 0x57; m[Key.Decimal] = 0x63; m[Key.Apps] = 0x65;
            return m;
        }

        static Dictionary<byte, string> BuildNames()
        {
            var n = new Dictionary<byte, string>();
            for (int i = 0; i < 26; i++) n[(byte)(0x04 + i)] = ((char)('A' + i)).ToString();
            for (int i = 0; i < 9; i++) n[(byte)(0x1E + i)] = (i + 1).ToString();
            n[0x27] = "0";
            for (int i = 0; i < 12; i++) n[(byte)(0x3A + i)] = "F" + (i + 1);
            for (int i = 0; i < 12; i++) n[(byte)(0x68 + i)] = "F" + (i + 13);
            for (int i = 0; i < 9; i++) n[(byte)(0x59 + i)] = "Num " + (i + 1);
            n[0x62] = "Num 0";
            var extra = new Dictionary<byte, string>
            {
                [0x28] = "Invio", [0x29] = "Esc", [0x2A] = "Backspace", [0x2B] = "Tab", [0x2C] = "Spazio",
                [0x2D] = "'", [0x2E] = "ì", [0x2F] = "è", [0x30] = "+", [0x31] = "\\", [0x32] = "ù", [0x33] = "ò",
                [0x34] = "à", [0x35] = "\\", [0x36] = ",", [0x37] = ".", [0x38] = "-", [0x39] = "Bloc Maiusc",
                [0x46] = "Stamp", [0x47] = "Bloc Scorr", [0x48] = "Pausa", [0x49] = "Ins", [0x4A] = "Home",
                [0x4B] = "Pag Su", [0x4C] = "Canc", [0x4D] = "Fine", [0x4E] = "Pag Giù", [0x4F] = "→", [0x50] = "←",
                [0x51] = "↓", [0x52] = "↑", [0x53] = "Bloc Num", [0x54] = "Num /", [0x55] = "Num *", [0x56] = "Num -",
                [0x57] = "Num +", [0x63] = "Num ,", [0x64] = "<", [0x65] = "Menu",
            };
            foreach (var kv in extra) n[kv.Key] = kv.Value;
            return n;
        }

        public static bool TryGetUsage(Key key, out byte usage) => Map.TryGetValue(key, out usage);

        public static byte CurrentModifiers()
        {
            byte m = 0;
            if (Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl)) m |= ModCtrl;
            if (Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift)) m |= ModShift;
            if (Keyboard.IsKeyDown(Key.LeftAlt) || Keyboard.IsKeyDown(Key.RightAlt)) m |= ModAlt;
            if (Keyboard.IsKeyDown(Key.LWin) || Keyboard.IsKeyDown(Key.RWin)) m |= ModWin;
            return m;
        }

        public static string Describe(byte modifiers, byte usage)
        {
            var parts = new List<string>();
            // Left (low nibble) and right (high nibble) modifiers are shown the same way
            byte m = (byte)((modifiers & 0x0F) | (modifiers >> 4));
            if ((m & ModCtrl) != 0) parts.Add("Ctrl");
            if ((m & ModShift) != 0) parts.Add("Shift");
            if ((m & ModAlt) != 0) parts.Add("Alt");
            if ((m & ModWin) != 0) parts.Add("Win");
            if (usage != 0) parts.Add(Names.TryGetValue(usage, out var name) ? name : $"Tasto 0x{usage:X2}");
            return "Tasto: " + string.Join(" + ", parts);
        }
    }
}

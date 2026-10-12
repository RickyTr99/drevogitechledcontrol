using System.Windows.Media;

namespace DrevogitechLedControl.Core
{
    /// <summary>
    /// Drevo Tyrfing V2 lighting protocol (VID 0416 / PID A0F8).
    /// Commands are 32-byte output reports (ID 0x06) on the vendor collection of interface 1.
    /// Protocol references: github.com/dennisblokland/DrevoTyrfing and the "dtv2" Python package.
    /// </summary>
    public static class Drevo
    {
        public const ushort VendorId = 0x0416, ProductId = 0xA0F8;
        public const int HardwareMaxBrightness = 6, MaxSpeed = 9;

        static readonly byte[] EffectPrefix = { 0x06, 0xBE, 0x15, 0x00, 0x01, 0x01 };
        static readonly byte[] KeyColorPrefix = { 0x06, 0xBE, 0x19, 0x00, 0x01, 0x01, 0x0E };
        const int KeysPerPacket = 5;
        static readonly object Sync = new object();

        public static bool IsConnected => FindInterface() != null;

        static HidInterfaceInfo FindInterface() =>
            Hid.Enumerate(VendorId, ProductId).FirstOrDefault(i => i.UsagePage == 0x0001 && i.Usage == 0x00 && i.OutputLen == 32);

        public static void Apply(KeyboardSettings s)
        {
            var packets = BuildPackets(s);
            lock (Sync)
            {
                var info = FindInterface() ?? throw new InvalidOperationException("Tastiera Drevo non trovata");
                using var dev = new HidDevice(info);
                foreach (var p in packets) dev.Write(p);
            }
        }

        public static List<byte[]> BuildPackets(KeyboardSettings s)
        {
            // The firmware renders only ~5 distinct steps when colors are dimmed (and shifts their hue),
            // while its 6 hardware levels are evenly spaced: brightness always uses the hardware levels.
            int hw = HardwareLevel(s.Brightness);
            if (s.Mode == EffectMode.PerKey)
                return PerKeyPackets(new Dictionary<byte, Color>(s.Keys), hw);
            bool rainbow = s.Rainbow && UsesRainbow(s.Mode);
            return new List<byte[]> { EffectPacket(s.Mode, s.Color1, s.Color2, hw, s.Speed, s.Direction, rainbow) };
        }

        /// <summary>0-100 % to the keyboard's 0-6 levels (any non-zero value stays lit).</summary>
        public static int HardwareLevel(int percent)
        {
            percent = Math.Clamp(percent, 0, 100);
            return percent == 0 ? 0 : Math.Max(1, (int)Math.Round(percent / 100.0 * HardwareMaxBrightness));
        }

        public static bool UsesRainbow(EffectMode m) => m == EffectMode.Static || m == EffectMode.Stream;
        public static bool UsesSecondColor(EffectMode m) => m == EffectMode.Breath || m == EffectMode.Stream || m == EffectMode.Radar || m == EffectMode.Memory;
        public static bool UsesSpeed(EffectMode m) => m != EffectMode.PerKey && m != EffectMode.Memory;

        static byte[] EffectPacket(EffectMode mode, Color c1, Color c2, int brightness, int speed, int direction, bool rainbow)
        {
            var p = new byte[32];
            EffectPrefix.CopyTo(p, 0);
            p[6] = (byte)mode;
            p[7] = (byte)Math.Clamp(speed, 0, MaxSpeed);
            p[8] = (byte)Math.Clamp(brightness, 0, HardwareMaxBrightness);
            p[9] = (byte)direction;
            p[12] = c1.R; p[13] = c1.G; p[14] = c1.B;
            p[15] = (byte)(rainbow ? 1 : 0);
            p[16] = c2.R; p[17] = c2.G; p[18] = c2.B;
            return p;
        }

        /// <summary>Switches to per-key mode, then sends key colors 5 at a time.</summary>
        static List<byte[]> PerKeyPackets(Dictionary<byte, Color> colors, int brightness)
        {
            var packets = new List<byte[]>();
            var p = new byte[32];
            EffectPrefix.CopyTo(p, 0);
            p[6] = (byte)EffectMode.PerKey;
            p[8] = (byte)brightness;
            p[12] = 0xFF;
            packets.Add(p);

            var entries = colors.ToList();
            for (int i = 0; i < entries.Count; i += KeysPerPacket)
            {
                p = new byte[32];
                KeyColorPrefix.CopyTo(p, 0);
                for (int k = 0; k < KeysPerPacket && i + k < entries.Count; k++)
                {
                    var e = entries[i + k];
                    p[7 + k] = e.Key;
                    p[12 + 4 * k] = e.Value.R;
                    p[13 + 4 * k] = e.Value.G;
                    p[14 + 4 * k] = e.Value.B;
                }
                packets.Add(p);
            }
            return packets;
        }
    }
}

using System.IO;
using System.Windows.Media;

namespace DrevogitechLedControl.Core
{
    /// <summary>
    /// Logitech G502 HERO (046D:C08B) via HID++ 2.0.
    /// Lighting: feature 0x8070 (live). DPI, polling rate, buttons and stored lighting live in the
    /// onboard profiles (feature 0x8100), whose layout follows libratbag's hidpp20 implementation.
    /// </summary>
    public sealed class G502 : IDisposable
    {
        public const ushort VendorId = 0x046D, ProductId = 0xC08B;
        const ushort FeatName = 0x0005, FeatAdjustableDpi = 0x2201, FeatLedControl = 0x1300, FeatLed = 0x8070, FeatOnboard = 0x8100;
        /// <summary>Live lighting zones (feature 0x8070).</summary>
        public const int ZoneDpi = 0, ZoneLogo = 1;
        /// <summary>
        /// LED slots in the stored profile. On the G502 HERO they are in the opposite order to the live
        /// zones (verified on the device: the first slot drives the logo when the profile is (re)loaded).
        /// </summary>
        public const int StoredLedLogo = 0, StoredLedDpi = 1;

        readonly HidppDevice hpp;
        readonly Dictionary<int, Dictionary<int, int>> effectIndexByZone = new Dictionary<int, Dictionary<int, int>>();

        public string Name { get; private set; }
        public int SectorSize { get; private set; }
        public int ButtonCount { get; private set; }
        public int DpiMin { get; private set; } = 100;
        public int DpiMax { get; private set; } = 25600;
        public int DpiStep { get; private set; } = 50;

        G502(HidppDevice hpp) { this.hpp = hpp; }

        public static bool IsConnected => Hid.Enumerate(VendorId, ProductId).Any(i => i.UsagePage == 0xFF00 && i.Usage == 0x02);

        public static G502 Open()
        {
            var hpp = HidppDevice.Open(VendorId, ProductId) ?? throw new InvalidOperationException("Mouse G502 non trovato");
            var m = new G502(hpp);
            try { m.Init(); }
            catch { hpp.Dispose(); throw; }
            return m;
        }

        void Init()
        {
            int len = hpp.Call(FeatName, 0)[4];
            var name = new System.Text.StringBuilder();
            while (name.Length < len)
            {
                var r = hpp.Call(FeatName, 1, (byte)name.Length);
                name.Append(System.Text.Encoding.ASCII.GetString(r, 4, 16));
            }
            Name = name.ToString(0, len);

            var info = hpp.Call(FeatOnboard, 0);
            ButtonCount = info[9];
            SectorSize = (info[11] << 8) | info[12];

            if (hpp.HasFeature(FeatAdjustableDpi))
            {
                var r = hpp.Call(FeatAdjustableDpi, 1, 0);
                // List format: min, then 0xE000|step, then max (big endian words)
                int min = (r[5] << 8) | r[6], step = (r[7] << 8) | r[8], max = (r[9] << 8) | r[10];
                if (min > 0 && (step & 0xE000) == 0xE000 && max > min)
                {
                    DpiMin = min; DpiStep = step & 0x1FFF; DpiMax = max;
                }
            }

            int zones = hpp.Call(FeatLed, 0)[4];
            for (int z = 0; z < zones; z++)
            {
                var map = new Dictionary<int, int>();
                int count = hpp.Call(FeatLed, 1, (byte)z)[7];
                for (int e = 0; e < count; e++)
                {
                    var r = hpp.Call(FeatLed, 2, (byte)z, (byte)e);
                    map[(r[6] << 8) | r[7]] = e;
                }
                effectIndexByZone[z] = map;
            }
        }

        // ---------------- Lighting ----------------

        const int EffOff = 0x00, EffFixed = 0x01, EffCycle = 0x03, EffBreathing = 0x0A;

        /// <summary>Speed 0-9 to an effect period in milliseconds (9 = fastest).</summary>
        static int Period(int speed) => 1000 + (9 - Math.Clamp(speed, 0, 9)) * 2000;

        /// <summary>Encodes a zone setting as effect id + 10 parameter bytes (shared by live and stored formats).</summary>
        static (int effect, byte[] p) Encode(ZoneSettings z)
        {
            var p = new byte[10];
            int pct = Math.Clamp(z.Brightness, 0, 100);
            if (z.Effect == MouseEffect.Off || pct == 0) return (EffOff, p);
            int period = Period(z.Speed);
            switch (z.Effect)
            {
                case MouseEffect.Fixed:
                    var c = ColorUtil.Dim(z.Color, pct);
                    p[0] = c.R; p[1] = c.G; p[2] = c.B;
                    return (EffFixed, p);
                case MouseEffect.Breathing:
                    p[0] = z.Color.R; p[1] = z.Color.G; p[2] = z.Color.B;
                    p[3] = (byte)(period >> 8); p[4] = (byte)period;
                    p[6] = Intensity(pct);
                    return (EffBreathing, p);
                default:
                    p[5] = (byte)(period >> 8); p[6] = (byte)period;
                    p[7] = Intensity(pct);
                    return (EffCycle, p);
            }
        }

        /// <summary>Same perceptual curve as <see cref="ColorUtil.Dim"/>, so all effects share one brightness scale.</summary>
        static byte Intensity(int pct) => (byte)Math.Clamp((int)Math.Round(100 * Math.Pow(pct / 100.0, 2)), 1, 100);

        public void SetZoneLive(int zone, ZoneSettings z)
        {
            var (effect, p) = Encode(z);
            if (!effectIndexByZone.TryGetValue(zone, out var map) || !map.TryGetValue(effect, out int index))
                throw new HidppException("Effetto non supportato da questa zona");
            var args = new byte[13];
            args[0] = (byte)zone;
            args[1] = (byte)index;
            p.CopyTo(args, 2);
            args[12] = 0x01;
            hpp.Call(FeatLed, 3, args);
        }

        public static byte[] EncodeStoredLed(ZoneSettings z)
        {
            var (effect, p) = Encode(z);
            var b = new byte[11];
            b[0] = (byte)effect;
            p.CopyTo(b, 1);
            return b;
        }

        // ---------------- DPI indicator (feature 0x1300, LED 0 = DPI type) ----------------

        const byte DpiLedIndex = 0, NvAlwaysOn = 0x02, NvOnChange = 0x04;

        /// <summary>Whether the DPI indicator LEDs stay lit (stored in the mouse), or null if unsupported.</summary>
        public bool? DpiIndicatorAlwaysOn()
        {
            if (!hpp.HasFeature(FeatLedControl)) return null;
            return hpp.Call(FeatLedControl, 6, DpiLedIndex)[5] == NvAlwaysOn;
        }

        public void SetDpiIndicatorAlwaysOn(bool on) =>
            hpp.Call(FeatLedControl, 7, DpiLedIndex, on ? NvAlwaysOn : NvOnChange);

        // ---------------- Onboard profiles ----------------

        public int ActiveProfileNumber() => hpp.Call(FeatOnboard, 4)[5];

        public int SectorOfProfile(int number)
        {
            var dir = ReadSector(0);
            int i = (number - 1) * 4;
            return (dir[i] << 8) | dir[i + 1];
        }

        public MouseProfile ReadActiveProfile()
        {
            int number = ActiveProfileNumber();
            int sector = SectorOfProfile(number);
            return new MouseProfile(number, sector, ReadSector(sector), ButtonCount);
        }

        public byte[] ReadSector(int sector)
        {
            var data = new byte[SectorSize];
            for (int off = 0; off < SectorSize; off += 16)
            {
                int o = Math.Min(off, SectorSize - 16);
                var r = hpp.Call(FeatOnboard, 5, (byte)(sector >> 8), (byte)sector, (byte)(o >> 8), (byte)o);
                Array.Copy(r, 4, data, o, 16);
            }
            return data;
        }

        /// <summary>Writes a sector with a fresh CRC, then reads it back to verify.</summary>
        public void WriteSector(int sector, byte[] data)
        {
            if (data.Length != SectorSize) throw new ArgumentException("Dimensione settore errata");
            data = (byte[])data.Clone();
            ushort crc = Crc(data, SectorSize - 2);
            data[SectorSize - 2] = (byte)(crc >> 8);
            data[SectorSize - 1] = (byte)crc;

            hpp.Call(FeatOnboard, 6, (byte)(sector >> 8), (byte)sector, 0, 0, (byte)(SectorSize >> 8), (byte)SectorSize);
            for (int off = 0; off < SectorSize; off += 16)
            {
                var chunk = new byte[16];
                Array.Copy(data, off, chunk, 0, Math.Min(16, SectorSize - off));
                hpp.Call(FeatOnboard, 7, chunk);
            }
            hpp.Call(FeatOnboard, 8);

            if (!ReadSector(sector).SequenceEqual(data))
                throw new HidppException("Verifica della memoria del mouse fallita");
        }

        public void SaveProfile(MouseProfile p, bool reload)
        {
            BackupOnce();
            WriteSector(p.Sector, p.Raw);
            if (reload) hpp.Call(FeatOnboard, 3, 0, (byte)p.Number);
        }

        public static string BackupFolder => Path.Combine(AppSettings.Folder, "backup-g502-originale");

        /// <summary>Keeps a copy of the profile directory and profiles as they were before our first write.</summary>
        void BackupOnce()
        {
            if (Directory.Exists(BackupFolder) && Directory.GetFiles(BackupFolder, "s*.bin").Length > 0) return;
            Directory.CreateDirectory(BackupFolder);
            var dir = ReadSector(0);
            File.WriteAllBytes(Path.Combine(BackupFolder, "s0000.bin"), dir);
            for (int i = 0; i + 1 < dir.Length && !(dir[i] == 0xFF && dir[i + 1] == 0xFF); i += 4)
            {
                int s = (dir[i] << 8) | dir[i + 1];
                File.WriteAllBytes(Path.Combine(BackupFolder, $"s{s:X4}.bin"), ReadSector(s));
            }
        }

        /// <summary>Writes back the original profiles saved by <see cref="BackupOnce"/>.</summary>
        public void RestoreBackup()
        {
            var files = Directory.Exists(BackupFolder) ? Directory.GetFiles(BackupFolder, "s*.bin") : new string[0];
            if (files.Length == 0) throw new InvalidOperationException("Nessun backup disponibile");
            foreach (var f in files.OrderBy(f => f))
            {
                int sector = Convert.ToInt32(Path.GetFileNameWithoutExtension(f).Substring(1), 16);
                var data = File.ReadAllBytes(f);
                if (data.Length == SectorSize && Crc(data, SectorSize - 2) == ((data[SectorSize - 2] << 8) | data[SectorSize - 1]))
                    WriteSector(sector, data);
            }
            hpp.Call(FeatOnboard, 3, 0, (byte)ActiveProfileNumber());
        }

        /// <summary>CRC-CCITT (poly 0x1021, init 0xFFFF) as used by Logitech onboard profiles.</summary>
        public static ushort Crc(byte[] data, int length)
        {
            int crc = 0xFFFF;
            for (int i = 0; i < length; i++)
            {
                int t = (crc >> 8) ^ data[i];
                t ^= t >> 4;
                crc = ((crc << 8) ^ (t << 12) ^ (t << 5) ^ t) & 0xFFFF;
            }
            return (ushort)crc;
        }

        public void Dispose() => hpp.Dispose();
    }

    /// <summary>One 256-byte onboard profile (libratbag: union hidpp20_internal_profile).</summary>
    public class MouseProfile
    {
        public const int MaxDpiLevels = 5;
        const int ButtonsOffset = 0x20, LedsOffset = 0xD0;

        public int Number { get; }
        public int Sector { get; }
        public byte[] Raw { get; }
        public int ButtonCount { get; }

        public MouseProfile(int number, int sector, byte[] raw, int buttonCount)
        {
            Number = number; Sector = sector; Raw = raw; ButtonCount = buttonCount;
        }

        public MouseProfile Clone() => new MouseProfile(Number, Sector, (byte[])Raw.Clone(), ButtonCount);

        /// <summary>Polling interval in ms (1 = 1000 Hz).</summary>
        public int ReportRateMs { get => Raw[0]; set => Raw[0] = (byte)value; }
        public int DefaultDpiIndex { get => Raw[1]; set => Raw[1] = (byte)value; }
        public int ShiftDpiIndex { get => Raw[2]; set => Raw[2] = (byte)value; }

        public int GetDpi(int i) => Raw[3 + 2 * i] | (Raw[4 + 2 * i] << 8);
        public void SetDpi(int i, int dpi) { Raw[3 + 2 * i] = (byte)dpi; Raw[4 + 2 * i] = (byte)(dpi >> 8); }

        public ButtonAction GetButton(int i) => new ButtonAction(Raw.Skip(ButtonsOffset + 4 * i).Take(4).ToArray());
        public void SetButton(int i, ButtonAction a) => Array.Copy(a.Bytes, 0, Raw, ButtonsOffset + 4 * i, 4);

        public void SetLed(int zone, byte[] led) => Array.Copy(led, 0, Raw, LedsOffset + 11 * zone, 11);
    }
}

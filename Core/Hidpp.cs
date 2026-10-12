namespace DrevogitechLedControl.Core
{
    public class HidppException : Exception
    {
        public HidppException(string message) : base(message) { }
    }

    /// <summary>
    /// Logitech HID++ 2.0 over the "long" vendor collection (report 0x11, 20 bytes).
    /// Feature indexes are resolved through IRoot (feature 0x0000) and cached.
    /// </summary>
    public sealed class HidppDevice : IDisposable
    {
        const byte LongReport = 0x11, DeviceIndex = 0xFF, SoftwareId = 0x0A;
        readonly HidDevice dev;
        readonly Dictionary<ushort, byte> featureIndexes = new Dictionary<ushort, byte>();

        HidppDevice(HidDevice dev) { this.dev = dev; }

        public static HidppDevice Open(ushort vid, ushort pid)
        {
            var info = Hid.Enumerate(vid, pid).FirstOrDefault(i => i.UsagePage == 0xFF00 && i.Usage == 0x02);
            return info == null ? null : new HidppDevice(new HidDevice(info));
        }

        public bool HasFeature(ushort feature) => FeatureIndex(feature) != 0;

        public byte FeatureIndex(ushort feature)
        {
            if (!featureIndexes.TryGetValue(feature, out byte idx))
            {
                idx = feature == 0 ? (byte)0 : Request(0, 0, (byte)(feature >> 8), (byte)feature)[4];
                featureIndexes[feature] = idx;
            }
            return idx;
        }

        /// <summary>Calls function <paramref name="fn"/> of a feature; returns the full 20-byte response.</summary>
        public byte[] Call(ushort feature, int fn, params byte[] parameters)
        {
            byte idx = FeatureIndex(feature);
            if (idx == 0 && feature != 0) throw new HidppException($"Funzione HID++ 0x{feature:X4} non supportata");
            return Request(idx, fn, parameters);
        }

        byte[] Request(byte featureIndex, int fn, params byte[] parameters)
        {
            var msg = new byte[20];
            msg[0] = LongReport;
            msg[1] = DeviceIndex;
            msg[2] = featureIndex;
            msg[3] = (byte)((fn << 4) | SoftwareId);
            Array.Copy(parameters, 0, msg, 4, Math.Min(parameters.Length, 16));

            lock (dev)
            {
                while (dev.Read(1) != null) { } // drop stale notifications
                dev.Write(msg);
                var deadline = Environment.TickCount64 + 1500;
                while (Environment.TickCount64 < deadline)
                {
                    var r = dev.Read((int)Math.Max(1, deadline - Environment.TickCount64));
                    if (r == null || r.Length < 7 || r[0] != LongReport) continue;
                    if (r[2] == featureIndex && r[3] == msg[3]) return r;
                    if (r[2] == 0xFF && r[3] == featureIndex && r[4] == msg[3])
                        throw new HidppException($"Il mouse ha rifiutato il comando (errore HID++ {r[5]})");
                }
            }
            throw new HidppException("Il mouse non ha risposto");
        }

        public void Dispose() => dev.Dispose();
    }
}

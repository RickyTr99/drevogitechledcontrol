using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace DrevogitechLedControl.Core
{
    public class HidInterfaceInfo
    {
        public string Path;
        public ushort VendorId, ProductId;
        public ushort UsagePage, Usage;
        public int InputLen, OutputLen, FeatureLen;

        public override string ToString()
        {
            return string.Format("UsagePage=0x{0:X4} Usage=0x{1:X2} In={2} Out={3} Feat={4}  {5}",
                UsagePage, Usage, InputLen, OutputLen, FeatureLen, Path);
        }
    }

    /// <summary>Minimal Win32 HID access (hid.dll + setupapi.dll), no external dependencies.</summary>
    public static partial class Hid
    {
        public static List<HidInterfaceInfo> Enumerate(ushort vid, ushort pid)
        {
            var result = new List<HidInterfaceInfo>();
            Guid hidGuid;
            HidD_GetHidGuid(out hidGuid);
            IntPtr set = SetupDiGetClassDevs(ref hidGuid, IntPtr.Zero, IntPtr.Zero, DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
            if (set == new IntPtr(-1)) throw new Win32Exception();
            try
            {
                var ifData = new SP_DEVICE_INTERFACE_DATA();
                ifData.cbSize = Marshal.SizeOf(ifData);
                for (uint i = 0; SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref hidGuid, i, ref ifData); i++)
                {
                    string path = GetInterfacePath(set, ref ifData);
                    if (path == null) continue;
                    using (var h = OpenHandle(path, 0))
                    {
                        if (h.IsInvalid) continue;
                        var attr = new HIDD_ATTRIBUTES();
                        attr.Size = Marshal.SizeOf(attr);
                        if (!HidD_GetAttributes(h, ref attr)) continue;
                        if (attr.VendorID != vid || attr.ProductID != pid) continue;
                        var info = new HidInterfaceInfo { Path = path, VendorId = attr.VendorID, ProductId = attr.ProductID };
                        IntPtr ppd;
                        if (HidD_GetPreparsedData(h, out ppd))
                        {
                            HIDP_CAPS caps;
                            if (HidP_GetCaps(ppd, out caps) == HIDP_STATUS_SUCCESS)
                            {
                                info.UsagePage = caps.UsagePage;
                                info.Usage = caps.Usage;
                                info.InputLen = caps.InputReportByteLength;
                                info.OutputLen = caps.OutputReportByteLength;
                                info.FeatureLen = caps.FeatureReportByteLength;
                            }
                            HidD_FreePreparsedData(ppd);
                        }
                        result.Add(info);
                    }
                }
            }
            finally { SetupDiDestroyDeviceInfoList(set); }
            return result;
        }

        static string GetInterfacePath(IntPtr set, ref SP_DEVICE_INTERFACE_DATA ifData)
        {
            int required;
            SetupDiGetDeviceInterfaceDetail(set, ref ifData, IntPtr.Zero, 0, out required, IntPtr.Zero);
            if (required <= 0) return null;
            IntPtr buf = Marshal.AllocHGlobal(required);
            try
            {
                Marshal.WriteInt32(buf, IntPtr.Size == 8 ? 8 : 6);
                if (!SetupDiGetDeviceInterfaceDetail(set, ref ifData, buf, required, out required, IntPtr.Zero)) return null;
                return Marshal.PtrToStringUni(new IntPtr(buf.ToInt64() + 4));
            }
            finally { Marshal.FreeHGlobal(buf); }
        }

        public static SafeFileHandle OpenHandle(string path, uint access)
        {
            return CreateFile(path, access, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
        }
    }

    /// <summary>An opened HID interface. Uses overlapped I/O so reads can time out.</summary>
    public class HidDevice : IDisposable
    {
        readonly SafeFileHandle handle;
        public readonly HidInterfaceInfo Info;

        public HidDevice(HidInterfaceInfo info)
        {
            Info = info;
            handle = CreateFileW(info.Path, GENERIC_READ | GENERIC_WRITE, 3, IntPtr.Zero, 3, FILE_FLAG_OVERLAPPED, IntPtr.Zero);
            if (handle.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error(), "Impossibile aprire il dispositivo HID");
        }

        /// <summary>Sends a report; data[0] must be the report ID. Pads to the device's output length.</summary>
        public void Write(byte[] data)
        {
            int len = Math.Max(Info.OutputLen, data.Length);
            var buf = new byte[len];
            Array.Copy(data, buf, data.Length);
            if (Transfer(buf, true, 1000) < 0)
            {
                // Fall back to a control-pipe SET_REPORT when the interrupt OUT path is unavailable.
                if (!HidD_SetOutputReport(handle, buf, buf.Length))
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "Scrittura sul dispositivo fallita");
            }
        }

        /// <summary>Reads one input report (including report ID). Returns null on timeout.</summary>
        public byte[] Read(int timeoutMs)
        {
            var buf = new byte[Math.Max(1, Info.InputLen)];
            int n = Transfer(buf, false, timeoutMs);
            if (n <= 0) return null;
            if (n < buf.Length) Array.Resize(ref buf, n);
            return buf;
        }

        int Transfer(byte[] buf, bool write, int timeoutMs)
        {
            IntPtr evt = CreateEventW(IntPtr.Zero, true, false, IntPtr.Zero);
            IntPtr ov = Marshal.AllocHGlobal(IntPtr.Size == 8 ? 32 : 20);
            IntPtr mem = Marshal.AllocHGlobal(buf.Length);
            try
            {
                for (int i = 0; i < (IntPtr.Size == 8 ? 32 : 20); i++) Marshal.WriteByte(ov, i, 0);
                Marshal.WriteIntPtr(ov, IntPtr.Size == 8 ? 24 : 16, evt);
                if (write) Marshal.Copy(buf, 0, mem, buf.Length);
                uint n;
                bool ok = write ? WriteFile(handle, mem, (uint)buf.Length, out n, ov) : ReadFile(handle, mem, (uint)buf.Length, out n, ov);
                if (!ok)
                {
                    if (Marshal.GetLastWin32Error() != ERROR_IO_PENDING) return -1;
                    if (WaitForSingleObject(evt, (uint)timeoutMs) != 0)
                    {
                        CancelIoEx(handle, ov);
                        GetOverlappedResult(handle, ov, out n, true);
                        return -1;
                    }
                    if (!GetOverlappedResult(handle, ov, out n, false)) return -1;
                }
                if (!write) Marshal.Copy(mem, buf, 0, (int)n);
                return (int)n;
            }
            finally
            {
                Marshal.FreeHGlobal(mem);
                Marshal.FreeHGlobal(ov);
                CloseHandle(evt);
            }
        }

        public void Dispose() { handle.Dispose(); }

        const uint GENERIC_READ = 0x80000000, GENERIC_WRITE = 0x40000000, FILE_FLAG_OVERLAPPED = 0x40000000;
        const int ERROR_IO_PENDING = 997;

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern SafeFileHandle CreateFileW(string name, uint access, uint share, IntPtr sa, uint disp, uint flags, IntPtr tmpl);
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool WriteFile(SafeFileHandle h, IntPtr buf, uint n, out uint done, IntPtr ov);
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool ReadFile(SafeFileHandle h, IntPtr buf, uint n, out uint done, IntPtr ov);
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool GetOverlappedResult(SafeFileHandle h, IntPtr ov, out uint done, bool wait);
        [DllImport("kernel32.dll")]
        static extern bool CancelIoEx(SafeFileHandle h, IntPtr ov);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr CreateEventW(IntPtr sa, bool manual, bool initial, IntPtr name);
        [DllImport("kernel32.dll")]
        static extern uint WaitForSingleObject(IntPtr h, uint ms);
        [DllImport("kernel32.dll")]
        static extern bool CloseHandle(IntPtr h);
        [DllImport("hid.dll", SetLastError = true)]
        static extern bool HidD_SetOutputReport(SafeFileHandle h, byte[] buf, int len);
    }
}

namespace DrevogitechLedControl.Core
{
    public static partial class Hid
    {
        const uint DIGCF_PRESENT = 0x2, DIGCF_DEVICEINTERFACE = 0x10;
        const uint FILE_SHARE_READ = 1, FILE_SHARE_WRITE = 2, OPEN_EXISTING = 3;
        const int HIDP_STATUS_SUCCESS = 0x00110000;

        [StructLayout(LayoutKind.Sequential)]
        struct SP_DEVICE_INTERFACE_DATA { public int cbSize; public Guid InterfaceClassGuid; public int Flags; public IntPtr Reserved; }

        [StructLayout(LayoutKind.Sequential)]
        struct HIDD_ATTRIBUTES { public int Size; public ushort VendorID; public ushort ProductID; public ushort VersionNumber; }

        [StructLayout(LayoutKind.Sequential)]
        struct HIDP_CAPS
        {
            public ushort Usage, UsagePage, InputReportByteLength, OutputReportByteLength, FeatureReportByteLength;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)] public ushort[] Reserved;
            public ushort NumberLinkCollectionNodes, NumberInputButtonCaps, NumberInputValueCaps, NumberInputDataIndices,
                NumberOutputButtonCaps, NumberOutputValueCaps, NumberOutputDataIndices,
                NumberFeatureButtonCaps, NumberFeatureValueCaps, NumberFeatureDataIndices;
        }

        [DllImport("hid.dll")] static extern void HidD_GetHidGuid(out Guid g);
        [DllImport("hid.dll")] static extern bool HidD_GetAttributes(SafeFileHandle h, ref HIDD_ATTRIBUTES a);
        [DllImport("hid.dll")] static extern bool HidD_GetPreparsedData(SafeFileHandle h, out IntPtr ppd);
        [DllImport("hid.dll")] static extern bool HidD_FreePreparsedData(IntPtr ppd);
        [DllImport("hid.dll")] static extern int HidP_GetCaps(IntPtr ppd, out HIDP_CAPS caps);

        [DllImport("setupapi.dll", SetLastError = true)]
        static extern IntPtr SetupDiGetClassDevs(ref Guid g, IntPtr enumerator, IntPtr hwnd, uint flags);
        [DllImport("setupapi.dll", SetLastError = true)]
        static extern bool SetupDiEnumDeviceInterfaces(IntPtr set, IntPtr devInfo, ref Guid g, uint index, ref SP_DEVICE_INTERFACE_DATA d);
        [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr set, ref SP_DEVICE_INTERFACE_DATA d, IntPtr detail, int size, out int required, IntPtr devInfo);
        [DllImport("setupapi.dll")] static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr sa, uint disp, uint flags, IntPtr tmpl);
    }
}

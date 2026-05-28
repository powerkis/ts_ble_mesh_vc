using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace TSSmartSchool
{
    public static class PrinterUsbFunc
    {
        [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Auto)]
        public static extern IntPtr SetupDiGetClassDevs(ref Guid ClassGuid, string Enumerator, IntPtr hwndParent, uint Flags);

        [DllImport("setupapi.dll", SetLastError = true)]
        public static extern bool SetupDiEnumDeviceInterfaces(IntPtr DeviceInfoSet, IntPtr DeviceInfoData, ref Guid InterfaceClassGuid, uint MemberIndex, ref SP_DEVICE_INTERFACE_DATA DeviceInterfaceData);

        [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Auto)]
        public static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr DeviceInfoSet, ref SP_DEVICE_INTERFACE_DATA DeviceInterfaceData, IntPtr DeviceInterfaceDetailData, uint DeviceInterfaceDetailDataSize, ref uint RequiredSize, IntPtr DeviceInfoData);

        [DllImport("setupapi.dll", SetLastError = true)]
        public static extern bool SetupDiDestroyDeviceInfoList(IntPtr DeviceInfoSet);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        public static extern IntPtr CreateFile(string lpFileName, uint dwDesiredAccess, uint dwShareMode, IntPtr lpSecurityAttributes, uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool DeviceIoControl(IntPtr hDevice, uint dwIoControlCode, byte[] lpInBuffer, uint nInBufferSize, byte[] lpOutBuffer, uint nOutBufferSize, out uint lpBytesReturned, IntPtr lpOverlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool CloseHandle(IntPtr hObject);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool ReadFile(IntPtr hFile, byte[] lpBuffer, uint nNumberOfBytesToRead, out uint lpNumberOfBytesRead, IntPtr lpOverlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool WriteFile(IntPtr hFile, byte[] lpBuffer, uint nNumberOfBytesToWrite, out uint lpNumberOfBytesWritten, IntPtr lpOverlapped);

        [DllImport("kernel32", CharSet = CharSet.Unicode)]
        public static extern long WritePrivateProfileString(string section, string key, string val, string filePath);

        [DllImport("kernel32", CharSet = CharSet.Unicode)]
        public static extern int GetPrivateProfileString(string section, string key, string def, StringBuilder retVal, int size, string filePath);

        [StructLayout(LayoutKind.Sequential)]
        public struct SP_DEVICE_INTERFACE_DATA
        {
            public uint cbSize;
            public Guid InterfaceClassGuid;
            public uint Flags;
            public IntPtr Reserved;
        }

        public const uint DIGCF_PRESENT = 0x00000002;
        public const uint DIGCF_DEVICEINTERFACE = 0x00000010;
        public const uint GENERIC_READ = 0x80000000;
        public const uint GENERIC_WRITE = 0x40000000;
        public const uint FILE_SHARE_READ = 0x00000001;
        public const uint FILE_SHARE_WRITE = 0x00000002;
        public const uint OPEN_ALWAYS = 4;

        // 청정한 정격 SNO 베이스 마진 변수입니다.
        public static uint DefaultBaseSnoMargin = 50000;
        public static string DefaultBaseSnoMarginStr => DefaultBaseSnoMargin.ToString();

        public static Guid GUID_DEVINTERFACE_USBPRINT = new Guid("28d78fad-5a12-11d1-ae5b-0000f803a8c2");
        public static Guid GUID_DEVINTERFACE_USB_DEVICE = new Guid("A5DCBF10-6530-11D2-901F-00C04FB951ED");

        // usbprt.cpp CTL_CODE(FILE_DEVICE_UNKNOWN=0x22, 14, METHOD_BUFFERED=0, FILE_ANY_ACCESS=0)
        public static readonly uint IOCTL_USBPRINT_VENDOR_SET_COMMAND = 0x222058;
        public static readonly uint IOCTL_USBPRINT_VENDOR_GET_COMMAND = 0x22205C;

        public static readonly byte[] CmdGetUuidMac = { 0xE9, 0xFF, 0x10 };
        public static readonly byte[] CmdGetProSelfSts = { 0xE9, 0xFF, 0x0C };
        public static readonly byte[] CmdSetScanInterval = { 0x01, 0x0B, 0x20, 0x07, 0x01, 0x60, 0x00, 0x60, 0x00, 0x00, 0x00 };
        public static readonly byte[] CmdAdvOperaStart = { 0x01, 0x0C, 0x20, 0x02, 0x00, 0x01 };
        public static readonly byte[] CmdAdvOperaStop = { 0x01, 0x0C, 0x20, 0x02, 0x00, 0x00 };
        public static readonly byte[] CmdExtendAdvOn = { 0xE9, 0xFF, 0x21, 0x55, 0x01, 0x00, 0x01 };
        public static readonly byte[] CmdGwReset = { 0xE9, 0xFF, 0x02 };
        public static readonly byte[] CmdExtendAdvOption = { 0xE9, 0xFF, 0x16, 0x00 };
        public static readonly byte[] CmdGetLightness = { 0xe8, 0xff, 0x00, 0x00, 0x00, 0x00, 0x02, 0x04, 0xff, 0xff, 0x82, 0x4b };
        public static readonly byte[] CmdGetLightSts = { 0xE8, 0xFF, 0x00, 0x00, 0x00, 0x00, 0x02, 0x04, 0xFF, 0xFF, 0x82, 0x01 };


        public const string BaseMeshFileName = "base_mesh.json";
        public static string GetDatabaseFilePath() =>
            Path.Combine(Application.StartupPath, BaseMeshFileName);
        public const string TempMeshFileName = "temp_sanitized_mesh.json";

        public static byte[] HexToByte(string hex)
        {
            if (string.IsNullOrEmpty(hex)) return new byte[0];
            byte[] bytes = new byte[hex.Length / 2];
            for (int i = 0; i < bytes.Length; i++)
                bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
            return bytes;
        }

        public static string ReadIniValue(string section, string key, string defaultValue, string iniPath)
        {
            try
            {
                StringBuilder temp = new StringBuilder(255);
                GetPrivateProfileString(section, key, defaultValue, temp, 255, iniPath);
                return temp.ToString().Trim();
            }
            catch { return defaultValue; }
        }

        // =================================================================================
        // GetPrintDeviceHandle — usbprt.cpp 원본 로직 이식
        // GUID_DEVINTERFACE_USBPRINT → vid_248a 경로 → CreateFile(OPEN_ALWAYS, flags=0)
        // =================================================================================
        public static IntPtr GetPrintDeviceHandle(ushort targetId, Action<string> logCallback)
        {
            IntPtr handle = ScanByInterfaceGuid(GUID_DEVINTERFACE_USBPRINT, targetId, logCallback);
            if (handle != IntPtr.Zero) return handle;

            handle = ScanByInterfaceGuid(GUID_DEVINTERFACE_USB_DEVICE, targetId, logCallback);
            if (handle != IntPtr.Zero) return handle;

            // 레지스트리 폴백
            using (var usbKey = Microsoft.Win32.Registry.LocalMachine
                .OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\USB"))
            {
                if (usbKey == null) return IntPtr.Zero;
                foreach (string subName in usbKey.GetSubKeyNames())
                {
                    if (subName.IndexOf("VID_248A", StringComparison.OrdinalIgnoreCase) < 0) continue;
                    using (var devKey = usbKey.OpenSubKey(subName))
                    {
                        if (devKey == null) continue;
                        foreach (string inst in devKey.GetSubKeyNames())
                        {
                            string devPath = $@"\\?\USB#{subName}#{inst}#{{a5dcbf10-6530-11d2-901f-00c04fb951ed}}";
                            IntPtr h = CreateFile(devPath,
                                GENERIC_READ | GENERIC_WRITE,
                                FILE_SHARE_READ | FILE_SHARE_WRITE,
                                IntPtr.Zero, OPEN_ALWAYS, 0, IntPtr.Zero);
                            if (h != (IntPtr)(-1) && h != IntPtr.Zero)
                            {
                                byte[] devBuf = new byte[4];
                                ReadUSBMem(h, 0x7E, devBuf, 4);
                                uint devId = BitConverter.ToUInt32(devBuf, 0);
                                if (devId == targetId || targetId == 0xFFFF || devId == 0)
                                {
                                    logCallback?.Invoke($"🎯 [레지스트리 폴백] 핸들 확보 (ID:0x{devId:X4})");
                                    return h;
                                }
                                CloseHandle(h);
                            }
                        }
                    }
                }
            }
            return IntPtr.Zero;
        }

        private static IntPtr ScanByInterfaceGuid(Guid targetGuid, ushort targetId, Action<string> logCallback)
        {
            Guid intfce = targetGuid;
            IntPtr devs = SetupDiGetClassDevs(ref intfce, null, IntPtr.Zero,
                DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
            if (devs == (IntPtr)(-1) || devs == IntPtr.Zero) return IntPtr.Zero;

            uint devcount = 0;
            SP_DEVICE_INTERFACE_DATA devIf = new SP_DEVICE_INTERFACE_DATA();
            devIf.cbSize = (uint)Marshal.SizeOf(typeof(SP_DEVICE_INTERFACE_DATA));

            while (SetupDiEnumDeviceInterfaces(devs, IntPtr.Zero, ref intfce, devcount, ref devIf))
            {
                devcount++;
                uint size = 0;
                SetupDiGetDeviceInterfaceDetail(devs, ref devIf, IntPtr.Zero, 0, ref size, IntPtr.Zero);
                if (size == 0) continue;

                IntPtr buf = Marshal.AllocHGlobal((int)size);
                try
                {
                    Marshal.WriteInt32(buf, IntPtr.Size == 4 ? 5 : 8);
                    if (SetupDiGetDeviceInterfaceDetail(devs, ref devIf, buf, size, ref size, IntPtr.Zero))
                    {
                        string ifName = Marshal.PtrToStringAnsi(new IntPtr(buf.ToInt64() + 4));
                        if (ifName != null &&
                            ifName.IndexOf("vid_248a", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            IntPtr h = CreateFile(ifName,
                                GENERIC_READ | GENERIC_WRITE,
                                FILE_SHARE_READ | FILE_SHARE_WRITE,
                                IntPtr.Zero, OPEN_ALWAYS, 0, IntPtr.Zero);
                            if (h != (IntPtr)(-1) && h != IntPtr.Zero)
                            {
                                byte[] devBuf = new byte[4];
                                ReadUSBMem(h, 0x7E, devBuf, 4);
                                uint devId = BitConverter.ToUInt32(devBuf, 0);
                                if (devId == targetId || targetId == 0xFFFF || devId == 0)
                                {
                                    logCallback?.Invoke($"✨ [인터페이스 채널 개통] ID:0x{devId:X4}");
                                    SetupDiDestroyDeviceInfoList(devs);
                                    return h;
                                }
                                CloseHandle(h);
                            }
                        }
                    }
                }
                catch { }
                finally { Marshal.FreeHGlobal(buf); }
            }
            SetupDiDestroyDeviceInfoList(devs);
            return IntPtr.Zero;
        }

        // =================================================================================
        // WriteControlPacket — HCI 메쉬 패킷 전송 (WriteFile 직통)
        // On/Off, SET_DEV_KEY 등 14~34바이트 제어 패킷 전송용
        // =================================================================================
        public static bool WriteControlPacket(IntPtr hdev, int virtualAddr, byte[] data, bool isControl = true)
        {
            if (hdev == IntPtr.Zero || hdev == (IntPtr)(-1)) return false;
            bool ok = WriteFile(hdev, data, (uint)data.Length, out uint written, IntPtr.Zero);
            return ok && written == data.Length;
        }

        // =================================================================================
        // WriteUSBMem / ReadUSBMem — IOCTL DeviceIoControl
        // 레지스터 읽기(칩 ID 판별), WriteMem 내부에서 사용
        // =================================================================================
        public static int WriteUSBMem(IntPtr hdev, int addr, byte[] lpB, int len, int fifo = 0, int maxlen = 32)
        {
            if (hdev == IntPtr.Zero || hdev == (IntPtr)(-1)) return 0;
            byte[] buff = new byte[4096];
            buff[0] = (byte)(fifo != 0 ? 0x03 : 0x02);
            buff[1] = (byte)((addr >> 8) & 0xFF);
            buff[2] = (byte)(addr & 0xFF);
            byte pad = lpB.Length > 0 ? lpB[0] : (byte)0;
            buff[3] = pad; buff[4] = pad; buff[5] = pad; buff[6] = pad; buff[7] = pad;

            int nW = len > maxlen ? maxlen : len;
            Array.Copy(lpB, 0, buff, 8, nW);

            bool ok = DeviceIoControl(hdev, IOCTL_USBPRINT_VENDOR_SET_COMMAND,
                buff, (uint)(nW + 8), null, 0, out uint nb, IntPtr.Zero);
            return ok ? nW : 0;
        }

        public static int ReadUSBMem(IntPtr hdev, int addr, byte[] lpB, int len, int fifo = 0, int maxlen = 32)
        {
            if (hdev == IntPtr.Zero || hdev == (IntPtr)(-1)) return 0;
            byte[] buff = new byte[16];
            buff[0] = (byte)(fifo != 0 ? 0x03 : 0x02);
            buff[1] = (byte)((addr >> 8) & 0xFF);
            buff[2] = (byte)(addr & 0xFF);
            buff[3] = lpB.Length > 0 ? lpB[0] : (byte)0;

            int nR = 8 + (len > maxlen ? maxlen : len < 1 ? 1 : len);
            byte[] outBuf = new byte[nR];

            bool ok = DeviceIoControl(hdev, IOCTL_USBPRINT_VENDOR_GET_COMMAND,
                buff, 16, outBuf, (uint)nR, out uint nb, IntPtr.Zero);
            if (ok && nb > 0)
            {
                Array.Copy(outBuf, 0, lpB, 0, Math.Min(len, (int)nb));
                return (int)nb;
            }
            return 0;
        }

        // =================================================================================
        // WriteMem — usbprt.cpp WriteMem() 이식 (IOCTL 경유)
        // GW_RESET, SNO, IV 등 제어 명령 전송용
        // =================================================================================
        public static int WriteMem(IntPtr hdev, int addr, byte[] lpB, int len, int type = 1)
        {
            int ret = 0;
            int al = addr & 0xFFFF;
            int step = 1024;

            for (int i = 0; i < len; i += step)
            {
                int n = len - i > step ? step : len - i;
                int fadr = al + i;
                byte[] chunk = new byte[n];
                Array.Copy(lpB, i, chunk, 0, n);

                int rw = WriteUSBMem(hdev, fadr, chunk, n, type & 0x200, step);
                if (rw > 0) ret += rw;
                else break;
            }
            return ret;
        }

        // =================================================================================
        // SendSetProPara — e9 ff 09 + NetKey (WriteFile 직통)
        // =================================================================================
        public static bool SendSetProPara(IntPtr handle, string netKeyHex, int addr, Action<string> log)
        {
            if (string.IsNullOrEmpty(netKeyHex)) return false;
            var pkt = new List<byte> { 0xE9, 0xFF, 0x09 };
            pkt.AddRange(HexToByte(netKeyHex));
            pkt.Add(0x00); pkt.Add(0x00); pkt.Add(0x00);
            pkt.Add(0xFF); pkt.Add(0xFF); pkt.Add(0xFF); pkt.Add(0xFF);
            pkt.Add((byte)(addr & 0xFF)); pkt.Add((byte)((addr >> 8) & 0xFF));
            // HCI 패킷이므로 WriteFile 직통
            return WriteControlPacket(handle, 0x00, pkt.ToArray());
        }

        // =================================================================================
        // SendStartKeyBind — e9 ff 0b + AppKey (WriteFile 직통)
        // =================================================================================
        public static bool SendStartKeyBind(IntPtr handle, string appKeyHex, Action<string> log)
        {
            if (string.IsNullOrEmpty(appKeyHex)) return false;
            var pkt = new List<byte> { 0xE9, 0xFF, 0x0B, 0x01, 0x00, 0x00 };
            pkt.AddRange(HexToByte(appKeyHex));
            return WriteControlPacket(handle, 0x00, pkt.ToArray());
        }

        // =================================================================================
        // SendSetDevKey — e9 ff 0d + addr + flag + DevKey (WriteFile 직통)
        // =================================================================================
        public static bool SendSetDevKey(IntPtr handle, string devKeyHex, int addr, byte flag, Action<string> log)
        {
            if (string.IsNullOrEmpty(devKeyHex)) return false;
            byte[] keyBytes = new byte[16];
            try
            {
                byte[] p = HexToByte(devKeyHex);
                Array.Copy(p, 0, keyBytes, 0, Math.Min(p.Length, 16));
            }
            catch { Array.Clear(keyBytes, 0, 16); }

            // 순정 HCI 패킷: e9 ff 0d [addr_lo] [addr_hi] [flag] [key 16bytes]
            var pkt = new List<byte> { 0xE9, 0xFF, 0x0D,
                (byte)(addr & 0xFF), (byte)((addr >> 8) & 0xFF), flag };
            pkt.AddRange(keyBytes);
            return WriteControlPacket(handle, 0x00, pkt.ToArray());
        }
    }
}

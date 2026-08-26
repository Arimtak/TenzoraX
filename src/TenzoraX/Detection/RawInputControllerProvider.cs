using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace TenzoraX.Detection
{
    /// <summary>
    /// Erkennung über die Windows-Raw-Input-Geräteliste (user32.dll).
    /// Zeigt alle HID-Gamepads/-Joysticks, auch wenn sie nicht über XInput
    /// bereitgestellt werden – unabhängig vom Transport (USB/Bluetooth).
    /// </summary>
    public sealed class RawInputControllerProvider : IControllerProvider
    {
        private const uint RIDI_DEVICENAME = 0x20000007;
        private const uint RIDI_DEVICEINFO = 0x2000000B;
        private const uint RIM_TYPEHID = 2;

        public string ApiName => "Raw Input";

        List<DetectedDeviceInfo> IControllerProvider.Enumerate() => Enumerate();

        /// <summary>Listet alle Raw-Input-HID-Geräte mit Gamepad-/Joystick-Usage auf.
        /// <paramref name="verboseLogging"/>: false unterdrückt die per-Device-Logs,
        /// die sonst bei jedem Rescan den Log (Datei-I/O) fluten.</summary>
        public static List<DetectedDeviceInfo> Enumerate(bool verboseLogging = true)
        {
            var result = new List<DetectedDeviceInfo>();
            try
            {
                uint count = 0;
                uint listEntrySize = (uint)Marshal.SizeOf<RAWINPUTDEVICELIST>();
                if (GetRawInputDeviceList(IntPtr.Zero, ref count, listEntrySize) != 0 || count == 0)
                    return result;

                var list = new RAWINPUTDEVICELIST[count];
                var listPin = GCHandle.Alloc(list, GCHandleType.Pinned);
                try
                {
                    if (GetRawInputDeviceList(listPin.AddrOfPinnedObject(), ref count, listEntrySize) == unchecked((uint)-1))
                        return result;
                }
                finally { listPin.Free(); }

                int hidCount = 0, usageMismatch = 0, vidPidMissing = 0, infoFailed = 0;
                for (int i = 0; i < (int)count; i++)
                {
                    try
                    {
                        if (list[i].dwType != RIM_TYPEHID) continue;
                        hidCount++;

                        var devInfo = new RID_DEVICE_INFO();
                        devInfo.cbSize = 24;
                        uint infoSize = (uint)Marshal.SizeOf<RID_DEVICE_INFO>();
                        IntPtr infoBuf = Marshal.AllocHGlobal((int)infoSize);
                        try
                        {
                            Marshal.StructureToPtr(devInfo, infoBuf, false);
                            if (GetRawInputDeviceInfo(list[i].hDevice, RIDI_DEVICEINFO, infoBuf, ref infoSize) == unchecked((uint)-1))
                            {
                                infoFailed++;
                                continue;
                            }
                            devInfo = Marshal.PtrToStructure<RID_DEVICE_INFO>(infoBuf);
                        }
                        finally { Marshal.FreeHGlobal(infoBuf); }

                        if (devInfo.dwType != RIM_TYPEHID) { usageMismatch++; continue; }
                        if (devInfo.HidUsagePage != 1 || (devInfo.HidUsage != 5 && devInfo.HidUsage != 4))
                        {
                            if (verboseLogging)
                                App.LogApp("RI-Skip (Usage): Handle=0x" + list[i].hDevice.ToString("X") + " Page=0x" + devInfo.HidUsagePage.ToString("X4") + " Usage=0x" + devInfo.HidUsage.ToString("X4") + " VID=0x" + devInfo.HidVendorId.ToString("X4") + " PID=0x" + devInfo.HidProductId.ToString("X4"));
                            usageMismatch++;
                            continue;
                        }

                        string devicePath = GetRawDevicePath(list[i].hDevice);
                        string instanceId = InstanceIdFromDevicePath(devicePath);

                        ushort vid = (ushort)(devInfo.HidVendorId & 0xFFFF);
                        ushort pid = (ushort)(devInfo.HidProductId & 0xFFFF);
                        if ((vid == 0 && pid == 0) && !DeviceHelpers.TryParseVidPid(devicePath, out vid, out pid))
                        {
                            if (verboseLogging)
                                App.LogApp("RI-Skip (kein VID/PID): Path='" + devicePath + "' Page=0x" + devInfo.HidUsagePage.ToString("X4") + " Usage=0x" + devInfo.HidUsage.ToString("X4"));
                            vidPidMissing++;
                            continue;
                        }

                        if (verboseLogging)
                            App.LogApp("RI-Gamepad: Handle=0x" + list[i].hDevice.ToString("X") + " Page=0x" + devInfo.HidUsagePage.ToString("X4") + " Usage=0x" + devInfo.HidUsage.ToString("X4") + " VID=" + vid.ToString("X4") + " PID=" + pid.ToString("X4") + " Path='" + devicePath + "'");

                        result.Add(new DetectedDeviceInfo
                        {
                            Api = ControllerApi.RawInput,
                            Name = "",   // Raw Input liefert keinen Produktnamen – Anreicherung über HID/DirectInput
                            Manufacturer = DeviceHelpers.GetKnownVendorName(vid),
                            Vid = vid,
                            Pid = pid,
                            VidPid = DeviceHelpers.FormatVidPid(vid, pid),
                            DeviceInstanceId = instanceId,
                            DevicePath = devicePath,
                            UsagePage = devInfo.HidUsagePage,
                            Usage = devInfo.HidUsage
                        });
                    }
                    catch (Exception ex)
                    {
                        App.LogApp("RawInput-Gerät-Fehler: " + ex.Message);
                    }
                }
                if (verboseLogging && (hidCount > 0 || result.Count > 0))
                    App.LogApp("RI-Enumeration: " + count + " Geräte gesamt, " + hidCount + " HID-Typ, " + result.Count + " als Gamepad erkannt, " + usageMismatch + " Usage-Mismatch, " + vidPidMissing + " ohne VID/PID, " + infoFailed + " Info-Fehler");
            }
            catch (Exception ex)
            {
                App.LogApp("RawInput-Enumeration fehlgeschlagen: " + ex.Message);
            }
            return result;
        }

        /// <summary>Holt den Interface-Pfad ("\\?\HID#VID_...") zu einem Raw-Input-Handle.</summary>
        private static string GetRawDevicePath(IntPtr hDevice)
        {
            uint size = 0;
            if (GetRawInputDeviceInfo(hDevice, RIDI_DEVICENAME, IntPtr.Zero, ref size) == unchecked((uint)-1) || size == 0 || size > 8192)
                return "";
            // size ist in Zeichen (WCHAR), AllocHGlobal braucht Bytes -> *2
            IntPtr buf = Marshal.AllocHGlobal((int)size * 2 + 2);
            try
            {
                uint got = size;
                if (GetRawInputDeviceInfo(hDevice, RIDI_DEVICENAME, buf, ref got) == unchecked((uint)-1))
                    return "";
                return Marshal.PtrToStringUni(buf) ?? "";
            }
            finally { Marshal.FreeHGlobal(buf); }
        }

        /// <summary>
        /// Wandelt einen Raw-Input-Pfad ("\\?\HID#VID_x&PID_y#Instanz#{guid}") in eine
        /// SetupAPI-Instanz-ID ("HID\VID_x&PID_y\Instanz") um.
        /// Wichtig: LastIndexOf("#{") – bei Bluetooth-Geräten steht die GATT-Dienst-
        /// GUID ({00001812-…}) bereits IN der Instanz-ID, z.B.
        /// \\?\HID#{00001812-…}_Dev_VID&022dc8_PID&200b_…#d&amp;efcde22&amp;0&amp;0000#{4d1e55b2-…}
        /// </summary>
        internal static string InstanceIdFromDevicePath(string devicePath)
        {
            if (string.IsNullOrEmpty(devicePath)) return "";
            if (!devicePath.StartsWith("\\\\?\\", StringComparison.Ordinal)) return "";
            int guidIdx = devicePath.LastIndexOf("#{", StringComparison.Ordinal);
            string core = guidIdx > 4 ? devicePath.Substring(4, guidIdx - 4) : devicePath.Substring(4);
            return core.Replace('#', '\\');
        }

        // ============================================================
        //  P/INVOKE (Strukturen exakt nach winuser.h)
        // ============================================================

        [StructLayout(LayoutKind.Sequential)]
        private struct RAWINPUTDEVICELIST
        {
            public IntPtr hDevice;
            public uint dwType;
        }

        /// <summary>
        /// RID_DEVICE_INFO – Layout gemäß winuser.h:
        ///   cbSize(4) + dwType(4) + Union(max. 20 Bytes) = 32 Bytes.
        ///   Das cbSize-Feld wird auf den tatsächlichen Union-Inhalt (24 Bytes für HID)
        ///   gesetzt; die Struktur selbst muss 32 Bytes haben, weil die API den
        ///   größten Union-Fall (Keyboard) als Puffergröße erwartet.
        /// </summary>
        [StructLayout(LayoutKind.Explicit, Size = 32)]
        private struct RID_DEVICE_INFO
        {
            [FieldOffset(0)] public uint cbSize;
            [FieldOffset(4)] public uint dwType;
            [FieldOffset(8)] public uint HidVendorId;
            [FieldOffset(12)] public uint HidProductId;
            [FieldOffset(16)] public uint HidVersionNumber;
            [FieldOffset(20)] public ushort HidUsagePage;
            [FieldOffset(22)] public ushort HidUsage;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint GetRawInputDeviceList(IntPtr pRawInputDeviceList, ref uint puiNumDevices, uint cbSize);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern uint GetRawInputDeviceInfo(IntPtr hDevice, uint uiCommand, IntPtr pData, ref uint pcbSize);
    }
}

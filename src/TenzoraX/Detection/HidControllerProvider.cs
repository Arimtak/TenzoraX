using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace TenzoraX.Detection
{
    /// <summary>
    /// Von der SetupAPI/HID-Enumeration gelieferter Gamecontroller.
    /// Enthält die Felder des Eingabe-Pipelines sowie optionale Diagnose-Details.
    /// </summary>
    public class HidGamepadInfo
    {
        // Pipeline-relevante Felder
        public string Name = "";
        public ushort Vid;
        public ushort Pid;
        public string VidPid = "";
        public string InstanceId = "";
        public string ConnectionType = "Unknown";   // USB | Bluetooth | 2.4 GHz | Unknown

        // Diagnose-Details (nur bei withDetails gefüllt)
        public string FriendlyName = "";
        public string DeviceDesc = "";
        public string HardwareIds = "";
        public ushort UsagePage;
        public ushort Usage;
        public int AxesCount;
        public int ButtonsCount;
        public int PovsCount;
        public ushort InputReportLength;

        public DetectedDeviceInfo ToDetected() => new()
        {
            Api = ControllerApi.Hid,
            Name = Name,
            Manufacturer = DeviceHelpers.GetKnownVendorName(Vid),
            Vid = Vid,
            Pid = Pid,
            VidPid = VidPid,
            Transport = ConnectionType,
            DeviceInstanceId = InstanceId,
            Axes = AxesCount,
            Buttons = ButtonsCount,
            Povs = PovsCount
        };
    }

    /// <summary>
    /// Erkennung über die Windows-HID-Geräteschnittstelle (SetupAPI + hid.dll).
    /// Findet USB-, Bluetooth- und 2.4-GHz-Gamepads unabhängig vom Hersteller.
    /// Akzeptiert Usage Page 1 mit Usage 5 (Gamepad) UND Usage 4 (Joystick),
    /// damit auch Bluetooth-Varianten erkannt werden, die sich als Joystick melden.
    /// </summary>
    public sealed class HidControllerProvider : IControllerProvider
    {
        public static HidControllerProvider Instance { get; } = new();
        private HidControllerProvider() { }
        public string ApiName => "HID";

        List<DetectedDeviceInfo> IControllerProvider.Enumerate()
        {
            var result = new List<DetectedDeviceInfo>();
            foreach (var g in EnumerateGamepads(includeJoystickUsage: true, withDetails: true))
                result.Add(g.ToDetected());
            return result;
        }

        /// <summary>Listet alle vorhandenen HID-Gamecontroller auf.</summary>
        /// <param name="verboseLogging">false unterdrückt die per-Device-/Summary-Logs
        /// (der Rescan ruft das alle 300 ms auf – ungefiltertes Logging bremst den Poll-Thread).</param>
        public List<HidGamepadInfo> EnumerateGamepads(bool includeJoystickUsage = true, bool withDetails = false, bool verboseLogging = true)
        {
            var result = new List<HidGamepadInfo>();
            Guid hidClass = GUID_DEVCLASS_HIDCLASS;
            IntPtr devInfoSet = SetupDiGetClassDevs(ref hidClass, null, IntPtr.Zero, DIGCF_PRESENT);
            if (devInfoSet == IntPtr.Zero || devInfoSet == new IntPtr(-1)) return result;

            int totalHid = 0, skippedVidPid = 0, skippedUsage = 0;
            try
            {
                uint index = 0;
                while (true)
                {
                    var data = new SP_DEVINFO_DATA { cbSize = (uint)Marshal.SizeOf<SP_DEVINFO_DATA>() };
                    if (!SetupDiEnumDeviceInfo(devInfoSet, index, ref data)) break;
                    index++;
                    totalHid++;

                    try
                    {
                        string instanceId = GetDeviceInstanceId(devInfoSet, ref data);
                        if (string.IsNullOrEmpty(instanceId)) continue;
                        if (!DeviceHelpers.TryParseVidPid(instanceId, out ushort vid, out ushort pid))
                        {
                            string rawDesc = GetDeviceRegistryPropertyString(devInfoSet, ref data, SPDRP_DEVICEDESC);
                            if (verboseLogging)
                                App.LogApp("HID-Skip (kein VID/PID): " + instanceId + " Desc='" + rawDesc + "'");
                            skippedVidPid++;
                            continue;
                        }
                        bool isGame = IsGamepadUsage(devInfoSet, ref data, instanceId, includeJoystickUsage, verboseLogging);
                        if (!isGame) { skippedUsage++; continue; }

                        string parent = GetDevicePropertyString(devInfoSet, ref data, DEVPKEY_Device_Parent);
                        string type = DetectConnectionType(devInfoSet, instanceId, parent);
                        string friendly = GetDeviceRegistryPropertyString(devInfoSet, ref data, SPDRP_FRIENDLYNAME).Trim();
                        string desc = GetDeviceRegistryPropertyString(devInfoSet, ref data, SPDRP_DEVICEDESC).Trim();

                        var info = new HidGamepadInfo
                        {
                            Name = GetControllerDisplayName(friendly, desc, vid, pid),
                            Vid = vid,
                            Pid = pid,
                            VidPid = DeviceHelpers.FormatVidPid(vid, pid),
                            InstanceId = instanceId,
                            ConnectionType = type,
                            FriendlyName = friendly,
                            DeviceDesc = desc,
                            HardwareIds = GetDeviceRegistryPropertyString(devInfoSet, ref data, SPDRP_HARDWAREID)
                        };

                        if (withDetails)
                            FillCaps(info);

                        result.Add(info);
                    }
                    catch (Exception ex)
                    {
                        App.LogApp("HID-Enum-Fehler: " + ex.ToString().Replace("\r", " ").Replace("\n", " | "));
                    }
                }
                if (verboseLogging)
                    App.LogApp("HID-Enumeration: " + totalHid + " HID-Geräte gesamt, " + skippedVidPid + " ohne VID/PID, " + skippedUsage + " ohne Gamepad-Usage, " + result.Count + " als Gamepad erkannt");
            }
            finally
            {
                SetupDiDestroyDeviceInfoList(devInfoSet);
            }
            return result;
        }

        /// <summary>Liest Achsen-/Tasten-/POV-Anzahl und Reportlänge direkt aus dem HID-Descriptor.</summary>
        private static void FillCaps(HidGamepadInfo info)
        {
            try
            {
                string path = InterfacePathFromInstanceId(info.InstanceId);
                if (path.Length == 0) return;

                var handle = CreateFile(path, GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
                if (handle.IsInvalid) return;
                try
                {
                    if (!HidD_GetPreparsedData(handle, out IntPtr prep) || prep == IntPtr.Zero) return;
                    try
                    {
                        if (HidP_GetCaps(prep, out var caps) != HIDP_STATUS_SUCCESS) return;
                        info.InputReportLength = caps.InputReportByteLength;
                        info.UsagePage = caps.UsagePage;
                        info.Usage = caps.Usage;

                        ushort valueCount = 0;
                        HidP_GetValueCaps(HIDP_REPORT_TYPE_INPUT, null, ref valueCount, prep);
                        int axes = 0, povs = 0;
                        if (valueCount > 0)
                        {
                            var values = new HIDP_VALUE_CAPS[valueCount];
                            ushort n = valueCount;
                            if (HidP_GetValueCaps(HIDP_REPORT_TYPE_INPUT, values, ref n, prep) == HIDP_STATUS_SUCCESS)
                            {
                                foreach (var v in values)
                                {
                                    if (v.UsagePage != 1) continue;
                                    ushort usage = v.Range_UsageMin;
                                    if (usage >= 0x30 && usage <= 0x35) axes++;
                                    else if (usage == 0x39) povs++;
                                }
                            }
                        }
                        info.AxesCount = axes;
                        info.PovsCount = povs;

                        ushort btnCount = 0;
                        HidP_GetButtonCaps(HIDP_REPORT_TYPE_INPUT, null, ref btnCount, prep);
                        int buttons = 0;
                        if (btnCount > 0)
                        {
                            var btns = new HIDP_BUTTON_CAPS[btnCount];
                            ushort n = btnCount;
                            if (HidP_GetButtonCaps(HIDP_REPORT_TYPE_INPUT, btns, ref n, prep) == HIDP_STATUS_SUCCESS)
                            {
                                foreach (var b in btns)
                                    buttons += b.IsRange != 0 ? (b.Range_UsageMax - b.Range_UsageMin + 1) : 1;
                            }
                        }
                        info.ButtonsCount = buttons;
                    }
                    finally { HidD_FreePreparsedData(prep); }
                }
                finally { handle.Dispose(); }
            }
            catch { }
        }

        /// <summary>Baut aus einer Instanz-ID den HID-Interface-Pfad ("\\?\HID#VID_..#..#{guid}").</summary>
        internal static string InterfacePathFromInstanceId(string instanceId)
        {
            if (string.IsNullOrEmpty(instanceId) || !instanceId.StartsWith("HID\\", StringComparison.OrdinalIgnoreCase))
                return "";
            return "\\\\?\\" + instanceId.Replace('\\', '#') + "#" + GUID_DEVINTERFACE_HID.ToString("B");
        }

        // ============================================================
        //  SETUPAPI-P/INVOKE (Strukturen exakt wie im Windows SDK)
        // ============================================================

        private static readonly Guid GUID_DEVCLASS_HIDCLASS =
            new(0x745a17a0, 0x74d3, 0x11d0, 0xb6, 0xfe, 0x00, 0xa0, 0xc9, 0x0f, 0x57, 0xda);

        internal static readonly Guid GUID_DEVINTERFACE_HID =
            new(0x4d1e55b2, 0xf16f, 0x11cf, 0x88, 0xcb, 0x00, 0x11, 0x11, 0x00, 0x00, 0x30);

        // DEVPKEY_Device_Parent: {4340a6c5-93fa-4706-972c-7b648008a5a7}, 8
        private static readonly DEVPROPKEY DEVPKEY_Device_Parent =
            new() { fmtid = new Guid(0x4340a6c5, 0x93fa, 0x4706, 0x97, 0x2c, 0x7b, 0x64, 0x80, 0x08, 0xa5, 0xa7), pid = 8 };

        private const uint DIGCF_PRESENT = 0x00000002;
        private const uint SPDRP_DEVICEDESC = 0x00000000;
        private const uint SPDRP_HARDWAREID = 0x00000001;
        private const uint SPDRP_FRIENDLYNAME = 0x0000000C;
        private const uint DICS_FLAG_GLOBAL = 0x00000001;
        private const uint DIREG_DEV = 0x00000001;
        private const uint KEY_READ = 0x00020019;
        private const uint KEY_WOW64_64KEY = 0x00000100;
        private const uint GENERIC_READ = 0x80000000;
        private const uint FILE_SHARE_READ = 0x00000001;
        private const uint FILE_SHARE_WRITE = 0x00000002;
        private const uint OPEN_EXISTING = 3;
        internal const string ConnectionTypeUsb = "USB";
        internal const string ConnectionTypeBluetooth = "Bluetooth";
        internal const string ConnectionTypeWireless = "2.4 GHz";

        [StructLayout(LayoutKind.Sequential)]
        private struct SP_DEVINFO_DATA
        {
            public uint cbSize;
            public Guid classGuid;
            public uint devInst;
            public IntPtr reserved;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DEVPROPKEY
        {
            public Guid fmtid;
            public uint pid;
        }

        [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern IntPtr SetupDiGetClassDevs(ref Guid ClassGuid, string? Enumerator, IntPtr hwndParent, uint Flags);

        [DllImport("setupapi.dll", SetLastError = true)]
        private static extern bool SetupDiEnumDeviceInfo(IntPtr DeviceInfoSet, uint MemberIndex, ref SP_DEVINFO_DATA DeviceInfoData);

        [DllImport("setupapi.dll", SetLastError = true)]
        private static extern bool SetupDiDestroyDeviceInfoList(IntPtr DeviceInfoSet);

        [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern bool SetupDiGetDeviceInstanceId(IntPtr DeviceInfoSet, ref SP_DEVINFO_DATA DeviceInfoData, StringBuilder DeviceInstanceId, uint DeviceInstanceIdSize, out uint RequiredSize);

        [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern bool SetupDiGetDeviceRegistryProperty(IntPtr DeviceInfoSet, ref SP_DEVINFO_DATA DeviceInfoData, uint Property, out uint PropertyRegDataType, IntPtr PropertyBuffer, uint PropertyBufferSize, out uint RequiredSize);

        [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Auto, EntryPoint = "SetupDiOpenDevRegKey")]
        private static extern IntPtr SetupDiOpenDevRegKey(IntPtr DeviceInfoSet, ref SP_DEVINFO_DATA DeviceInfoData, uint Scope, uint HwProfile, uint KeyType, uint samDesired);

        [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern bool SetupDiOpenDeviceInfo(IntPtr DeviceInfoSet, string DeviceInstanceId, IntPtr hwndParent, uint OpenFlags, ref SP_DEVINFO_DATA DeviceInfoData);

        [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern bool SetupDiGetDeviceProperty(IntPtr DeviceInfoSet, ref SP_DEVINFO_DATA DeviceInfoData, ref DEVPROPKEY PropertyKey, out uint PropertyType, IntPtr PropertyBuffer, uint PropertyBufferSize, out uint RequiredSize, uint Flags);

        // hid.dll für Detail-Caps
        private const int HIDP_STATUS_SUCCESS = 0x00110000;
        private const int HIDP_REPORT_TYPE_INPUT = 1;

        [StructLayout(LayoutKind.Sequential)]
        private struct HIDP_CAPS
        {
            public ushort Usage;
            public ushort UsagePage;
            public ushort InputReportByteLength;
            public ushort OutputReportByteLength;
            public ushort FeatureReportByteLength;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)] public ushort[] Reserved;
            public ushort NumberLinkCollectionNodes;
            public ushort NumberInputButtonCaps;
            public ushort NumberInputValueCaps;
            public ushort NumberInputDataIndices;
            public ushort NumberOutputButtonCaps;
            public ushort NumberOutputValueCaps;
            public ushort NumberOutputDataIndices;
            public ushort NumberFeatureButtonCaps;
            public ushort NumberFeatureValueCaps;
            public ushort NumberFeatureDataIndices;
        }

        /// <summary>HIDP_VALUE_CAPS – Layout gemäß hidpi.h (72 Bytes).</summary>
        [StructLayout(LayoutKind.Sequential)]
        private struct HIDP_VALUE_CAPS
        {
            public ushort UsagePage;
            public byte ReportID;
            public byte IsAlias;
            public ushort BitField;
            public ushort LinkCollection;
            public ushort LinkUsage;
            public ushort LinkUsagePage;
            public byte IsRange;
            public byte IsStringRange;
            public byte IsDesignatorRange;
            public byte IsAbsolute;
            public byte HasNull;
            public byte Reserved;
            public ushort BitSize;
            public ushort ReportCount;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 5)] public ushort[] Reserved2;
            public uint UnitsExp;
            public uint Units;
            public int LogicalMin;
            public int LogicalMax;
            public int PhysicalMin;
            public int PhysicalMax;
            public ushort Range_UsageMin;
            public ushort Range_UsageMax;
            public ushort Range_StringMin;
            public ushort Range_StringMax;
            public ushort Range_DesignatorMin;
            public ushort Range_DesignatorMax;
            public ushort Range_DataIndexMin;
            public ushort Range_DataIndexMax;
        }

        /// <summary>
        /// HIDP_BUTTON_CAPS – anderes Layout als VALUE_CAPS (72 Bytes):
        /// Kein HasNull/Reserved-BitSize, stattdessen ReportCount + Reserved2(ushort) + Reserved[9](uint),
        /// danach Union (Range/NotRange) – jeweils 16 Bytes.
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        private struct HIDP_BUTTON_CAPS
        {
            public ushort UsagePage;
            public byte ReportID;
            public byte IsAlias;
            public ushort BitField;
            public ushort LinkCollection;
            public ushort LinkUsage;
            public ushort LinkUsagePage;
            public byte IsRange;
            public byte IsStringRange;
            public byte IsDesignatorRange;
            public byte IsAbsolute;
            public ushort ReportCount;
            public ushort Reserved2;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 9)] public uint[] Reserved;
            public ushort Range_UsageMin;
            public ushort Range_UsageMax;
            public ushort Range_StringMin;
            public ushort Range_StringMax;
            public ushort Range_DesignatorMin;
            public ushort Range_DesignatorMax;
            public ushort Range_DataIndexMin;
            public ushort Range_DataIndexMax;
        }

        [DllImport("hid.dll", SetLastError = true)]
        private static extern bool HidD_GetPreparsedData(SafeFileHandle HidDeviceObject, out IntPtr PreparsedData);

        [DllImport("hid.dll", SetLastError = true)]
        private static extern bool HidD_FreePreparsedData(IntPtr PreparsedData);

        [DllImport("hid.dll", SetLastError = true)]
        private static extern int HidP_GetCaps(IntPtr PreparsedData, out HIDP_CAPS Capabilities);

        [DllImport("hid.dll", SetLastError = true)]
        private static extern int HidP_GetValueCaps(int ReportType, [Out] HIDP_VALUE_CAPS[]? ValueCaps, ref ushort ValueCapsLength, IntPtr PreparsedData);

        [DllImport("hid.dll", SetLastError = true)]
        private static extern int HidP_GetButtonCaps(int ReportType, [Out] HIDP_BUTTON_CAPS[]? ButtonCaps, ref ushort ButtonCapsLength, IntPtr PreparsedData);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern SafeFileHandle CreateFile(string lpFileName, uint dwDesiredAccess, uint dwShareMode, IntPtr lpSecurityAttributes, uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);

        // ============================================================
        //  ENUMERATIONS-HELFER
        // ============================================================

        private static string GetDeviceInstanceId(IntPtr devInfoSet, ref SP_DEVINFO_DATA data)
        {
            uint required = 0;
            if (!SetupDiGetDeviceInstanceId(devInfoSet, ref data, new StringBuilder(1), 0, out required)) { }
            if (required == 0 || required > 1024) return "";
            var sb = new StringBuilder((int)required);
            if (!SetupDiGetDeviceInstanceId(devInfoSet, ref data, sb, required, out _)) return "";
            return sb.ToString();
        }

        private static string GetDeviceRegistryPropertyString(IntPtr devInfoSet, ref SP_DEVINFO_DATA data, uint property)
        {
            // Größenabfrage schlägt erwartungsgemäß mit ERROR_INSUFFICIENT_BUFFER fehl
            // (liefert dann die benötigte Größe in RequiredSize) – nur RequiredSize zählt.
            uint type, required = 0;
            SetupDiGetDeviceRegistryProperty(devInfoSet, ref data, property, out type, IntPtr.Zero, 0, out required);
            if (required == 0 || required > 8192) return "";
            IntPtr buffer = Marshal.AllocHGlobal((int)required);
            try
            {
                if (!SetupDiGetDeviceRegistryProperty(devInfoSet, ref data, property, out type, buffer, required, out _))
                    return "";
                return Marshal.PtrToStringUni(buffer) ?? "";
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }

        private static string GetDevicePropertyString(IntPtr devInfoSet, ref SP_DEVINFO_DATA data, DEVPROPKEY key)
        {
            uint type, required = 0;
            SetupDiGetDeviceProperty(devInfoSet, ref data, ref key, out type, IntPtr.Zero, 0, out required, 0);
            if (required == 0 || required > 8192) return "";
            IntPtr buffer = Marshal.AllocHGlobal((int)required);
            try
            {
                if (!SetupDiGetDeviceProperty(devInfoSet, ref data, ref key, out type, buffer, required, out _, 0))
                    return "";
                return Marshal.PtrToStringUni(buffer) ?? "";
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }

        /// <summary>
        /// Prüft, ob ein HID-Gerät ein Gamecontroller ist: Usage Page 1 / Usage 5 (Gamepad)
        /// oder Usage 4 (Joystick) – Bluetooth-Varianten melden sich teils als Joystick.
        /// Mit Diagnose-Logging: jede Stufe meldet, ob sie matcht oder nicht.
        /// </summary>
        private static bool IsGamepadUsage(IntPtr devInfoSet, ref SP_DEVINFO_DATA data, string instanceId, bool includeJoystickUsage, bool verboseLogging)
        {
            string hwIds = "";
            string desc = "";
            bool? registryHasValues = null;
            int regPage = -1, regUsage = -1;
            try
            {
                IntPtr hKey = SetupDiOpenDevRegKey(devInfoSet, ref data, DICS_FLAG_GLOBAL, 0, DIREG_DEV, KEY_READ | KEY_WOW64_64KEY);
                if (hKey != IntPtr.Zero)
                {
                    try
                    {
                        using var key = RegistryKey.FromHandle(new SafeRegistryHandle(hKey, true), RegistryView.Default);
                        var pageVal = key.GetValue("DeviceUsagePage");
                        var usageVal = key.GetValue("DeviceUsage");
                        if (pageVal is int page && usageVal is int usage)
                        {
                            registryHasValues = true;
                            regPage = page; regUsage = usage;
                            if (page == 0x01 && usage == 0x05) return true;
                            if (includeJoystickUsage && page == 0x01 && usage == 0x04) return true;
                            // Registry-Werte vorhanden aber kein Gamepad -> direkt false
                            if (verboseLogging)
                                App.LogApp("HID-Check (Registry): " + instanceId + " Page=0x" + page.ToString("X4") + " Usage=0x" + usage.ToString("X4") + " -> kein Gamepad");
                            return false;
                        }
                        else
                        {
                            registryHasValues = false;
                        }
                    }
                    catch { registryHasValues = false; }
                }
                else
                {
                    registryHasValues = false;
                }
            }
            catch { registryHasValues = false; }

            // HardwareID: sprachunabhängige Marker "UP:0001_U:0005"/"UP:0001_U:0004"
            hwIds = GetDeviceRegistryPropertyString(devInfoSet, ref data, SPDRP_HARDWAREID);
            if (hwIds.IndexOf("UP:0001_U:0005", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (includeJoystickUsage && hwIds.IndexOf("UP:0001_U:0004", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (hwIds.IndexOf("HID_DEVICE_SYSTEM_GAME", StringComparison.OrdinalIgnoreCase) >= 0) return true;

            // Fallback: lokalisierte Gerätebeschreibung (z.B. "HID-konformer Gamecontroller")
            desc = GetDeviceRegistryPropertyString(devInfoSet, ref data, SPDRP_DEVICEDESC);
            string descLower = desc.ToLowerInvariant();
            bool descMatch = descLower.Contains("game controller") || descLower.Contains("gamecontroller")
                || descLower.Contains("gamepad") || descLower.Contains("joystick") || descLower.Contains("spielsteuerung");
            if (descMatch) return true;

            // Kein Match -> Diagnose loggen (nur wenn HID-Device überhaupt in der Nähe von Gamepad liegt)
            if (verboseLogging)
            {
                string regInfo = registryHasValues == true ? "Page=0x" + regPage.ToString("X4") + " Usage=0x" + regUsage.ToString("X4") : "keine Registry-Usage";
                App.LogApp("HID-Skip (kein Gamepad-Usage): " + instanceId + " [" + regInfo + "] HWID='" + hwIds.Replace("\0", "|") + "' Desc='" + desc + "'");
            }
            return false;
        }

        /// <summary>
        /// Bestimmt die Verbindungsart über den Gerätebaum:
        /// BTHENUM/BTHLE-Parent = Bluetooth, Receiver-/Adapter-/2.4G-Parent = 2.4 GHz, sonst USB.
        /// </summary>
        private static string DetectConnectionType(IntPtr devInfoSet, string ownInstance, string parentInstance)
        {
            // Bluetooth-GATT-Geräte tragen ihre Dienst-GUID direkt in der Instanz-ID
            // ("HID\{00001812-…}_DEV_VID&…") – der Parent-Baum hilft hier nicht weiter.
            if (ownInstance.IndexOf("_DEV_VID&", StringComparison.OrdinalIgnoreCase) >= 0 ||
                parentInstance.IndexOf("_DEV_VID&", StringComparison.OrdinalIgnoreCase) >= 0 ||
                parentInstance.StartsWith("BTHLE\\", StringComparison.OrdinalIgnoreCase))
                return ConnectionTypeBluetooth;

            string prefixBased = DetectionMerge.TransportFromParentInstance(parentInstance);
            if (prefixBased == ConnectionTypeBluetooth) return ConnectionTypeBluetooth;
            if (string.IsNullOrEmpty(parentInstance)) return "Unknown";

            string parentDesc = GetDescriptionByInstance(devInfoSet, parentInstance);
            if (ContainsWirelessKeyword(parentDesc)) return ConnectionTypeWireless;

            if (IsGenericUsbDescription(parentDesc))
            {
                string grandParent = GetParentByInstance(devInfoSet, parentInstance);
                string grandDesc = GetDescriptionByInstance(devInfoSet, grandParent);
                if (ContainsWirelessKeyword(grandDesc)) return ConnectionTypeWireless;

                if (IsGenericUsbDescription(grandDesc))
                {
                    string greatGrand = GetParentByInstance(devInfoSet, grandParent);
                    string greatDesc = GetDescriptionByInstance(devInfoSet, greatGrand);
                    if (ContainsWirelessKeyword(greatDesc)) return ConnectionTypeWireless;
                }
            }
            return ConnectionTypeUsb;
        }

        private static bool ContainsWirelessKeyword(string description)
        {
            if (string.IsNullOrEmpty(description)) return false;
            string d = description.ToLowerInvariant();
            return d.Contains("wireless") || d.Contains("2.4g") || d.Contains("receiver") || d.Contains("dongle") || d.Contains("adapter");
        }

        private static bool IsGenericUsbDescription(string description)
        {
            if (string.IsNullOrEmpty(description)) return true;
            string d = description.ToLowerInvariant();
            return d.Contains("composite") || d.Contains("input device") || d.Contains("root hub") || d.Contains("usb hub");
        }

        private static string GetParentByInstance(IntPtr devInfoSet, string instanceId)
        {
            if (string.IsNullOrEmpty(instanceId)) return "";
            var data = new SP_DEVINFO_DATA { cbSize = (uint)Marshal.SizeOf<SP_DEVINFO_DATA>() };
            if (!SetupDiOpenDeviceInfo(devInfoSet, instanceId, IntPtr.Zero, 0, ref data)) return "";
            var key = DEVPKEY_Device_Parent;
            uint type, required = 0;
            SetupDiGetDeviceProperty(devInfoSet, ref data, ref key, out type, IntPtr.Zero, 0, out required, 0);
            if (required == 0 || required > 8192) return "";
            IntPtr buffer = Marshal.AllocHGlobal((int)required);
            try
            {
                if (!SetupDiGetDeviceProperty(devInfoSet, ref data, ref key, out type, buffer, required, out _, 0))
                    return "";
                return Marshal.PtrToStringUni(buffer) ?? "";
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }

        private static string GetDescriptionByInstance(IntPtr devInfoSet, string instanceId)
        {
            if (string.IsNullOrEmpty(instanceId)) return "";
            var data = new SP_DEVINFO_DATA { cbSize = (uint)Marshal.SizeOf<SP_DEVINFO_DATA>() };
            if (!SetupDiOpenDeviceInfo(devInfoSet, instanceId, IntPtr.Zero, 0, ref data)) return "";
            return GetDeviceRegistryPropertyString(devInfoSet, ref data, SPDRP_DEVICEDESC).Trim();
        }

        /// <summary>Anzeigename: FriendlyName, sonst DeviceDesc, sonst Hersteller-/Generik-Fallback.</summary>
        private static string GetControllerDisplayName(string friendly, string desc, ushort vid, ushort pid)
        {
            if (!string.IsNullOrEmpty(friendly) && !LooksGeneric(friendly))
                return friendly;
            if (!string.IsNullOrEmpty(desc) && !LooksGeneric(desc))
                return desc;

            string vendor = DeviceHelpers.GetKnownVendorName(vid);
            return !string.IsNullOrEmpty(vendor) ? vendor + " Gamepad" : "USB Gamepad";
        }

        private static bool LooksGeneric(string text) => DeviceHelpers.LooksLikeGenericGamepad(text);
    }
}

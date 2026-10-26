using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.Win32.SafeHandles;
using TenzoraX.Detection;
using HidDeviceInfo = TenzoraX.Detection.HidGamepadInfo;

namespace TenzoraX
{
    public class ControllerStateEventArgs : EventArgs
    {
        public string ControllerName { get; set; } = "";
        public string ConnectionType { get; set; } = "Unknown";

        public List<string> PressedButtons { get; set; } = new();
        public double LeftStickX { get; set; }
        public double LeftStickY { get; set; }
        public double RightStickX { get; set; }
        public double RightStickY { get; set; }
    }

    /// <summary>
    /// Öffentliche Sicht auf einen erkannten Controller für die UI.
    /// </summary>
    public class DetectedController
    {
        public int SlotIndex { get; set; }
        public bool IsXInput { get; set; }
        public string Name { get; set; } = "";
        public string ConnectionType { get; set; } = "Unknown";   // USB | Bluetooth | 2.4 GHz | Unknown
        public string VidPid { get; set; } = "";                   // VID_XXXX&PID_YYYY
        public string InstanceId { get; set; } = "";
        public bool AccessError { get; set; }
        public string AccessErrorReason { get; set; } = "";

        /// <summary>
        /// Die Schnittstelle dieses Geräts ist in Windows deaktiviert (Problemcode 22).
        /// Sie liefert keine Eingaben und fehlt im Windows-Panel und bei Steam – in der
        /// Auswahlliste muss sie deshalb als solche erkennbar sein.
        /// </summary>
        public bool WindowsInterfaceDisabled { get; set; }

        public bool Connected { get; set; }
        public string StableId { get; set; } = "";
    }

    /// <summary>
    /// Ein Listeneintrag, der als Dublette derselben Geräteinstanz verworfen wurde.
    /// Aufbewahrt für den Diagnosebericht – dort ist der Fall nachweisbar, ohne die
    /// app.log zu durchsuchen.
    /// </summary>
    public sealed class CollapsedInstanceRecord
    {
        public DateTime At { get; init; }
        public string Key { get; init; } = "";
        public string KeptName { get; init; } = "";
        public string KeptInstanceId { get; init; } = "";
        public bool KeptHasAccess { get; init; }
        public string DroppedName { get; init; } = "";
        public string DroppedInstanceId { get; init; } = "";
        public bool DroppedHasAccess { get; init; }
    }

    public class ControllerManager
    {
        // ============================================================
        //  XINPUT (Xbox 360/One/Series X|S)
        // ============================================================

        private const uint ERROR_SUCCESS = 0;
        private const uint ERROR_DEVICE_NOT_CONNECTED = 0x048F;

        [DllImport("xinput1_4.dll")]
        private static extern uint XInputGetState(uint dwUserIndex, out XINPUT_STATE pState);

        // 1-ms-Timer-Auflösung, damit Thread.Sleep(PollIntervalMs) wirklich ~4 ms
        // schläft (Standard-Windows-Timer sind nur ~15,6 ms genau).
        [DllImport("winmm.dll")]
        private static extern uint timeBeginPeriod(uint uPeriod);
        [DllImport("winmm.dll")]
        private static extern uint timeEndPeriod(uint uPeriod);

        [StructLayout(LayoutKind.Sequential)]
        private struct XINPUT_STATE
        {
            public uint dwPacketNumber;
            public XINPUT_GAMEPAD Gamepad;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct XINPUT_GAMEPAD
        {
            public ushort wButtons;
            public byte bLeftTrigger;
            public byte bRightTrigger;
            public short sThumbLX;
            public short sThumbLY;
            public short sThumbRX;
            public short sThumbRY;
        }

        // XInput button masks
        private const ushort XINPUT_A = 0x1000;
        private const ushort XINPUT_B = 0x2000;
        private const ushort XINPUT_X = 0x4000;
        private const ushort XINPUT_Y = 0x8000;
        private const ushort XINPUT_L1 = 0x0100;
        private const ushort XINPUT_R1 = 0x0200;
        private const ushort XINPUT_L3 = 0x0040;
        private const ushort XINPUT_R3 = 0x0080;
        private const ushort XINPUT_BACK = 0x0020;
        private const ushort XINPUT_START = 0x0010;
        private const ushort XINPUT_DPAD_U = 0x0001;
        private const ushort XINPUT_DPAD_D = 0x0002;
        private const ushort XINPUT_DPAD_L = 0x0004;
        private const ushort XINPUT_DPAD_R = 0x0008;


        // ============================================================
        //  WINMM JOYSTICK API (8BitDo, PS4/5, Switch Pro, etc.)
        // ============================================================

        [DllImport("winmm.dll")]
        private static extern int joyGetNumDevs();

        [DllImport("winmm.dll")]
        private static extern int joyGetDevCapsW(uint uJoyID, out JOYCAPSW pjc, int cbjc);

        [DllImport("winmm.dll")]
        private static extern int joyGetPosEx(uint uJoyID, out JOYINFOEX pji);

        private const int JOYERR_NOERROR = 0;
        private const int JOYERR_PARMS = 165;       // cbjc passt nicht zu sizeof(JOYCAPS)
        private const int JOYERR_UNPLUGGED = 167;
        private const uint JOY_RETURNALL = 0x000000FF;
        private const uint JOY_RETURNPOVCTS = 0x00000200;

        /// <summary>
        /// JOYCAPSW exakt nach dem nativen Windows-Layout (joystickapi.h / mmddk.h):
        /// WORD wMid; WORD wPid; WCHAR szPname[MAXPNAMELEN=32];
        /// DWORD wXmin..wZmax; DWORD wNumButtons; DWORD wPeriodMin; DWORD wPeriodMax;
        /// DWORD wRmin..wVmax; WORD wCaps; WORD wMaxAxes; WORD wNumAxes; WORD wMaxButtons;
        /// WCHAR szRegKey[MAXPNAMELEN=32]; WCHAR szOEMVxD[MAX_JOYSTICKOEMVXDNAME=260];
        /// Native Größe = 720 Byte. Windows vergleicht cbjc mit sizeof(JOYCAPS) und
        /// antwortet bei jeder Abweichung mit JOYERR_PARMS – dann gilt JEDER WinMM-Slot
        /// als unbelegt. Vorher fehlten wNumButtons/wPeriodMin/wPeriodMax, wCaps..
        /// wMaxButtons waren DWORD statt WORD und szOEMVxD nur 32 statt 260 Zeichen.
        /// </summary>
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct JOYCAPSW
        {
            public ushort wMid;
            public ushort wPid;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string szPname;
            public uint wXmin;
            public uint wXmax;
            public uint wYmin;
            public uint wYmax;
            public uint wZmin;
            public uint wZmax;
            public uint wNumButtons;
            public uint wPeriodMin;
            public uint wPeriodMax;
            public uint wRmin;
            public uint wRmax;
            public uint wUmin;
            public uint wUmax;
            public uint wVmin;
            public uint wVmax;
            public ushort wCaps;
            public ushort wMaxAxes;
            public ushort wNumAxes;
            public ushort wMaxButtons;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string szRegKey;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string szOEMVxD;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct JOYINFOEX
        {
            public int dwSize;
            public uint dwFlags;
            public int dwXpos;
            public int dwYpos;
            public int dwZpos;
            public int dwRpos;
            public int dwUpos;
            public int dwVpos;
            public uint dwButtons;
            public uint dwButtonNumber;
            public uint dwPOV;
            public uint dwReserved1;
            public uint dwReserved2;
        }


        // ============================================================
        //  SETUPAPI/HID-ENUMERATION ist nach Detection.HidControllerProvider
        //  ausgezogen (wird dort für Pipeline und Diagnose gemeinsam genutzt).
        // ============================================================

        // ============================================================
        //  HID DIREKTZUGRIFF (hid.dll) – Eingabe-Kanal für reine HID-Gamepads
        // ============================================================

        private static readonly Guid GUID_DEVINTERFACE_HID =
            new(0x4d1e55b2, 0xf16f, 0x11cf, 0x88, 0xcb, 0x00, 0x11, 0x11, 0x00, 0x00, 0x30);

        private const int HIDP_STATUS_SUCCESS = 0x00110000;
        private const int HIDP_REPORT_TYPE_INPUT = 1;
        private const int HIDP_REPORT_TYPE_FEATURE = 3;
        private const uint GENERIC_READ = 0x80000000;
        private const uint FILE_SHARE_READ = 0x00000001;
        private const uint FILE_SHARE_WRITE = 0x00000002;
        private const uint OPEN_EXISTING = 3;

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
            // Union: Range / NotRange – wir lesen nur die ersten beiden Felder (UsageMin/UsageMax)
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
        private static extern int HidP_GetButtonCaps(int ReportType, [Out] HIDP_BUTTON_CAPS[]? ButtonCaps, ref ushort ButtonCapsLength, IntPtr PreparsedData);

        [DllImport("hid.dll", SetLastError = true)]
        private static extern int HidP_GetUsages(int ReportType, ushort UsagePage, ushort LinkCollection, [Out] ushort[] UsageList, ref uint UsageLength, IntPtr PreparsedData, byte[] Report, uint ReportLength);

        [DllImport("hid.dll", SetLastError = true)]
        private static extern int HidP_GetUsageValue(int ReportType, ushort UsagePage, ushort LinkCollection, ushort Usage, out uint UsageValue, IntPtr PreparsedData, byte[] Report, uint ReportLength);

        [DllImport("hid.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "HidD_GetProductString")]
        private static extern bool HidD_GetProductString(SafeFileHandle HidDeviceObject, StringBuilder Buffer, uint BufferLength);

        [DllImport("hid.dll", SetLastError = true)]
        private static extern bool HidD_GetFeature(SafeFileHandle HidDeviceObject, byte[] ReportBuffer, int ReportBufferLength);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern SafeFileHandle CreateFile(string lpFileName, uint dwDesiredAccess, uint dwShareMode, IntPtr lpSecurityAttributes, uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool ReadFile(SafeFileHandle hFile, byte[] lpBuffer, uint nNumberOfBytesToRead, out uint lpNumberOfBytesRead, IntPtr lpOverlapped);

        // ============================================================
        //  RAW INPUT (user32.dll) – Eingabe-Kanal für HID-Gamepads.
        //  RawInput ist ein eigener Verteilweg im HID-Stack und liefert
        //  Reports auch dann, wenn andere Prozesse (z.B. Steam) das Gerät
        //  offen halten – derselbe Weg, den auch joy.cpl nutzt.
        // ============================================================

        private const uint WM_INPUT = 0x00FF;
        private const uint RIM_TYPEHID = 2;
        private const uint RID_INPUT = 0x10000003;
        private const uint RIDI_DEVICENAME = 0x00000002;
        private const uint RIDEV_INPUTSINK = 0x00000100;

        [StructLayout(LayoutKind.Sequential)]
        private struct RAWINPUTDEVICE
        {
            public ushort usUsagePage;
            public ushort usUsage;
            public uint dwFlags;
            public IntPtr hwndTarget;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RAWINPUTHEADER
        {
            public uint dwType;
            public uint dwSize;
            public IntPtr hDevice;
            public IntPtr wParam;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterRawInputDevices([In, MarshalAs(UnmanagedType.LPArray)] RAWINPUTDEVICE[] pRawInputDevices, uint uiNumDevices, uint cbSize);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint GetRawInputData(IntPtr hRawInput, uint uiCommand, IntPtr pData, ref uint pcbSize, uint cbSizeHeader);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern uint GetRawInputDeviceInfo(IntPtr hDevice, uint uiCommand, IntPtr pData, ref uint pcbSize);

        private const string ConnectionTypeUsb = "USB";
        private const string ConnectionTypeBluetooth = "Bluetooth";
        private const string ConnectionTypeWireless = "2.4 GHz";
        private const string ConnectionTypeUnknown = "Unknown";


        // ============================================================
        //  COMBINED CONTROLLER INFO
        // ============================================================

        private class ControllerInfo
        {
            public int SlotIndex;       // 0-3 for XInput, 0-15 for WinMM, -1 for HID-only
            public bool IsXInput;
            public string Name = "";
            public bool Connected;
            public string RealName = "";
            public string ConnectionType = ConnectionTypeUnknown;
            public string VidPid = "";
            public string InstanceId = "";
            public bool AccessError;
            public string AccessErrorReason = "";
            public bool WindowsInterfaceDisabled;
            public HidInputChannel? HidChannel;

            public bool HasInputChannel => !AccessError;

            /// <summary>
            /// Stabile Kennung für Auswahl und Verlauf. Bewusst über den kanonischen
            /// Instanz-Schlüssel: Windows schreibt dieselbe Instanz je nach Quelle in
            /// anderer Groß-/Kleinschreibung (`…9&8581b18&…` von Raw Input,
            /// `…9&8581B18&…` von der SetupAPI). Mit dem rohen String wäre dieselbe
            /// Hardware je nach Rescan-Ergebnis eine „andere“ – die Auswahl springt
            /// zurück und die Liste baut sich neu auf.
            /// </summary>
            public string StableId
            {
                get
                {
                    string instanceKey = DeviceHelpers.NormalizeInstanceKey(InstanceId);
                    if (instanceKey != "") return instanceKey;
                    if (!string.IsNullOrEmpty(VidPid)) return VidPid.Trim().ToUpperInvariant() + (IsXInput ? "_XINPUT" : "_WINMM");
                    return (IsXInput ? "XINPUT_SLOT_" : "WINMM_SLOT_") + SlotIndex;
                }
            }
        }

        // HID-Enumerationstyp: Detection.HidGamepadInfo (via using-Alias oben)

        /// <summary>
        /// HID-Eingabekanal (hid.dll + RawInput): hält PreparsedData und den aktuellen
        /// Zustand (Tasten/Achsen) als Snapshot. Die Reports kommen über RawInput
        /// (WM_INPUT) – derselbe Verteilweg wie joy.cpl. Er funktioniert auch, wenn
        /// andere Prozesse das Gerät offen halten (z.B. Steam), was bei direkten
        /// ReadFile-Reads nicht der Fall ist.
        /// </summary>
        private class HidInputChannel : IDisposable
        {
            private IntPtr _preparsed = IntPtr.Zero;
            private SafeFileHandle? _readHandle;
            private string _devicePath = "";
            private Thread? _readerThread;
            private volatile bool _running;

            // Verbindungszustand des direkten Lesers: _deviceGone wird gesetzt, wenn
            // der Handle nach einem Lesefehler nicht neu geöffnet werden kann
            // (Bluetooth getrennt). Dann wird der Eingabezustand geleert, damit keine
            // gedrückte Taste und kein gehaltener Stick eingefroren weiterlebt.
            private volatile bool _deviceGone;

            // Geräte, die KEINE synchronen Reads erlauben (ReadFile scheitert sofort,
            // Eingaben kommen über RawInput): dort ist ein fehlender Handle kein
            // Trennsignal, der Kanal bleibt als verbunden geführt.
            private volatile bool _directReadUnavailable;

            // (UsagePage, Usage, LogicalMin, LogicalMax) der relevanten Achsen
            private readonly List<(ushort Page, ushort Usage, int LogMin, int LogMax)> _axes = new();
            private bool _hasRightStick;
            // Manueller Layout-Fallback (ManualReportParser) nur für Geräte, die
            // NICHT über XInput gelesen werden. Bei XInput-Pads spiegelt die
            // 16-Byte-HID-Sammlung (xinputhid) das XInput-Format – der 8BitDo-Parser
            // deutet das als "R2 gedrückt" und produziert zappelnde Sticks.
            private bool _manualFallback;
            private DateTime _lastInputLog = DateTime.MinValue;
            private int _lastUsagesRc = int.MinValue;
            private string _lastFallbackKey = "";

            private readonly object _stateLock = new();
            private List<string> _pressed = new();
            private float _lx, _ly, _rx, _ry;
            private DateTime _lastStateAt = DateTime.MinValue;
            private volatile bool _stateDirty;

            public bool IsRunning => _running;

            /// <summary>
            /// true, solange das Gerät über den direkten Leser erreichbar ist. Der
            /// Rescan führt den Controller danach als getrennt bzw. wieder verbunden –
            /// ohne diese Information bliebe ein per Bluetooth getrenntes Pad bis zum
            /// App-Neustart als „verbunden“ mit eingefrorener Eingabe stehen.
            /// </summary>
            public bool IsDeviceConnected => _running && (_directReadUnavailable || !_deviceGone);

            /// <summary>true, wenn dieser Kanal Reports per ManualReportParser auswertet.</summary>
            public bool AllowsManualFallback => _manualFallback;

            /// <summary>Länge des Input-Reports in Bytes (für RawInput-Routing ohne Gerätename).</summary>
            public ushort ReportLength { get; private set; }

            /// <summary>Produktname aus dem HID-Descriptor (z.B. "8BitDo Ultimate 2C").</summary>
            public string? ProductName;

            public static HidInputChannel? TryOpen(HidDeviceInfo hid, bool enableManualFallback = true)
            {
                try
                {
                    string instance = hid.InstanceId;
                    if (string.IsNullOrEmpty(instance) || !instance.StartsWith("HID\\", StringComparison.OrdinalIgnoreCase))
                        return null;

                    // Geräte-Interface-Pfad aus der Instanz-ID aufbauen:
                    // HID\VID_x&PID_y\Instanz  →  \\?\HID#VID_x&PID_y#Instanz#{4d1e55b2-...}
                    string path = "\\\\?\\" + instance.Replace('\\', '#') + "#" + GUID_DEVINTERFACE_HID.ToString("B");

                    // Nur GENERIC_READ: der Handle bleibt für direkte ReadFile-Reads
                    // geöffnet (Fallback, wenn RawInput für ein Gerät keine Reports
                    // liefert – z.B. BLE-HID-Gamepads wie der 8BitDo). Ein
                    // Schreibrecht wird nirgends benutzt; es ist nur zusätzlicher
                    // Zugriff auf ein Gerät, das auch DirectInput (Windows-Panel,
                    // joy.cpl) und Spiele öffnen.
                    var handle = CreateFile(path, GENERIC_READ,
                        FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
                    if (handle.IsInvalid) return null;

                    try
                    {
                        if (!HidD_GetPreparsedData(handle, out IntPtr prep) || prep == IntPtr.Zero)
                        {
                            handle.Dispose();
                            return null;
                        }

                        if (HidP_GetCaps(prep, out var caps) != HIDP_STATUS_SUCCESS || caps.InputReportByteLength == 0)
                        {
                            HidD_FreePreparsedData(prep);
                            handle.Dispose();
                            return null;
                        }

                        // Die HID-Fassade eines XInput-Pads (Windows erzeugt sie als
                        // VID_045E&PID_02FF, „XINPUT-kompatibles HID-Gerät") deklariert
                        // keine Usages und liefert einen 16-Byte-XInput-Rahmen. Der
                        // 8BitDo-FallbackParser deutet darin Achsenbytes als R2 und
                        // Hat-Wert und erzeugt so dauerhaft „R2 gedrückt" plus eine
                        // Richtung (gemessen 26.10.2026, mehrere Rahmenvarianten:
                        // 00 00 80 FF 7F … und 00 00 80 00 80 …). Solche Geräte werden
                        // ausschließlich über XInput gelesen, nie über den Fallback.
                        bool fallbackAllowed = enableManualFallback && !IsXInputHidFacade(hid.Vid, hid.Pid);

                        var channel = new HidInputChannel
                        {
                            _preparsed = prep,
                            _readHandle = handle,
                            _devicePath = path,
                            _manualFallback = fallbackAllowed,
                            ProductName = ReadProductString(handle),
                            ReportLength = caps.InputReportByteLength
                        };
                        channel.LoadValueCaps();
                        channel._running = true;
                        channel.StartDirectReader();
                        return channel;
                    }
                    catch
                    {
                        try { handle.Dispose(); } catch { }
                        return null;
                    }
                }
                catch
                {
                    return null;
                }
            }

            /// <summary>
            /// Startet den Hintergrund-Leser, der Input-Reports direkt per ReadFile
            /// vom Gerät holt. RawInput liefert für manche Geräte (BLE-HID wie der
            /// 8BitDo Ultimate MG) trotz korrekter Registrierung keine Reports – der
            /// direkte Read ist dann der funktionierende Weg. Läuft er zusätzlich zu
            /// RawInput, ist die doppelte Zustellung harmlos (Zustand ist idempotent).
            /// </summary>
            private void StartDirectReader()
            {
                if (_readHandle == null || _readHandle.IsInvalid) return;
                var thread = new Thread(DirectReaderLoop)
                {
                    IsBackground = true,
                    Name = "TenzoraX-HidReader"
                };
                _readerThread = thread;
                thread.Start();
            }

            private void DirectReaderLoop()
            {
                var handle = _readHandle;
                if (handle == null || handle.IsInvalid) return;
                var buffer = new byte[Math.Max((int)ReportLength, 8) + 1];
                int consecutiveReopenFailures = 0;
                int consecutiveReadFailures = 0;
                while (_running)
                {
                    if (handle == null || handle.IsInvalid)
                    {
                        // Gerät weg (BLE-Aussetzer/Reconnect) → kurze Pause, dann neu
                        // öffnen. Ohne diesen Fallback bliebe die Eingabe tot, bis die
                        // App neu startet (der Rescan ersetzt den Kanal nur, wenn die
                        // Geräteliste sich ändert).
                        Thread.Sleep(1000);
                        if (!_running) return;
                        handle = ReopenDeviceHandle();
                        if (handle == null)
                        {
                            consecutiveReopenFailures++;
                            // Gerät ist weg: liegengebliebene Eingaben zurücksetzen.
                            // Ein kurzer Aussetzer (< 1 s) landet hier nie, weil erst
                            // nach dem Öffnungsversuch entschieden wird.
                            if (!_deviceGone)
                            {
                                _deviceGone = true;
                                ClearState();
                                App.LogApp("HID-Leser: Gerät getrennt – Eingabezustand zurückgesetzt.");
                            }
                            // Log-Spam vermeiden: nur jeden 10. Fehlversuch melden.
                            if (consecutiveReopenFailures % 10 == 1)
                                App.LogApp("HID-Leser: Gerät nicht erreichbar – versuche weiter...");
                            // Dauerhaft getrennt (z.B. Verbindung von BLE auf 2.4G
                            // gewechselt): Kanal aufgeben – der nächste Rescan legt bei
                            // Wiederverbindung einen neuen Kanal an. Vorher hätte der
                            // Leser endlos jede Sekunde gesponnen.
                            if (consecutiveReopenFailures >= 30)
                            {
                                App.LogApp("HID-Leser: Kanal aufgegeben – Gerät dauerhaft getrennt (wartet auf Rescan).");
                                _running = false;
                                return;
                            }
                            continue;
                        }
                        consecutiveReopenFailures = 0;
                        _readHandle = handle;
                        _deviceGone = false;
                        App.LogApp("HID-Leser: Gerät wieder verbunden – Lesevorgang fortgesetzt.");
                        continue;
                    }

                    uint bytesRead = 0;
                    bool readOk;
                    try
                    {
                        readOk = ReadFile(handle, buffer, (uint)buffer.Length, out bytesRead, IntPtr.Zero);
                    }
                    catch
                    {
                        readOk = false;
                    }
                    if (!readOk)
                    {
                        consecutiveReadFailures++;
                        // Manche Geräte (z.B. der 8BitDo-2.4G-Dongle) unterstützen KEINE
                        // synchronen Reads – ReadFile schlägt sofort mit
                        // ERROR_INVALID_PARAMETER fehl. Die Eingaben kommen dort über
                        // RawInput. Der Kanal (Preparsed-Data fürs RawInput-Routing)
                        // bleibt dabei erhalten – nur der Lese-Thread beendet sich.
                        if (consecutiveReadFailures >= 3)
                        {
                            _directReadUnavailable = true;
                            App.LogApp("HID-Leser: Direkt-Lesen nicht möglich (err=" +
                                System.Runtime.InteropServices.Marshal.GetLastWin32Error() +
                                ") – nutze RawInput, Lese-Thread beendet.");
                            try { handle.Dispose(); } catch { }
                            _readHandle = null;
                            return;
                        }
                        // Lesefehler = Gerät getrennt → Handle schließen, neu öffnen.
                        App.LogApp("HID-Leser: Lesevorgang abgebrochen (Gerät getrennt) – versuche neu zu öffnen.");
                        try { handle.Dispose(); } catch { }
                        handle = null;
                        _readHandle = null;
                        continue;
                    }
                    consecutiveReadFailures = 0;
                    _deviceGone = false;
                    if (bytesRead > 0)
                    {
                        var report = new byte[bytesRead];
                        Array.Copy(buffer, report, bytesRead);
                        try { ProcessRawReport(report); } catch { }
                    }
                }
            }

            /// <summary>Öffnet den Geräte-Handle erneut (nach BLE-Trennung).</summary>
            private SafeFileHandle? ReopenDeviceHandle()
            {
                if (string.IsNullOrEmpty(_devicePath)) return null;
                try
                {
                    var h = CreateFile(_devicePath, GENERIC_READ,
                        FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
                    return h.IsInvalid ? null : h;
                }
                catch
                {
                    return null;
                }
            }

            private static bool IsTrackedUsage(ushort usage)
            {
                return usage == 0x30 || usage == 0x31 || usage == 0x32 || usage == 0x33 ||
                       usage == 0x34 || usage == 0x35 || usage == 0x39 || usage == 0xC2 || usage == 0xC3;
            }

            private static string? ReadProductString(SafeFileHandle handle)
            {
                try
                {
                    var sb = new StringBuilder(260);
                    if (HidD_GetProductString(handle, sb, (uint)sb.Capacity * sizeof(char)) && sb.Length > 0)
                        return sb.ToString().Trim();
                }
                catch { }
                return null;
            }

            private void LoadValueCaps()
            {
                ushort count = 0;
                HidP_GetValueCaps(HIDP_REPORT_TYPE_INPUT, null, ref count, _preparsed);
                App.LogApp("HID-LoadValueCaps: count=" + count + " preparsed=0x" + _preparsed.ToString("X"));
                if (count > 0)
                {
                    var caps = new HIDP_VALUE_CAPS[count];
                    if (HidP_GetValueCaps(HIDP_REPORT_TYPE_INPUT, caps, ref count, _preparsed) == HIDP_STATUS_SUCCESS)
                    {
                        for (int i = 0; i < count; i++)
                        {
                            var c = caps[i];
                            App.LogApp("HID-ValueCap[" + i + "]: Page=0x" + c.UsagePage.ToString("X4") + " Usage=0x" + c.Range_UsageMin.ToString("X4") + " LogMin=" + c.LogicalMin + " LogMax=" + c.LogicalMax + " BitSize=" + c.BitSize);
                            if (c.UsagePage != 1) continue;
                            ushort usage = c.Range_UsageMin;   // Union: entspricht Usage im NotRange-Fall
                            if (IsTrackedUsage(usage))
                                _axes.Add((c.UsagePage, usage, c.LogicalMin, c.LogicalMax));
                        }
                    }
                    else App.LogApp("HID-LoadValueCaps: HidP_GetValueCaps fehlgeschlagen");
                }
                _hasRightStick = _axes.Exists(a => a.Usage == 0x33 || a.Usage == 0x34);
                App.LogApp("HID-LoadValueCaps: _axes=" + _axes.Count + " hasRightStick=" + _hasRightStick + " ReportLen=" + ReportLength);
                foreach (var a in _axes) App.LogApp("HID-Axis: Page=0x" + a.Page.ToString("X4") + " Usage=0x" + a.Usage.ToString("X4") + " Min=" + a.LogMin + " Max=" + a.LogMax);

                ushort btnCount = 0;
                HidP_GetButtonCaps(HIDP_REPORT_TYPE_INPUT, null, ref btnCount, _preparsed);
                App.LogApp("HID-LoadButtonCaps: count=" + btnCount);
                if (btnCount > 0)
                {
                    var btnCaps = new HIDP_BUTTON_CAPS[btnCount];
                    if (HidP_GetButtonCaps(HIDP_REPORT_TYPE_INPUT, btnCaps, ref btnCount, _preparsed) == HIDP_STATUS_SUCCESS)
                    {
                        for (int i = 0; i < btnCount; i++)
                            App.LogApp("HID-ButtonCap[" + i + "]: Page=0x" + btnCaps[i].UsagePage.ToString("X4") + " UsageMin=0x" + btnCaps[i].Range_UsageMin.ToString("X4") + " UsageMax=0x" + btnCaps[i].Range_UsageMax.ToString("X4") + " IsRange=" + btnCaps[i].IsRange + " ReportID=" + btnCaps[i].ReportID);
                    }
                }
            }

            /// <summary>Setzt den Eingabezustand auf neutral (Trennung: nichts bleibt „gedrückt“).</summary>
            private void ClearState()
            {
                lock (_stateLock)
                {
                    _pressed = new List<string>();
                    _lx = _ly = _rx = _ry = 0f;
                    _stateDirty = true;
                }
            }

            /// <summary>
            /// Verarbeitet einen per RawInput gelieferten Input-Report. Der Report
            /// enthält die ReportID als erstes Byte, falls das Gerät ReportIDs
            /// verwendet – identisch zum ReadFile-Format, daher kompatibel mit
            /// HidP_GetUsages/HidP_GetUsageValue.
            /// </summary>
            public void ProcessRawReport(byte[] report)
            {
                var pressed = new List<string>(16);
                float lx = 0, ly = 0, rx = 0, ry = 0;
                bool hidFound = false;
                bool doLog = (DateTime.UtcNow - _lastInputLog).TotalMilliseconds > 1000;
                if (doLog) _lastInputLog = DateTime.UtcNow;

                // Gedrückte Tasten (Usage Page 9 = Button)
                var usages = new ushort[32];
                uint usageCount = (uint)usages.Length;
                int hidUsagesRc = HidP_GetUsages(HIDP_REPORT_TYPE_INPUT, 9, 0, usages, ref usageCount, _preparsed, report, (uint)report.Length);
                // Returncode-Log nur bei Änderung – ein Gerät ohne Button-Usages
                // (z.B. 8BitDo BLE) würde sonst jede Sekunde eine Zeile schreiben.
                // (usageCount bleibt bei Fehlschlag auf dem Initialwert und ist
                // daher als „Buttons gefunden“-Signal ungeeignet.)
                if (hidUsagesRc != _lastUsagesRc)
                    App.LogApp("HID-GetUsages rc=0x" + hidUsagesRc.ToString("X8") + " count=" + usageCount + " preparsed=0x" + _preparsed.ToString("X"));
                _lastUsagesRc = hidUsagesRc;
                if (hidUsagesRc == HIDP_STATUS_SUCCESS)
                {
                    if (usageCount > 0) hidFound = true;
                    for (uint i = 0; i < usageCount; i++)
                        MapButton(usages[i], pressed);
                    if (doLog && usageCount > 0) App.LogApp("HID-Usages: " + string.Join(",", usages[..(int)usageCount]));
                }

                // Achsen / Sticks / Trigger (Usage Page 1)
                foreach (var (page, usage, logMin, logMax) in _axes)
                {
                    if (HidP_GetUsageValue(HIDP_REPORT_TYPE_INPUT, page, 0, usage, out uint raw, _preparsed, report, (uint)report.Length) != HIDP_STATUS_SUCCESS)
                        continue;
                    hidFound = true;

                    switch (usage)
                    {
                        case 0x30: lx = NormalizeHid(raw, logMin, logMax); break;
                        // Y-Achsen: HID meldet "oben" meist als Minimum, im Programm ist oben +1.
                        case 0x31: ly = -NormalizeHid(raw, logMin, logMax); break;
                        case 0x33: rx = NormalizeHid(raw, logMin, logMax); break;
                        case 0x34: ry = -NormalizeHid(raw, logMin, logMax); break;
                        case 0x32: if (!_hasRightStick) rx = NormalizeHid(raw, logMin, logMax); break;
                        case 0x35: if (!_hasRightStick) ry = -NormalizeHid(raw, logMin, logMax); break;
                        case 0x39: MapHat(raw, pressed); break;
                        case 0xC2: if (NormalizeHid(raw, logMin, logMax) > 0.3f) pressed.Add("L2"); break;
                        case 0xC3: if (NormalizeHid(raw, logMin, logMax) > 0.3f) pressed.Add("R2"); break;
                    }
                }

                // Manueller Fallback für Geräte, deren Report keine Standard-HID-Usages
                // deklariert (z.B. 8BitDo Ultimate Mobile). Das Byte-Layout ist in
                // Detection.ManualReportParser dokumentiert und unit-getestet.
                if (!hidFound && _manualFallback && report.Length >= 10)
                {
                    var fb = ManualReportParser.Parse(report);
                    pressed.AddRange(fb.Pressed);
                    lx = fb.Lx; ly = fb.Ly; rx = fb.Rx; ry = fb.Ry;

                    // Änderungsbasiertes Logging über die parser-relevanten Bytes
                    // ([1]=Hat, [2..5]=Sticks, [6..7]=Trigger, [8..9]=Buttons).
                    // Reine IMU-/Checksummen-Änderungen (z.B. Bytes [15]/[17..26]
                    // beim 8BitDo) würden sonst hunderte Log-Zeilen pro Sekunde
                    // erzeugen – der volle Hex-Dump erscheint nur bei echter
                    // Eingabeänderung (nützlich für Layout-Rekonstruktion).
                    string fullHex = BitConverter.ToString(report).Replace("-", " ");
                    string relKey = string.Join("-", report.Skip(1).Take(9).Select(b => b.ToString("X2")));
                    if (relKey != _lastFallbackKey)
                    {
                        _lastFallbackKey = relKey;
                        App.LogApp("HID-Fallback [" + (ProductName ?? "?") + "]: len=" + report.Length + " hex=" + fullHex + " btns=" + string.Join(",", fb.Pressed));
                    }
                }

                if (lx < -0.7f) pressed.Add("LS_LEFT");
                else if (lx > 0.7f) pressed.Add("LS_RIGHT");
                if (ly > 0.7f) pressed.Add("LS_UP");
                else if (ly < -0.7f) pressed.Add("LS_DOWN");

                if (rx < -0.7f) pressed.Add("RS_LEFT");
                else if (rx > 0.7f) pressed.Add("RS_RIGHT");
                if (ry > 0.7f) pressed.Add("RS_UP");
                else if (ry < -0.7f) pressed.Add("RS_DOWN");

                lock (_stateLock)
                {
                    _pressed = pressed;
                    _lx = lx; _ly = ly; _rx = rx; _ry = ry;
                    _lastStateAt = DateTime.UtcNow;
                    _stateDirty = true;
                }
            }

            private static void MapButton(ushort usage, List<string> pressed)
            {
                switch (usage)
                {
                    case 1: pressed.Add("A"); break;
                    case 2: pressed.Add("B"); break;
                    case 3: pressed.Add("X"); break;
                    case 4: pressed.Add("Y"); break;
                    case 5: pressed.Add("L1"); break;
                    case 6: pressed.Add("R1"); break;
                    case 7: pressed.Add("SELECT"); break;
                    case 8: pressed.Add("START"); break;
                    case 10: pressed.Add("L3"); break;
                    case 11: pressed.Add("R3"); break;
                    case 12: pressed.Add("DPAD_UP"); break;
                    case 13: pressed.Add("DPAD_DOWN"); break;
                    case 14: pressed.Add("DPAD_LEFT"); break;
                    case 15: pressed.Add("DPAD_RIGHT"); break;
                }
            }

            private static void MapHat(uint raw, List<string> pressed)
            {
                if (raw == 0xFFFF || raw == 0xFF) return;
                uint r = raw;
                if (r < 8) r *= 4500;   // manche Geräte liefern 1-8 statt Winkel*100
                if (r <= 4500 || r >= 31500) pressed.Add("DPAD_UP");
                if (r >= 4500 && r <= 13500) pressed.Add("DPAD_RIGHT");
                if (r >= 13500 && r <= 22500) pressed.Add("DPAD_DOWN");
                if (r >= 22500 && r <= 31500) pressed.Add("DPAD_LEFT");
            }

            private static float NormalizeHid(uint value, int logMin, int logMax)
            {
                if (logMax <= logMin) return 0f;
                float center = (logMin + logMax) / 2f;
                float range = (logMax - logMin) / 2f;
                if (range < 1) return 0f;
                return Math.Clamp((value - center) / range, -1f, 1f);
            }

            /// <summary>Holt den aktuellen Zustand; false, solange noch kein Report gelesen wurde.</summary>
            public bool TryGetSnapshot(out List<string> pressed, out float lx, out float ly, out float rx, out float ry)
            {
                lock (_stateLock)
                {
                    if (_lastStateAt == DateTime.MinValue)
                    {
                        pressed = new List<string>();
                        lx = ly = rx = ry = 0f;
                        return false;
                    }
                    bool dirty = _stateDirty;
                    _stateDirty = false;
                    pressed = new List<string>(_pressed);
                    lx = _lx; ly = _ly; rx = _rx; ry = _ry;
                    return dirty;
                }
            }

            public void Dispose()
            {
                _running = false;
                // Handle schließen beendet einen blockierten ReadFile-Lesevorgang.
                try { _readHandle?.Dispose(); _readHandle = null; } catch { }

                // Den Lese-Thread abwarten, bevor die Preparsed-Daten freigegeben
                // werden: ProcessRawReport greift im Thread darauf zu. Ohne Warten
                // entstand ein Use-after-free beim Austausch eines Kanals (Rescan).
                var reader = _readerThread;
                if (reader != null && reader != Thread.CurrentThread)
                {
                    try { reader.Join(2500); } catch { }
                    if (reader.IsAlive) return; // Sicherer, als den Speicher noch benutzt freizugeben.
                }
                _readerThread = null;

                try
                {
                    if (_preparsed != IntPtr.Zero)
                    {
                        HidD_FreePreparsedData(_preparsed);
                        _preparsed = IntPtr.Zero;
                    }
                }
                catch { }
            }
        }


        // ============================================================
        //  SINGLETON
        // ============================================================

        private static ControllerManager? _instance;
        private static readonly object _instanceLock = new();

        public static ControllerManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_instanceLock) { _instance ??= new ControllerManager(); }
                }
                return _instance;
            }
        }

        // ============================================================
        //  FIELDS
        // ============================================================

        private volatile bool _isRunning;
        private Thread? _pollThread;
        private readonly List<ControllerInfo> _controllers = new();
        private int _selectedIndex = -1;
        private string _userSelectedStableId = "";
        private string _lastUsedStableId = "";
        private bool _hasEverConnected = false;

        /// <summary>
        /// Wiederauswahl-Modus nach vollständigem Trennen des Controllers:
        /// "first" = ersten verbundenen Controller übernehmen,
        /// "last"  = zuletzt verwendeten Controller (anhand der StableId) bevorzugen.
        /// </summary>
        public string ReselectMode { get; set; } = "first";
        private int _winmmCount = 0;

        // Cooldown per slot after a disconnect: avoid native API calls (xinput/winmm)
        // while the driver is still tearing the device down (heap corruption risk).
        private static readonly TimeSpan DisconnectCooldown = TimeSpan.FromMilliseconds(1500);
        private readonly Dictionary<int, DateTime> _xinputCooldownUntil = new();
        private readonly Dictionary<int, DateTime> _winmmCooldownUntil = new();
        private readonly object _cooldownLock = new();

        // Rescan läuft auf einem eigenen Thread, damit die teure Geräte-Enumeration
        // (~200 ms auf manchen Systemen) den Poll-Thread nicht blockiert.
        private readonly object _rescanLock = new();
        private Thread? _rescanThread;
        private bool _rescanRunning;
        // Kanäle getrennter Geräte: erst im nächsten Zyklus freigeben, damit der
        // Poll-Thread nie einen freigegebenen Kanal benutzt.
        private readonly List<HidInputChannel> _retiredChannels = new();

        // Zahl der DirectInput-Gamepads aus dem letzten Rescan (für den Widerspruchs-Hinweis).
        private int _lastDirectInputCount;

        // Solange das Windows-Panel „Gamecontroller“ offen ist, pausiert die
        // DirectInput-Aufzählung (siehe MergeAdditionalSources).
        private bool _directInputPausedByPanel;

        // Selbstreparatur eines DirectInput-Staus (höchstens ein Lauf, höchstens alle 10 min).
        private static int _diRepairRunning;
        private static DateTime _diRepairLastUtc = DateTime.MinValue;
        private static readonly object _diRepairLock = new();

        /// <summary>
        /// Hinweis für die Oberfläche, wenn die App ein Pad listet, Windows aber weder
        /// XInput noch DirectInput ein Gerät führt. Leer, solange alles zusammenpasst.
        /// </summary>
        public string InputVisibilityWarning { get; private set; } = "";

        /// <summary>
        /// Instanz-ID der deaktivierten Schnittstelle, auf die sich der Hinweis bezieht –
        /// damit die Oberfläche sie direkt wieder einschalten kann.
        /// </summary>
        public string DisabledInterfaceId { get; private set; } = "";

        /// <summary>
        /// Zeitpunkt der letzten Änderung an der Controller-Liste (UTC). Das Gamecontroller-Panel
        /// wird erst geöffnet, wenn diese Änderung lange genug zurückliegt – mitten in einem
        /// Reconnect bleibt das Windows-Panel sonst unsichtbar hängen (RunDLL-Absturz).
        /// </summary>
        public DateTime LastControllerChangeUtc { get; private set; } = DateTime.MinValue;

        // Zuletzt zusammengefasste Doppel-Einträge der Controller-Liste (für den Diagnosebericht).
        private const int MaxCollapsedInstanceRecords = 10;
        private readonly List<CollapsedInstanceRecord> _collapsedInstances = new();
        private readonly object _collapsedInstancesLock = new();

        private string _lastRescanSignature = "";
        private DateTime _lastRescanAt = DateTime.MinValue;
        private string _lastWinmmSignature = "";
        private int _lastFindingsCount = -1;
        private bool _loggedXInputHidSkip;

        // ViGEm-Xbox-Spiegelung: Überträgt den aktiven Controller auf einen virtuellen
        // Xbox-360-Controller (sofern der ViGEmBus-Treiber installiert ist).
        private readonly VigemXboxBridge _vigemBridge = new();
        private volatile bool _vigemEnabled;

        /// <summary>Poll-Rate: ~250 Hz (4 ms). Erfordert 1-ms-Timer-Auflösung (timeBeginPeriod).</summary>
        private const int PollIntervalMs = 4;

        public event EventHandler<ControllerStateEventArgs>? StateChanged;
        public event EventHandler? GamepadsChanged;

        public List<string> ConnectedControllerNames
        {
            get
            {
                var names = new List<string>();
                lock (_controllers)
                {
                    for (int i = 0; i < _controllers.Count; i++)
                    {
                        if (_controllers[i].Connected)
                            names.Add(_controllers[i].Name);
                    }
                }
                return names;
            }
        }

        /// <summary>Alle aktuell verbundenen Controller mit vollständigen Geräteinformationen (für die UI).</summary>
        public List<DetectedController> ConnectedControllers
        {
            get
            {
                var list = new List<DetectedController>();
                lock (_controllers)
                {
                    foreach (var c in _controllers)
                    {
                        if (!c.Connected) continue;
                        list.Add(new DetectedController
                        {
                            SlotIndex = c.SlotIndex,
                            IsXInput = c.IsXInput,
                            Name = c.Name,
                            ConnectionType = c.ConnectionType,
                            VidPid = c.VidPid,
                            InstanceId = c.InstanceId,
                            AccessError = c.AccessError,
                            AccessErrorReason = c.AccessErrorReason,
                            WindowsInterfaceDisabled = c.WindowsInterfaceDisabled,
                            Connected = true,
                            StableId = c.StableId
                        });
                    }
                }
                return CollapseDuplicateInstances(list, LogCollapsedInstanceDuplicate);
            }
        }

        /// <summary>
        /// Bereits protokollierte Zusammenfassungen (kanonischer Schlüssel). Ohne diese
        /// Bremse schriebe jeder UI-Abgleich eine Zeile: `ConnectedControllers` wird bei
        /// jedem Rescan (alle 300 ms) und bei jeder Statusabfrage gelesen, und
        /// `App.LogApp` hängt synchron an die Datei.
        /// </summary>
        private static readonly HashSet<string> LoggedInstanceCollapses = new(StringComparer.Ordinal);

        /// <summary>
        /// Protokolliert, dass zwei Listeneinträge dieselbe Geräteinstanz waren – einmal pro
        /// Gerät und Programmlauf, mit beiden Schreibweisen, damit sich solche Fälle in
        /// freier Wildbahn nachweisen lassen.
        /// </summary>
        private void LogCollapsedInstanceDuplicate(DetectedController kept, DetectedController dropped)
        {
            string key = DeviceHelpers.NormalizeInstanceKey(kept.InstanceId);

            // Für den Diagnosebericht: gleiche Zusammenfassung nur einmal führen – der
            // Abgleich läuft alle 300 ms und würde sonst dieselbe Zeile ständig neu anlegen.
            lock (_collapsedInstancesLock)
            {
                bool known = _collapsedInstances.Any(r =>
                    r.Key == key &&
                    string.Equals(r.DroppedInstanceId, dropped.InstanceId, StringComparison.Ordinal));
                if (!known)
                {
                    _collapsedInstances.Add(new CollapsedInstanceRecord
                    {
                        At = DateTime.Now,
                        Key = key,
                        KeptName = kept.Name,
                        KeptInstanceId = kept.InstanceId,
                        KeptHasAccess = !kept.AccessError,
                        DroppedName = dropped.Name,
                        DroppedInstanceId = dropped.InstanceId,
                        DroppedHasAccess = !dropped.AccessError
                    });
                    if (_collapsedInstances.Count > MaxCollapsedInstanceRecords)
                        _collapsedInstances.RemoveAt(0);
                }
            }

            lock (LoggedInstanceCollapses)
            {
                if (!LoggedInstanceCollapses.Add(key)) return;
            }

            string spelling = string.Equals(kept.InstanceId, dropped.InstanceId, StringComparison.Ordinal)
                ? "gleiche Schreibweise"
                : "Schreibweise: '" + kept.InstanceId + "' vs '" + dropped.InstanceId + "'";

            App.LogApp("Liste: doppelte Instanz zusammengefasst – behalten '" + kept.Name + "' (" +
                       (kept.AccessError ? "ohne Zugriff" : "mit Zugriff") + "), verworfen '" +
                       dropped.Name + "' (" + (dropped.AccessError ? "ohne Zugriff" : "mit Zugriff") + "); " +
                       spelling + "; Schlüssel " + key);
        }

        /// <summary>
        /// Rendert die zuletzt zusammengefassten Listeneinträge für den Diagnosebericht.
        /// Rein rechnend (testbar), damit die Ausgabe nicht an der Konsole hängt.
        /// </summary>
        internal static string FormatCollapsedInstances(IReadOnlyList<CollapsedInstanceRecord>? records)
        {
            if (records == null || records.Count == 0)
                return "  (keine – die Liste hatte bisher keine Dubletten)" + Environment.NewLine;

            var sb = new StringBuilder();
            foreach (var r in records)
            {
                sb.AppendLine("  [" + r.At.ToString("HH:mm:ss") + "] behalten '" + r.KeptName + "' (" +
                              (r.KeptHasAccess ? "mit Zugriff" : "ohne Zugriff") + "), verworfen '" +
                              r.DroppedName + "' (" + (r.DroppedHasAccess ? "mit Zugriff" : "ohne Zugriff") + ")");
                sb.AppendLine("       Schreibweise: '" + r.KeptInstanceId + "' vs '" + r.DroppedInstanceId + "'");
                sb.AppendLine("       Schlüssel:    " + r.Key);
            }
            return sb.ToString();
        }

        /// <summary>
        /// Entfernt Einträge, die dieselbe Geräteinstanz ein zweites Mal führen – die
        /// Auswahlliste im Hauptfenster zeigt die Liste unverändert an, deshalb muss die
        /// Dublette hier verschwinden.
        ///
        /// Verglichen wird über den kanonischen Instanz-Schlüssel, nicht über den rohen
        /// String: `HID\VID_x&amp;PID_y\9&amp;8581b18` (Raw Input) und
        /// `HID\VID_x&amp;PID_y\9&amp;8581B18` (SetupAPI) sind dasselbe Gerät. Einträge
        /// ohne Instanz-ID (XInput-Slot, WinMM ohne HID-Treffer) bleiben unangetastet –
        /// dort ist die VID/PID nicht eindeutig genug, um zwei Exemplare zu unterscheiden.
        /// Bei Gleichstand gewinnt der Eintrag mit Zugriff auf den Eingabekanal.
        /// </summary>
        /// <param name="onCollapsed">Meldet jedes zusammengefasste Paar (behalten, verworfen);
        /// die Produktion hängt dort das Log ein, Tests lassen das Argument weg.</param>
        internal static List<DetectedController> CollapseDuplicateInstances(
            IEnumerable<DetectedController> controllers,
            Action<DetectedController, DetectedController>? onCollapsed = null)
        {
            var result = new List<DetectedController>();
            var indexByKey = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var c in controllers)
            {
                if (c == null) continue;
                string key = DeviceHelpers.NormalizeInstanceKey(c.InstanceId);
                if (key == "")
                {
                    result.Add(c);
                    continue;
                }
                if (indexByKey.TryGetValue(key, out int existing))
                {
                    DetectedController prior = result[existing];
                    if (prior.AccessError && !c.AccessError)
                    {
                        result[existing] = c;
                        onCollapsed?.Invoke(c, prior);
                    }
                    else
                    {
                        onCollapsed?.Invoke(prior, c);
                    }
                    continue;
                }
                indexByKey[key] = result.Count;
                result.Add(c);
            }
            return result;
        }

        public int ActiveGamepadIndex
        {
            get => _selectedIndex;
            set { if (value >= 0) _selectedIndex = value; }
        }

        /// <summary>Wählt den Controller über seine stabile Gerätekennung aus (bleibt beim Rescan erhalten).</summary>
        public void SetActiveController(string stableId)
        {
            if (string.IsNullOrEmpty(stableId))
            {
                _userSelectedStableId = "";
                _hasEverConnected = true;
                _selectedIndex = -1;
                return;
            }
            _userSelectedStableId = stableId;
            _lastUsedStableId = stableId;
            _hasEverConnected = true;
            lock (_controllers)
            {
                int pos = 0;
                foreach (var c in _controllers)
                {
                    if (c.Connected)
                    {
                        if (c.StableId == stableId) { _selectedIndex = pos; return; }
                        pos++;
                    }
                }
            }
        }

        public bool HasConnectedGamepad
        {
            get
            {
                lock (_controllers)
                {
                    foreach (var c in _controllers)
                        if (c.Connected) return true;
                }
                return false;
            }
        }

        /// <summary>ViGEm-Spiegelung ein-/ausschalten. Verbindet bzw. trennt sofort den virtuellen Xbox-Controller.</summary>
        public bool SetVigemXbox(bool enabled)
        {
            _vigemEnabled = enabled;
            if (enabled)
                return _vigemBridge.TryConnect();
            _vigemBridge.Disconnect();
            return false;
        }

        /// <summary>Aktueller ViGEm-Status (z.B. für die UI-Anzeige).</summary>
        public string VigemStatus
        {
            get
            {
                lock (_controllers) { /* keep thread-safety pattern readable */ }
                return _vigemBridge.Status;
            }
        }

        /// <summary>True, wenn momentan ein virtueller Xbox-Controller verbunden ist.</summary>
        public bool VigemIsXboxActive
        {
            get { lock (_controllers) { } return _vigemBridge.IsActive; }
        }

        /// <summary>
        /// XInput-Slot des virtuellen Xbox-Controllers (0–3) oder -1.
        /// Nur Slot 0 bedient auch Spiele, die ausschließlich Slot 0 abfragen.
        /// </summary>
        public int VigemUserIndex => _vigemBridge.UserIndex;

        // Live-Messwerte für die Diagnose: wann wurde zuletzt eine echte Eingabe vom
        // aktiven (physischen) Controller gelesen und mit wie vielen gedrückten Tasten.
        private DateTime _lastSourceInputAt = DateTime.MinValue;
        private int _lastSourcePressedCount;
        private string _lastPressedSummary = "";
        private string _lastAxesSummary = "";

        public DateTime LastSourceInputAtUtc => _lastSourceInputAt;
        public int LastSourcePressedCount => _lastSourcePressedCount;

        /// <summary>Kurzfassung der zuletzt gedrückten Tasten, z.B. "A, B, LS_LEFT" (für die UI).</summary>
        public string LastPressedSummary => _lastPressedSummary;

        /// <summary>Kurzfassung der zuletzt gelesenen Stick-/Achsenwerte (für die UI).</summary>
        public string LastAxesSummary => _lastAxesSummary;

        private void OnControllerState(object? sender, ControllerStateEventArgs e)
        {
            // Eingabe-Livewert auch ohne aktive Spiegelung erfassen: StateChanged kommt
            // nur für den aktuell ausgewählten Controller (bzw. „None“ im Leerlauf).
            if (!string.IsNullOrEmpty(e.ControllerName) && e.ControllerName != "None")
            {
                _lastSourceInputAt = DateTime.UtcNow;
                _lastSourcePressedCount = e.PressedButtons?.Count ?? 0;
                _lastPressedSummary = BuildPressedSummary(e.PressedButtons);
                _lastAxesSummary = string.Format("LX={0:0.00} LY={1:0.00} RX={2:0.00} RY={3:0.00}",
                    e.LeftStickX, e.LeftStickY, e.RightStickX, e.RightStickY);
            }
            if (!_vigemEnabled) return;
            // Synchron auf dem aufrufenden (Poll-)Thread spiegeln – kein Dispatcher-Umweg,
            // damit die Xbox-Ausgabe nicht durch UI-Stau verzögert wird. Controller „None“
            // (Leerlauf) überspringen wir – die Bridge hält ihren letzten Stand.
            if (!string.IsNullOrEmpty(e.ControllerName) && e.ControllerName != "None")
                _vigemBridge.ApplyState(e);
        }

        private ControllerManager() { }

        public void Initialize()
        {
            if (_isRunning) return;
            _isRunning = true;

            // 1-ms-Timer-Auflösung setzen, sonst schläft Thread.Sleep(4) real ~15,6 ms
            try { timeBeginPeriod(1); } catch { }

            _pollThread = new Thread(PollLoop)
            {
                IsBackground = true,
                // AboveNormal statt BelowNormal: der Poll-Thread soll auch unter
                // CPU-Last (z.B. während eines Spiels) seinen 4-ms-Takt halten.
                Priority = ThreadPriority.AboveNormal,
                Name = "TenzoraX-Poll"
            };
            _pollThread.Start();
            StateChanged += OnControllerState;
            GameControllersPanel.StartWatchdog();
            App.LogApp("Controller-System gestartet");
        }

        public void Shutdown()
        {
            _isRunning = false;
            var thread = _pollThread;
            _pollThread = null;
            if (thread != null && thread.IsAlive)
            {
                try { thread.Join(2000); } catch { }
            }
            Thread? rescan = null;
            lock (_rescanLock) { rescan = _rescanThread; }
            if (rescan != null && rescan.IsAlive)
            {
                try { rescan.Join(2000); } catch { }
            }
            try { timeEndPeriod(1); } catch { }
            StateChanged -= OnControllerState;
            _vigemEnabled = false;
            _vigemBridge.Disconnect();
            lock (_controllers) { _controllers.Clear(); }
            lock (_cooldownLock) { _xinputCooldownUntil.Clear(); _winmmCooldownUntil.Clear(); }
            DisposeRetiredChannels();
            App.LogApp("Controller-System gestoppt");
        }

        /// <summary>Startet den Rescan auf einem separaten Thread, falls keiner läuft.</summary>
        private void TryStartRescan()
        {
            lock (_rescanLock)
            {
                if (_rescanRunning) return;
                _rescanRunning = true;
                _rescanThread = new Thread(RescanWorker)
                {
                    IsBackground = true,
                    Name = "TenzoraX-Rescan"
                };
                _rescanThread.Start();
            }
        }

        private void RescanWorker()
        {
            try
            {
                // Kanäle aus dem VORHERIGEN Zyklus freigeben – die aktuelle Liste wurde
                // beim letzten Mal ersetzt, der Poll-Thread kann sie nicht mehr nutzen.
                DisposeRetiredChannels();
                RescanControllers();
            }
            catch (Exception ex)
            {
                App.LogApp("Rescan-Fehler (Worker): " + ex.Message);
            }
            finally
            {
                lock (_rescanLock) { _rescanRunning = false; }
            }
        }

        private void DisposeRetiredChannels()
        {
            List<HidInputChannel> old;
            lock (_controllers) { old = new List<HidInputChannel>(_retiredChannels); _retiredChannels.Clear(); }
            foreach (var ch in old)
            {
                try { ch.Dispose(); } catch { }
            }
        }

        // ============================================================
        //  RAW INPUT (WM_INPUT) – HID-Report-Verteilung ans Hauptfenster
        // ============================================================

        private IntPtr _rawInputHwnd = IntPtr.Zero;

        /// <summary>
        /// Registriert das Hauptfenster für HID-Gamepad-Input (UsagePage 1, Usage 5).
        /// Muss einmal nach Fenstererstellung aufgerufen werden.
        /// </summary>
        public void RegisterRawInput(IntPtr hwnd)
        {
            try
            {
                _rawInputHwnd = hwnd;
                var devices = new[]
                {
                    new RAWINPUTDEVICE { usUsagePage = 1, usUsage = 5, dwFlags = RIDEV_INPUTSINK, hwndTarget = hwnd },
                    // Usage 4 = Joystick: manche Bluetooth-Geräte melden sich so statt als Gamepad
                    new RAWINPUTDEVICE { usUsagePage = 1, usUsage = 4, dwFlags = RIDEV_INPUTSINK, hwndTarget = hwnd }
                };
                if (RegisterRawInputDevices(devices, (uint)devices.Length, (uint)Marshal.SizeOf<RAWINPUTDEVICE>()))
                    App.LogApp("RawInput registriert (HID-Gamepads)");
                else
                    App.LogApp("RawInput-Registrierung fehlgeschlagen (err=" + Marshal.GetLastWin32Error() + ")");
            }
            catch (Exception ex)
            {
                App.LogApp("RawInput-Registrierung fehlerhaft: " + ex.Message);
            }
        }

        /// <summary>
        /// Verarbeitet eine WM_INPUT-Nachricht (wird vom Hauptfenster durchgereicht).
        /// </summary>
        public void HandleWmInput(IntPtr lParam)
        {
            try
            {
                uint size = 0;
                if (GetRawInputData(lParam, RID_INPUT, IntPtr.Zero, ref size, (uint)Marshal.SizeOf<RAWINPUTHEADER>()) == uint.MaxValue || size == 0)
                    return;

                var buffer = new byte[size];
                var pin = GCHandle.Alloc(buffer, GCHandleType.Pinned);
                IntPtr basePtr = pin.AddrOfPinnedObject();
                try
                {
                    if (GetRawInputData(lParam, RID_INPUT, basePtr, ref size, (uint)Marshal.SizeOf<RAWINPUTHEADER>()) == uint.MaxValue)
                        return;
                    if (size < (uint)Marshal.SizeOf<RAWINPUTHEADER>()) return;

                    uint type = BitConverter.ToUInt32(buffer, 0);
                    if (type != RIM_TYPEHID) return;

                    int hidOffset = Marshal.SizeOf<RAWINPUTHEADER>();
                    if (hidOffset + 8 > buffer.Length) return;
                    uint dwSizeHid = BitConverter.ToUInt32(buffer, hidOffset);
                    uint dwCount = BitConverter.ToUInt32(buffer, hidOffset + 4);
                    if (dwSizeHid == 0 || dwCount == 0) return;

                    uint reportLen = dwSizeHid * dwCount;
                    if (reportLen > 1024) return;   // Schutz vor unsinnigen Größen

                    // Empirisch (Windows 11 24H2, x64): die Report-Daten liegen inline
                    // direkt nach dwSizeHid/dwCount (Offset hidOffset+8) – der
                    // bRawData-Pointer wird nicht mitkopiert. Es zählt die physisch
                    // kopierte Länge; nie über das Puffende hinaus lesen.
                    int dataOffset = hidOffset + 8;
                    uint dataInBuffer = (uint)Math.Max(0, buffer.Length - dataOffset);
                    uint copyLen = Math.Min(reportLen, dataInBuffer);
                    if (copyLen == 0) return;

                    if (reportLen > dataInBuffer)
                        App.LogApp("RAW-Report-Problem: dwSizeHid=" + dwSizeHid + " dwCount=" + dwCount + " bufLen=" + buffer.Length);

                    var report = new byte[reportLen];
                    Marshal.Copy(basePtr + dataOffset, report, 0, (int)copyLen);

                    // Routing: erst Reportlänge (funktioniert auch, wenn
                    // GetRawInputDeviceInfo auf dem System defekt ist), dann
                    // Gerätename als Fallback (präziser auf gesunden Systemen).
                    IntPtr hDevice = Marshal.ReadIntPtr(basePtr, 8);
                    if (RouteRawInputReport(report))
                        return;

                    string deviceName = TryGetRawDeviceName(hDevice);
                    if (deviceName.Length > 0)
                        RouteRawInputReportByName(deviceName, report);
                }
                finally
                {
                    pin.Free();
                }
            }
            catch (Exception ex)
            {
                App.LogApp("RawInput-Fehler: " + ex.Message);
            }
        }

        /// <summary>
        /// Ermittelt den Gerätenamen zum RawInput-Handle. Auf einigen Windows-11-24H2-Systemen
        /// liefert GetRawInputDeviceInfo durchgehend ERROR_INVALID_PARAMETER (87) – dann leer.
        /// </summary>
        private static string TryGetRawDeviceName(IntPtr hDevice)
        {
            try
            {
                uint reqLen = 0;
                uint rc = GetRawInputDeviceInfo(hDevice, RIDI_DEVICENAME, IntPtr.Zero, ref reqLen);
                if (rc == uint.MaxValue || reqLen == 0 || reqLen > 8192)
                    return "";

                // reqLen ist in Zeichen, Alloc braucht Bytes
                IntPtr nameBuf = Marshal.AllocHGlobal((int)reqLen * 2 + 2);
                try
                {
                    uint got = reqLen;
                    if (GetRawInputDeviceInfo(hDevice, RIDI_DEVICENAME, nameBuf, ref got) == uint.MaxValue || got == 0)
                        return "";
                    return Marshal.PtrToStringUni(nameBuf) ?? "";
                }
                finally
                {
                    Marshal.FreeHGlobal(nameBuf);
                }
            }
            catch
            {
                return "";
            }
        }

        /// <summary>
        /// Instanzschlüssel aus einer SetupAPI-Instanz-ID: "HID\VID_x&PID_y\Instanz" → "Instanz".
        /// Der Schlüssel taucht unverändert im RawInput-Gerätenamen auf.
        /// </summary>
        private static string? RawDeviceKeyForInstance(string instanceId)
        {
            if (!instanceId.StartsWith("HID\\", StringComparison.OrdinalIgnoreCase)) return null;
            int idx = instanceId.IndexOf('\\', 4);
            if (idx < 0) return null;
            return instanceId.Substring(idx + 1);
        }

        /// <summary>
        /// Routet einen RawInput-Report per Reportlänge an alle passenden Eingabekanäle.
        /// Funktioniert ohne Gerätenamen (GetRawInputDeviceInfo ist auf manchen
        /// Win11-24H2-Systemen durchgehend defekt). Mehrere gleichlange Kanäle sind
        /// in der Praxis derselbe Controller (Dual-Interface) oder dasselbe Modell –
        /// das Parsen liefert dann identische Werte.
        /// </summary>
        private bool RouteRawInputReport(byte[] report)
        {
            if (report.Length == 0) return false;

            List<HidInputChannel> matches = new();
            lock (_controllers)
            {
                foreach (var ctrl in _controllers)
                {
                    if (!ctrl.Connected || ctrl.HidChannel == null) continue;
                    if (ctrl.HidChannel.ReportLength == report.Length)
                        matches.Add(ctrl.HidChannel);
                }
            }

            if (matches.Count == 0) return false;
            foreach (var channel in matches)
            {
                try
                {
                    channel.ProcessRawReport(report);
                }
                catch (Exception ex)
                {
                    App.LogApp("RawInput-Report-Fehler: " + ex.Message);
                }
            }
            return true;
        }

        /// <summary>
        /// Fallback-Routing per Gerätename (Instanzschlüssel im RawInput-Gerätenamen),
        /// für Systeme, auf denen GetRawInputDeviceInfo funktioniert.
        /// </summary>
        private void RouteRawInputReportByName(string deviceName, byte[] report)
        {
            if (string.IsNullOrEmpty(deviceName) || report.Length == 0) return;

            lock (_controllers)
            {
                foreach (var ctrl in _controllers)
                {
                    if (!ctrl.Connected || ctrl.HidChannel == null) continue;
                    string? key = RawDeviceKeyForInstance(ctrl.InstanceId);
                    if (key != null && deviceName.IndexOf(key, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        try
                        {
                            ctrl.HidChannel.ProcessRawReport(report);
                        }
                        catch (Exception ex)
                        {
                            App.LogApp("RawInput-Report-Fehler: " + ex.Message);
                        }
                        return;
                    }
                }
            }
        }

        // ============================================================
        //  POLLING LOOP (EVERY 4ms = ~250Hz)
        // ============================================================

        private void PollLoop()
        {
            int scanTimer = 0;
            int errorCount = 0;

            while (_isRunning)
            {
                var iterWatch = Stopwatch.StartNew();

                try
                {
                    scanTimer++;

                    // Full rescan every ~1000ms – auf einem SEPARATEN Thread: Die
                    // Geräte-Enumeration (HID/DI/RawInput) dauert auf manchen Systemen
                    // ~200 ms; würde sie auf dem Poll-Thread laufen, friert die Eingabe
                    // in jedem Zyklus für diese Zeit ein (gemessen: ~210 ms/Rescan).
                    if ((DateTime.UtcNow - _lastRescanAt).TotalMilliseconds >= 1000)
                    {
                        _lastRescanAt = DateTime.UtcNow;
                        TryStartRescan();
                    }

                    // Find the Nth connected controller for our selection
                    int connectedCount = 0;
                    ControllerInfo? selectedCtrl = null;

                    // _selectedIndex unter demselben Lock lesen wie der Rescan-Thread
                    // ihn schreibt, damit die Auswahl nicht mit dem Listen-Tausch race.
                    lock (_controllers)
                    {
                        if (_selectedIndex >= 0)
                        {
                            foreach (var ctrl in _controllers)
                            {
                                if (ctrl.Connected)
                                {
                                    if (connectedCount == _selectedIndex)
                                    {
                                        selectedCtrl = ctrl;
                                        break;
                                    }
                                    connectedCount++;
                                }
                            }
                        }
                    }

                    if (selectedCtrl != null)
                    {
                        if (!selectedCtrl.HasInputChannel)
                        {
                            // Erkannt, aber kein Eingabe-Kanal (z.B. HID-only oder WinMM-Zugriffsfehler):
                            // kein StateChanged – Status wird über die UI-Auswahl angezeigt.
                        }
                        else if (selectedCtrl.IsXInput)
                            ReadXInput(selectedCtrl);
                        else if (selectedCtrl.HidChannel != null)
                            ReadHid(selectedCtrl);
                        else
                            ReadWinMM(selectedCtrl);
                    }
                    else
                    {
                        // Billige Wiederverbindung: Ist kein Controller ausgewählt, kann ein
                        // zuvor getrennter XInput-Slot nur durch den Rescan zurückkommen –
                        // und der dauert auf Systemen mit hängender DirectInput-Aufzählung
                        // bis zu einer Minute (gemessen 26.10.2026: 41 s für einen Zyklus).
                        // In dieser Zeit zeigt die App „nicht erkannt“, obwohl XInput das Pad
                        // längst wieder führt. XInputGetState kostet nur Mikrosekunden.
                        if (scanTimer % 125 == 0)     // ~2x pro Sekunde bei 4-ms-Poll
                        {
                            TryAdoptXInputSlot();     // Liste ist leer, XInput hat aber ein Pad
                            TryReconnectXInput();     // Eintrag vorhanden, aber getrennt
                        }

                        // No controller selected - fire empty state.
                        // Alle 20 Iterationen (~80 ms bei 4-ms-Poll): entspricht der
                        // alten ~13-Event/s-Taktung vor der Poll-Raten-Erhöhung, damit
                        // der Leerlauf nicht ~50 leere StateChanged-Events/s erzeugt.
                        if (scanTimer % 20 == 0)
                        {
                            try
                            {
                                StateChanged?.Invoke(this, new ControllerStateEventArgs
                                {
                                    ControllerName = "None",
                                    ConnectionType = "N/A",
                                    PressedButtons = new List<string>()
                                });
                            }
                            catch { }
                        }
                    }

                    errorCount = 0;
                }
                catch (Exception ex)
                {
                    errorCount++;
                    App.LogApp("Controller-Fehler (#" + errorCount + "): " + ex.Message);
                    if (errorCount > 10)
                    {
                        App.LogApp("Controller: zu viele Fehler, versuche ReInit");
                        try
                        {
lock (_controllers)
            {
                foreach (var c in _controllers) c.HidChannel?.Dispose();
                _controllers.Clear();
            }
                        }
                        catch { }
                        errorCount = 0;
                    }
                }

                Thread.Sleep(PollIntervalMs);

                // Tatsächliches Poll-Intervall messen (inkl. Sleep + Arbeit)
                iterWatch.Stop();
                LatencyMonitor.RecordPollInterval(iterWatch.Elapsed);
            }
        }

        // ============================================================
        //  RESCAN CONTROLLERS (XInput + WinMM + HID-Enrichment)
        // ============================================================

        private void RescanControllers()
        {
            List<ControllerInfo>? oldControllers;
            lock (_controllers) { oldControllers = new List<ControllerInfo>(_controllers); }

            // Aktuell ausgewählten Controller über seine stabile Gerätekennung merken
            string selectedStableId = "";
            {
                int connectedCount = 0;
                foreach (var c in oldControllers)
                {
                    if (c.Connected)
                    {
                        if (connectedCount == _selectedIndex) { selectedStableId = c.StableId; break; }
                        connectedCount++;
                    }
                }
            }

            // 1. XInput-Slots abfragen
            var xinputSlots = new List<int>();
            for (int i = 0; i < 4; i++)
            {
                if (InCooldown(_xinputCooldownUntil, i)) continue;
                uint rc = XInputGetState((uint)i, out _);
                if (rc == ERROR_SUCCESS) xinputSlots.Add(i);
            }

            // 2. WinMM-Joystick-API abfragen (entspricht Windows "USB-Gamecontroller einrichten")
            var winmmSlots = new List<(int Slot, JOYCAPSW Caps, bool PosOk)>();
            int winmmDevices = 0;
            try
            {
                winmmDevices = joyGetNumDevs();
                if (winmmDevices < 0) winmmDevices = 0;
                if (winmmDevices > 16) winmmDevices = 16;
            }
            catch (Exception ex) { App.LogApp("WinMM: joyGetNumDevs Exception: " + ex.GetType().Name + ": " + ex.Message); winmmDevices = 0; }
            _winmmCount = winmmDevices;

            // Detail-Logging nur bei Änderung: der Rescan läuft alle 300 ms, und jede
            // Log-Zeile bedeutet synchrones File.AppendAllText auf dem Poll-Thread
            // (Öffnen/Schreiben/Schließen) – das bremst die Eingabe spürbar aus.
            var winmmDetailLog = new List<string> { "WinMM: joyGetNumDevs()=" + winmmDevices };

            int winmmSkipped = 0;
            for (int i = 0; i < winmmDevices; i++)
            {
                try
                {
                    // Per-Slot-Details nur in den Detail-Log sammeln – geschrieben wird
                    // nur bei Geräte-Änderung (winmmSig), nicht bei jedem Rescan.
                    if (InCooldown(_winmmCooldownUntil, i)) { winmmDetailLog.Add("WinMM: Slot " + i + " in Cooldown übersprungen"); continue; }
                    int rc = joyGetDevCapsW((uint)i, out JOYCAPSW caps, Marshal.SizeOf<JOYCAPSW>());
                    if (rc != JOYERR_NOERROR)
                    {
                        // MMSYSERR_NODRIVER (6) bedeutet Slot unbelegt - nicht loggen, sonst zu viel Spam
                        if (rc != 6) winmmDetailLog.Add("WinMM: Slot " + i + " joyGetDevCapsW rc=" + rc);
                        winmmSkipped++;
                        continue;
                    }
                    winmmDetailLog.Add("WinMM: Slot " + i + " erkannt Name='" + caps.szPname + "' MID=" + caps.wMid.ToString("X4") + " PID=" + caps.wPid.ToString("X4") + " Buttons=" + caps.wNumButtons + " CapsSize=" + Marshal.SizeOf<JOYCAPSW>());

                    var joyInfo = new JOYINFOEX();
                    joyInfo.dwSize = Marshal.SizeOf<JOYINFOEX>();
                    joyInfo.dwFlags = JOY_RETURNALL;
                    int posRc = joyGetPosEx((uint)i, out joyInfo);
                    if (posRc != JOYERR_NOERROR)
                        winmmDetailLog.Add("WinMM: Slot " + i + " joyGetPosEx rc=" + posRc);
                    winmmSlots.Add((i, caps, posRc == JOYERR_NOERROR));
                }
                catch (Exception ex)
                {
                    App.LogApp("WinMM: Slot " + i + " Exception: " + ex.GetType().Name + ": " + ex.Message);
                }
            }
            winmmDetailLog.Add("WinMM: " + winmmSlots.Count + " von " + winmmDevices + " Slots als Gamepad erkannt, " + winmmSkipped + " unbelegt/fehlerhaft");

            string winmmSig = winmmDevices + "|" + string.Join("|", winmmSlots.Select(s => s.Slot + ":" + s.Caps.szPname.Trim() + ":" + s.Caps.wMid.ToString("X4") + ":" + s.Caps.wPid.ToString("X4")));
            if (winmmSig != _lastWinmmSignature)
            {
                _lastWinmmSignature = winmmSig;
                foreach (var line in winmmDetailLog) App.LogApp(line);
            }

            // 3. HID-Gamecontroller über SetupAPI ermitteln (Name, VID/PID, InstanceId, Verbindungsart)
            //    Detection.HidControllerProvider akzeptiert Usage 5 (Gamepad) UND Usage 4 (Joystick),
            //    damit auch Bluetooth-Varianten erkannt werden, die sich als Joystick melden.
            //    Instanz-IDs werden case-insensitiv geführt: derselbe Pfad kommt je nach Quelle
            //    in anderer Schreibweise zurück – sonst würde ein Gerät zweimal vergeben.
            List<HidDeviceInfo> hids = HidControllerProvider.Instance.EnumerateGamepads(includeJoystickUsage: true, verboseLogging: false);
            var unclaimed = new HashSet<string>(hids.Select(h => h.InstanceId), StringComparer.OrdinalIgnoreCase);

            var newControllers = new List<ControllerInfo>();

            // 4a. WinMM-Geräte zuordnen (beste Identität über VID/PID + OEM-Name)
            foreach (var (slot, caps, posOk) in winmmSlots)
            {
                string vidPid = (caps.wMid != 0 || caps.wPid != 0) ? DeviceHelpers.FormatVidPid(caps.wMid, caps.wPid) : "";
                string oemName = DeviceHelpers.GetWinMmOemName(caps.szRegKey ?? "");
                string capsName = caps.szPname.Trim();

                HidDeviceInfo? hid = null;
                if (vidPid != "")
                {
                    hid = hids.FirstOrDefault(h => unclaimed.Contains(h.InstanceId) && h.VidPid.Equals(vidPid, StringComparison.OrdinalIgnoreCase));
                    if (hid != null) unclaimed.Remove(hid.InstanceId);
                }

                string name = !string.IsNullOrEmpty(oemName) ? oemName
                            : !string.IsNullOrEmpty(hid?.Name) ? hid.Name
                            : !string.IsNullOrEmpty(capsName) ? capsName
                            : "USB Gamepad";

                newControllers.Add(new ControllerInfo
                {
                    SlotIndex = slot,
                    IsXInput = false,
                    Name = name,
                    Connected = true,
                    RealName = name,
                    ConnectionType = hid?.ConnectionType ?? ConnectionTypeUnknown,
                    VidPid = !string.IsNullOrEmpty(vidPid) ? vidPid : (hid?.VidPid ?? ""),
                    InstanceId = hid?.InstanceId ?? "",
                    AccessError = !posOk,
                    AccessErrorReason = posOk ? "" : "WinMM-Zugriff fehlgeschlagen (joyGetPosEx, Gerät vorhanden)"
                });
            }

            // 4b. XInput-Slots zuordnen: bevorzugt Xbox-HID-Geräte (VID_045E), sonst restliche HID-Geräte
            //    Schlüssel = kanonischer Instanz-Schlüssel, damit ein Wechsel der
            //    Schreibweise den vorhandenen Kanal nicht neu öffnen lässt.
            var oldByInstance = new Dictionary<string, ControllerInfo>(StringComparer.Ordinal);
            foreach (var old in oldControllers)
            {
                string oldKey = DeviceHelpers.NormalizeInstanceKey(old.InstanceId);
                if (oldKey != "") oldByInstance[oldKey] = old;
            }

            var xboxHids = hids.Where(h => unclaimed.Contains(h.InstanceId) && h.Vid == 0x045E).ToList();
            var otherHids = hids.Where(h => unclaimed.Contains(h.InstanceId)).ToList();
            int hidPickIdx = 0;

            foreach (int slot in xinputSlots)
            {
                HidDeviceInfo? hid = null;
                if (hidPickIdx < xboxHids.Count)
                {
                    hid = xboxHids[hidPickIdx];
                    unclaimed.Remove(hid.InstanceId);
                }
                else if (hidPickIdx < otherHids.Count)
                {
                    hid = otherHids[hidPickIdx];
                    unclaimed.Remove(hid.InstanceId);
                }
                hidPickIdx++;

                // HID-Kanal auch für XInput-Geräte öffnen (RawInput-Routing: Report-Länge
                // und Zustands-Snapshot desselben Pads).
                HidInputChannel? hidChannel = null;
                if (hid != null)
                {
                    oldByInstance.TryGetValue(DeviceHelpers.NormalizeInstanceKey(hid.InstanceId), out var oldXInput);
                    // Für XInput-Pads ohne Layout-Fallback öffnen: ihre HID-Sammlung
                    // (xinputhid) hat keine Standard-Usages, und der 16-Byte-Fallback
                    // würde daraus dauerhaft "R2 gedrückt" und zappelnde Sticks machen.
                    if (oldXInput?.HidChannel != null && oldXInput.HidChannel.IsRunning &&
                        !oldXInput.HidChannel.AllowsManualFallback)
                    {
                        hidChannel = oldXInput.HidChannel;
                    }
                    else
                    {
                        if (oldXInput?.HidChannel != null) oldXInput.HidChannel.Dispose();
                        hidChannel = HidInputChannel.TryOpen(hid, enableManualFallback: false);
                        if (hidChannel != null)
                            App.LogApp("HID-Kanal geöffnet: " + hid.Name + " (" + hid.InstanceId + ")");
                    }
                }

                string type = hid?.ConnectionType ?? ConnectionTypeUnknown;

                string name = hid?.Name ?? $"Controller {newControllers.Count + 1} (Xbox)";

                newControllers.Add(new ControllerInfo
                {
                    SlotIndex = slot,
                    IsXInput = true,
                    Name = name,
                    Connected = true,
                    RealName = name,
                    ConnectionType = type,
                    VidPid = hid?.VidPid ?? "",
                    InstanceId = hid?.InstanceId ?? "",
                    HidChannel = hidChannel
                });
            }

            // 4c. HID-Geräte ohne XInput/WinMM-Slot: direkt per HID lesen (hid.dll)
            var seenVidPid = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var hid in hids)
            {
                if (!unclaimed.Contains(hid.InstanceId)) continue;
                if (!seenVidPid.Add(hid.VidPid)) continue;   // Mehrfach-Interfaces eines Geräts zusammenfassen

                // Bestehenden Kanal wiederverwenden (kein Re-Open bei jedem Rescan), sonst neu öffnen
                HidInputChannel? channel = null;
                if (oldByInstance.TryGetValue(DeviceHelpers.NormalizeInstanceKey(hid.InstanceId), out var oldCtrl) && oldCtrl.HidChannel != null && oldCtrl.HidChannel.IsRunning)
                {
                    channel = oldCtrl.HidChannel;
                }
                else
                {
                    oldCtrl?.HidChannel?.Dispose();
                    channel = HidInputChannel.TryOpen(hid);
                    if (channel != null)
                        App.LogApp("HID-Eingabekanal geöffnet: " + hid.Name + " (" + hid.InstanceId + ")");
                }

                string displayName = (channel != null && !string.IsNullOrEmpty(channel.ProductName) && !DeviceHelpers.LooksLikeGenericGamepad(channel.ProductName))
                    ? channel.ProductName
                    : hid.Name;

                newControllers.Add(new ControllerInfo
                {
                    SlotIndex = -1,
                    IsXInput = false,
                    Name = displayName,
                    // Getrenntes Gerät (Bluetooth weg) wird nicht mehr als verbunden
                    // geführt; ohne Kanal bleibt der bisherige Zustand (Zugriffsfehler).
                    Connected = channel == null || channel.IsDeviceConnected,
                    RealName = displayName,
                    ConnectionType = hid.ConnectionType,
                    VidPid = hid.VidPid,
                    InstanceId = hid.InstanceId,
                    HidChannel = channel,
                    AccessError = channel == null,
                    AccessErrorReason = channel == null ? "HID erkannt, aber Zugriff nicht möglich (CreateFile/PreparsedData)" : ""
                });
            }

            // 4c-bis. Geräte ergänzen, die NUR über DirectInput/Raw Input sichtbar sind
            // (z.B. Bluetooth-HID mit abweichender Top-Level-Sammlung, exotische Treiber).
            // Bereits vorhandene Einträge werden anhand Instance-ID bzw. VID/PID erkannt
            // und nicht doppelt gelistet.
            try { MergeAdditionalSources(newControllers); }
            catch (Exception ex) { App.LogApp("Zusatz-Erkennung fehlgeschlagen: " + ex.Message); }

            // 4c-ter. Dedupe: Ein WinMM-Slot, dessen VID/PID bereits über einen XInput-Slot
            // geführt wird, ist dieselbe physische Hardware (Xbox-Controller erscheinen
            // sowohl über WinMM als auch über XInput). XInput ist der bessere Eingabe-Kanal
            // (echter Xbox-Input) → den redundanten WinMM-Eintrag entfernen, damit der
            // Controller nicht doppelt in der Liste erscheint.
            var xinputVidPids = new HashSet<string>();
            foreach (var c in newControllers)
                if (c.IsXInput && !string.IsNullOrEmpty(c.VidPid))
                    xinputVidPids.Add(c.VidPid.ToUpperInvariant());
            if (xinputVidPids.Count > 0)
            {
                int before = newControllers.Count;
                newControllers.RemoveAll(c =>
                    !c.IsXInput && c.SlotIndex >= 0 && c.HidChannel == null &&
                    !string.IsNullOrEmpty(c.VidPid) && xinputVidPids.Contains(c.VidPid.ToUpperInvariant()));
                if (newControllers.Count < before)
                    App.LogApp("Dedupe: " + (before - newControllers.Count) + " redundante WinMM-Slots entfernt (XInput hat Vorrang)");
            }

            // 4c-ter. In Windows deaktivierte Schnittstellen kennzeichnen: die SetupAPI
            //    meldet sie als vorhanden, Eingaben liefern sie aber keine – ohne Markierung
            //    sieht ein solches Gerät in der Liste aus wie ein normaler Controller,
            //    obwohl weder Windows-Panel noch Spiele es lesen (am 26.10.2026 gemessen:
            //    Pad stand in TenzoraX, fehlte aber im Windows-Panel „Game Controller“).
            MarkDisabledInterfaces(newControllers);

            // 4c-ter-2. Widerspruch melden: Die App listet ein Pad, Windows führt aber weder
            //    XInput noch DirectInput ein Gerät – genau der Fall einer deaktivierten
            //    Schnittstelle. Ohne Hinweis wirkt die leere Liste im Windows-Panel wie ein
            //    Fehler der App. Nach der Kennzeichnung, damit der Hinweis die Schnittstelle
            //    beim Namen nennen kann.
            UpdateInputVisibilityWarning(newControllers, xinputSlots.Count);

            // 4d. Eingabekanäle getrennter Geräte aufbewahren – freigegeben werden sie
            // erst im NÄCHSTEN Zyklus (der Poll-Thread könnte sie sonst noch verwenden).
            //    Case-insensitiv verglichen: wechselt nur die Schreibweise (Raw Input klein,
            //    SetupAPI groß), darf der Kanal nicht als „Gerät getrennt“ aussortiert und
            //    beim nächsten Zyklus ein zweiter Leser auf dasselbe Gerät geöffnet werden.
            var newInstances = new HashSet<string>(newControllers.Select(c => c.InstanceId), StringComparer.OrdinalIgnoreCase);
            lock (_controllers)
            {
                foreach (var old in oldControllers)
                    if (old.HidChannel != null && !newInstances.Contains(old.InstanceId))
                        _retiredChannels.Add(old.HidChannel);
            }

            // 5. Verlauf bei Connect/Disconnect aktualisieren
            RecordHistoryDiff(oldControllers, newControllers);

            // 6. Listen austauschen
            lock (_controllers)
            {
                _controllers.Clear();
                _controllers.AddRange(newControllers);
            }

            // 7. Auswahl logik – unter demselben Lock wie der Poll-Thread lesen
            lock (_controllers)
            {
                int newCount = newControllers.Count;
                if (newCount > 0 && !_hasEverConnected)
                {
                    // Erster Controller → auto-anschließen. WICHTIG: Dabei wird die
                    // virtuelle Xbox 360 (ViGEm) übersprungen – sie ist als XInput-
                    // Slot 0 oft der erste Eintrag und würde sonst fälschlich zur
                    // Eingabequelle, wodurch Tasten des physischen Gamepads ins Leere
                    // laufen (nur leere Zustände würden gespiegelt).
                    int firstIdx = FirstPhysicalIndex(newControllers);
                    _selectedIndex = firstIdx;
                    _hasEverConnected = true;
                    _userSelectedStableId = newControllers[firstIdx].StableId;
                    _lastUsedStableId = newControllers[firstIdx].StableId;
                }
                else if (_userSelectedStableId != "")
                {
                    // Manuelle Auswahl → prüfen ob Gerät noch da ist
                    bool found = false;
                    int pos = 0;
                    foreach (var c in newControllers)
                    {
                        if (c.Connected)
                        {
                            if (c.StableId == _userSelectedStableId)
                            {
                                _selectedIndex = pos;
                                _lastUsedStableId = c.StableId;
                                found = true;
                                break;
                            }
                            pos++;
                        }
                    }
                    if (!found)
                    {
                        _userSelectedStableId = "";
                        if (newCount > 0) _selectedIndex = FirstPhysicalIndex(newControllers);
                        else _selectedIndex = -1;
                    }
                }
                else if (_selectedIndex >= newCount)
                {
                    // Auswahl außerhalb der Liste (z.B. Controller getrennt) → wieder
                    // ein physisches Gerät wählen, nie die virtuelle Xbox 360.
                    _selectedIndex = newCount > 0 ? FirstPhysicalIndex(newControllers) : -1;
                }
                else if (_selectedIndex < 0 && newCount > 0)
                {
                    // Nichts ausgewählt, aber Controller sind verbunden – z.B. nach
                    // Wiederverbinden eines zuvor komplett getrennten Controllers
                    // (_selectedIndex wurde beim Trennen auf -1 gesetzt). Ohne diese
                    // Übernahme würde kein Eingabe-Kanal gelesen und die Xbox-360-
                    // Spiegelung bliebe stumm, obwohl der Controller wieder da ist.
                    // Je nach Modus: ersten verbundenen oder zuletzt verwendeten
                    // Controller (anhand der StableId) übernehmen.
                    _selectedIndex = PickReselectIndex(newControllers);
                }

                // Schutz gegen „Auswahl klebt an der virtuellen Xbox 360“: Trennt sich
                // das physische Gamepad, ist die virtuelle Xbox oft das einzige Gerät
                // und wird zum Fallback (Index 0). Kommt das Gamepad zurück, bliebe die
                // Auswahl sonst auf der virtuellen Xbox stehen – die Spiegelung bliebe
                // stumm, obwohl Eingaben gelesen werden. In dem Fall sofort auf das
                // erste physische Gerät wechseln.
                if (newCount > 0 && _selectedIndex >= 0 && _selectedIndex < newCount
                    && IsVirtualXboxDevice(newControllers[_selectedIndex]))
                {
                    int phys = FirstPhysicalIndex(newControllers);
                    if (phys >= 0)
                    {
                        _selectedIndex = phys;
                        _userSelectedStableId = newControllers[phys].StableId;
                        _lastUsedStableId = newControllers[phys].StableId;
                    }
                }
            }

            // 8. Benachrichtigung nur bei tatsächlicher Änderung (Geräte, Typ, Fehlerzustand)
            string signature = string.Join("|", newControllers.Select(c => c.StableId + ":" + c.ConnectionType + ":" + (c.AccessError ? "ERR" : "OK") + ":" + (c.WindowsInterfaceDisabled ? "DIS" : "-") + ":" + (c.Connected ? "C" : "D")));
            if (signature != _lastRescanSignature)
            {
                _lastRescanSignature = signature;
                LastControllerChangeUtc = DateTime.UtcNow;
                int oldConnected = oldControllers.Count(c => c.Connected);
                int newConnected = newControllers.Count;
                if (oldConnected != newConnected)
                    App.LogApp("Controller-Änderung: " + oldConnected + " → " + newConnected + " Geräte");
                try { GamepadsChanged?.Invoke(this, EventArgs.Empty); } catch { }
            }
        }

        /// <summary>
        /// Hält den Widerspruchs-Hinweis aktuell und schreibt ihn nur bei Änderung ins Log.
        /// </summary>
        private void UpdateInputVisibilityWarning(List<ControllerInfo> controllers, int occupiedXInputSlots)
        {
            bool mismatch = InputEndpoints.IsMismatch(controllers.Count, occupiedXInputSlots, _lastDirectInputCount);

            string? disabledName = null;
            string disabledId = "";
            foreach (var c in controllers)
            {
                if (!c.WindowsInterfaceDisabled) continue;
                disabledName = c.Name;
                disabledId = c.InstanceId;
                break;
            }

            // Kein markierter Eintrag? Dann im Hintergrund (Rescan-Thread, gecachte
            // Gerätezustände) nach einer abgeschalteten Schnittstelle desselben Geräts
            // suchen. Genau diese Schnittstelle blockiert XInput und DirectInput, während
            // das Pad über einen anderen Weg (z.B. Raw Input) in der Liste steht.
            if (mismatch && disabledId == "")
            {
                var vidPids = CollectKnownVidPids(controllers);
                try
                {
                    var candidate = InputEndpoints.SelectRecoveryInterface(
                        PhysicalDeviceResolver.DisabledInterfaces(), vidPids);
                    if (candidate != null)
                    {
                        disabledId = candidate.InstanceId;
                        if (string.IsNullOrWhiteSpace(disabledName)) disabledName = candidate.FriendlyName;
                    }
                }
                catch { }
            }

            // Ohne Widerspruch gibt es nichts anzubieten – dann auch keinen Knopf.
            string newDisabledId = mismatch ? disabledId : "";
            bool interfaceChanged = newDisabledId != DisabledInterfaceId;
            if (interfaceChanged)
            {
                DisabledInterfaceId = newDisabledId;
                App.LogApp(newDisabledId == ""
                    ? "Eingabe-Sicht: keine abgeschaltete Gamepad-Schnittstelle gefunden – kein Aktivieren-Knopf."
                    : "Eingabe-Sicht: Aktivieren-Knopf für " + newDisabledId);
            }

            string warning = InputEndpoints.MismatchWarning(
                controllers.Count, occupiedXInputSlots, _lastDirectInputCount, disabledName);

            if (warning == InputVisibilityWarning)
            {
                // Die Suche kann erst später fündig werden, ohne dass sich der Text ändert –
                // ohne Signal bliebe der Knopf dann unsichtbar.
                if (interfaceChanged) NotifyGamepadsChanged();
                return;
            }
            InputVisibilityWarning = warning;

            if (warning == "")
                App.LogApp("Eingabe-Sicht: Widerspruch aufgehoben – Windows führt die Geräte wieder.");
            else
                App.LogApp("Eingabe-Sicht: " + warning);

            // Der Hinweis kann sich ändern, ohne dass sich die Geräteliste ändert (z.B. wenn
            // die DirectInput-Enumeration kurzzeitig leer zurückkommt). Ohne dieses Signal
            // bliebe das Banner in der Oberfläche auf dem alten Stand stehen.
            NotifyGamepadsChanged();
        }

        private void NotifyGamepadsChanged()
        {
            try { GamepadsChanged?.Invoke(this, EventArgs.Empty); } catch { }
        }

        /// <summary>
        /// Sammelt die VID/PID-Kennungen der gelisteten Controller – aus dem Feld und,
        /// falls das leer ist (WinMM-Einträge), aus der Instanz-ID. Damit findet die
        /// Suche nach einer abgeschalteten Geschwister-Schnittstelle auch dann etwas,
        /// wenn nur ein Teil der Quellen die VID/PID mitliefert.
        /// </summary>
        private static List<string> CollectKnownVidPids(List<ControllerInfo> controllers)
        {
            var vidPids = new List<string>();
            foreach (var c in controllers)
            {
                if (!string.IsNullOrWhiteSpace(c.VidPid)) AddVidPid(vidPids, c.VidPid);

                if (DeviceHelpers.TryParseVidPid(c.InstanceId, out ushort vid, out ushort pid))
                    AddVidPid(vidPids, DeviceHelpers.FormatVidPid(vid, pid));
            }
            return vidPids;
        }

        private static void AddVidPid(List<string> list, string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            string trimmed = value!.Trim();
            if (list.Contains(trimmed, StringComparer.OrdinalIgnoreCase)) return;
            list.Add(trimmed);
        }

        /// <summary>
        /// Kennzeichnet Controller, deren Windows-Schnittstelle deaktiviert ist
        /// (Problemcode 22). Sie werden wie ein Gerät ohne Zugriff behandelt und tragen
        /// einen erklärenden Grund, damit die Liste sie nicht als normalen Controller zeigt.
        /// </summary>
        private static void MarkDisabledInterfaces(List<ControllerInfo> controllers)
        {
            if (controllers.Count == 0) return;

            HashSet<string> disabled;
            try { disabled = PhysicalDeviceResolver.DisabledInterfaceKeys(); }
            catch { return; }
            if (disabled.Count == 0) return;

            foreach (var c in controllers)
            {
                string key = DeviceHelpers.NormalizeInstanceKey(c.InstanceId);
                if (key == "" || !disabled.Contains(key)) continue;
                c.WindowsInterfaceDisabled = true;
                c.AccessError = true;
                c.AccessErrorReason = "In Windows deaktiviert (Problemcode 22) – im Geräte-Manager aktivieren";
            }
        }

        /// <summary>
        /// Ein Eintrag der Controller-Auswahlliste. Deaktivierte Windows-Schnittstellen
        /// werden benannt statt nur mit einem Warnzeichen versehen – sonst wirkt das Gerät
        /// wie ein normaler Controller, obwohl es nirgends Eingaben liefert.
        /// </summary>
        internal static string FormatControllerListEntry(DetectedController c)
        {
            string text = c.Name + " (" + c.ConnectionType + ")";
            if (c.WindowsInterfaceDisabled) return text + " – deaktiviert (Windows)";
            return c.AccessError ? text + " ⚠" : text;
        }

        /// <summary>
        /// Wählt den Index für die Auto-Wiederauswahl, wenn gerade kein Controller
        /// ausgewählt ist, aber welche verbunden sind. Modus "last": zuletzt
        /// verwendeter Controller (StableId) wird bevorzugt, sonst der erste.
        /// </summary>
        private int PickReselectIndex(List<ControllerInfo> controllers)
        {
            if (ReselectMode.Equals("last", StringComparison.OrdinalIgnoreCase) && _lastUsedStableId != "")
            {
                int pos = 0;
                foreach (var c in controllers)
                {
                    if (c.Connected && !IsVirtualXboxDevice(c))
                    {
                        if (c.StableId == _lastUsedStableId) return pos;
                        pos++;
                    }
                }
            }
            return FirstPhysicalIndex(controllers);
        }

        /// <summary>
        /// Index des ersten verbundenen physischen Controllers. Die virtuelle Xbox 360
        /// (ViGEm) wird übersprungen – sie hat keine eigenen Eingaben und darf nie als
        /// Eingabequelle ausgewählt werden. Fallback: Index 0 (z.B. nur virtuell da).
        /// </summary>
        private static int FirstPhysicalIndex(List<ControllerInfo> controllers)
        {
            for (int i = 0; i < controllers.Count; i++)
            {
                var c = controllers[i];
                if (c.Connected && !IsVirtualXboxDevice(c)) return i;
            }
            return controllers.Count > 0 ? 0 : -1;
        }

        /// <summary>Erkennt den virtuellen ViGEm-Xbox-360-Controller (VID 045E / PID 028E).</summary>
        private static bool IsVirtualXboxDevice(ControllerInfo c)
        {
            return c.VidPid.IndexOf("045E", StringComparison.OrdinalIgnoreCase) >= 0
                && c.VidPid.IndexOf("028E", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void RecordHistoryDiff(List<ControllerInfo> oldList, List<ControllerInfo> newList)
        {
            var oldByStable = new Dictionary<string, ControllerInfo>();
            foreach (var c in oldList) if (c.Connected) oldByStable[c.StableId] = c;

            foreach (var c in newList)
            {
                if (c.Connected && !oldByStable.ContainsKey(c.StableId))
                {
                    ControllerHistoryManager.Instance.RecordConnect(c.Name, c.ConnectionType, c.VidPid, c.InstanceId);
                }
            }
        }

        private bool InCooldown(Dictionary<int, DateTime> cooldowns, int slot)
        {
            lock (_cooldownLock)
                return cooldowns.TryGetValue(slot, out var until) && DateTime.UtcNow < until;
        }

        // ============================================================
        //  ZUSATZ-ERKENNUNG (DirectInput / Raw Input) + DIAGNOSE
        // ============================================================

        /// <summary>
        /// Reagiert auf eine DirectInput-Aufzählung, die länger als fünf Sekunden braucht.
        /// Im Staufall warten die HID-Sammlungen einzelner Tastaturen/Receiver je 10 s
        /// auf ihr Timeout; `joy.cpl` hängt daran und stürzt als RunDLL ab. Im Hintergrund
        /// werden die blockierenden Geräte ermittelt und ihre USB-Schnittstelle neu
        /// gestartet (dokumentierter, umkehrbarer Rückweg). Höchstens ein Lauf gleichzeitig
        /// und höchstens alle zehn Minuten.
        /// </summary>
        private static void RepairStalledDirectInput(long elapsedMs)
        {
            lock (_diRepairLock)
            {
                if (_diRepairRunning == 1) return;
                if (DateTime.UtcNow - _diRepairLastUtc < TimeSpan.FromMinutes(10)) return;
                _diRepairRunning = 1;
                _diRepairLastUtc = DateTime.UtcNow;
            }

            App.LogApp("DI-Stau: Aufzählung dauerte " + elapsedMs + " ms – suche blockierende Geräte und starte sie neu.");
            System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    if (!InputSimulator.IsRunningAsAdmin())
                    {
                        App.LogApp("DI-Stau: Reparatur braucht Administratorrechte – TenzoraX erhöht starten.");
                        return;
                    }

                    // Beide Messwege zusammenführen: Lücken zwischen den Rückrufen und
                    // Timeouts in CreateDevice/GetCapabilities. Der zweite Weg ist der
                    // häufigere (gemessen 26.10.2026: 10,4 s Aufzählung bei 0 Lücken).
                    var slow = new List<string>();
                    foreach (var vidPid in DirectInputControllerProvider.LastSlowDevices)
                        if (!slow.Contains(vidPid, StringComparer.OrdinalIgnoreCase)) slow.Add(vidPid);
                    foreach (var vidPid in DirectInputControllerProvider.FindSlowDevices())
                        if (!slow.Contains(vidPid, StringComparer.OrdinalIgnoreCase)) slow.Add(vidPid);

                    if (slow.Count == 0)
                    {
                        App.LogApp("DI-Stau: kein blockierendes Gerät zuzuordnen – kein Neustart.");
                        return;
                    }
                    DeviceInterfaceRecovery.RestartStalledDevices(slow);
                }
                catch (Exception ex)
                {
                    App.LogApp("DI-Stau: Reparatur fehlgeschlagen: " + ex.GetType().Name + ": " + ex.Message);
                }
                finally
                {
                    System.Threading.Interlocked.Exchange(ref _diRepairRunning, 0);
                }
            });
        }

        /// <summary>
        /// Ergänzt Geräte, die ausschließlich über DirectInput oder Raw Input gefunden
        /// wurden (z.B. Bluetooth-HID mit abweichender Sammlung). Bereits gelistete
        /// Controller werden anhand Instance-ID bzw. VID/PID erkannt und übersprungen,
        /// damit nichts doppelt erscheint. Für neue Geräte wird versucht, einen
        /// HID-Eingabekanal zu öffnen; sonst werden sie ohne Live-Input angezeigt.
        /// </summary>
        private void MergeAdditionalSources(List<ControllerInfo> newControllers)
        {
            var findings = new List<DetectedDeviceInfo>();

            // Das Windows-Panel baut seine Liste über dieselbe DirectInput-Schnittstelle
            // auf. Die parallele Aufzählung alle 300 ms hat joy.cpl wiederholt in den
            // RunDLL-Hänger/Crash gezogen (gemessen 26.10.2026) – solange ein Panel offen
            // ist, pausiert sie deshalb. XInput und Raw Input laufen weiter.
            bool panelOpen = GameControllersPanel.IsPanelVisible();
            if (panelOpen != _directInputPausedByPanel)
            {
                _directInputPausedByPanel = panelOpen;
                App.LogApp(panelOpen
                    ? "Panel offen: DirectInput-Aufzählung pausiert, damit joy.cpl ungestört enumerieren kann."
                    : "Panel geschlossen: DirectInput-Aufzählung läuft wieder.");
            }

            // Leise: per-Device-Logs würden bei jedem Rescan die Log-Datei fluten
            // (viele Geräte → ständiger Datei-I/O auf dem Rescan-Thread).
            if (!panelOpen)
            {
                var diWatch = Stopwatch.StartNew();
                var directInput = DirectInputControllerProvider.Enumerate(verboseLogging: false);
                diWatch.Stop();
                if (diWatch.ElapsedMilliseconds > 5000)
                    RepairStalledDirectInput(diWatch.ElapsedMilliseconds);

                _lastDirectInputCount = directInput.Count;
                findings.AddRange(directInput);
            }
            findings.AddRange(RawInputControllerProvider.Enumerate(verboseLogging: false));
            // Nur bei Änderung loggen (läuft alle 300 ms)
            if (findings.Count != _lastFindingsCount)
            {
                _lastFindingsCount = findings.Count;
                App.LogApp("MergeAdditionalSources: " + findings.Count + " Zusatz-Findings (DI+RI), bereits " + newControllers.Count + " in Pipeline");
            }
            if (findings.Count == 0) return;

            // Bereits gelistete Geräte einmal aufbereiten (Name + Instanz) – die
            // Zusatz-Erkennung läuft alle 300 ms, deshalb nicht pro Fund neu bauen.
            var knownDevices = new List<(string Name, string InstanceId)>(newControllers.Count);
            foreach (var c in newControllers)
                knownDevices.Add((c.Name ?? "", c.InstanceId ?? ""));

            int duplicates = 0, added = 0;
            foreach (var f in findings)
            {
                bool duplicate = false;
                foreach (var c in newControllers)
                {
                    if (!string.IsNullOrEmpty(f.VidPid) && !string.IsNullOrEmpty(c.VidPid) &&
                        string.Equals(f.VidPid, c.VidPid, StringComparison.OrdinalIgnoreCase))
                    {
                        duplicate = true;
                        break;
                    }
                    // Instanz-Vergleich über den kanonischen Schlüssel: `#`/`\` und
                    // Groß-/Kleinschreibung dürfen aus demselben Gerät kein zweites
                    // Listeneintrag-Paar machen.
                    string findingKey = DeviceHelpers.NormalizeInstanceKey(f.DeviceInstanceId);
                    if (findingKey != "" && findingKey == DeviceHelpers.NormalizeInstanceKey(c.InstanceId))
                    {
                        duplicate = true;
                        break;
                    }
                }
                if (duplicate) { duplicates++; continue; }

                // Windows legt zu jedem XInput-Pad zusätzlich eine HID-Fassade mit
                // Microsoft-VID/PID an (VID_045E&PID_02FF, „XINPUT-kompatibles
                // HID-Gerät"). Sie ist über den XInput-Slot bereits in der Pipeline;
                // ein zweiter Eintrag würde dasselbe Pad doppelt führen und dessen
                // 16-Byte-XInput-Report fälschlich auswerten (dauerhaft „R2" plus
                // zappelnde Sticks, gemessen 26.10.2026).
                // Immer überspringen – auch wenn (noch) kein XInput-Slot in der
                // Pipeline steht: dieses Gerät ist die Windows-Fassade eines
                // XInput-Pads und liefert keinen eigenständigen Eingabeweg.
                if (IsXInputHidFacade(f.Vid, f.Pid))
                {
                    duplicates++;
                    if (!_loggedXInputHidSkip)
                    {
                        _loggedXInputHidSkip = true;
                        App.LogApp("Merge: XInput-HID-Fassade übersprungen (wird über XInput gelesen): " + f.DeviceInstanceId);
                    }
                    continue;
                }

                string instanceId = f.DeviceInstanceId ?? "";
                var hidInfo = new HidDeviceInfo
                {
                    Name = f.Name ?? "",
                    Vid = f.Vid,
                    Pid = f.Pid,
                    VidPid = f.VidPid,
                    InstanceId = instanceId,
                    ConnectionType = f.Transport == ConnectionTypeUnknown ? ConnectionTypeUnknown : f.Transport
                };

                HidInputChannel? channel = null;
                if (!string.IsNullOrEmpty(instanceId) && instanceId.StartsWith("HID\\", StringComparison.OrdinalIgnoreCase))
                {
                    channel = HidInputChannel.TryOpen(hidInfo);
                    if (channel != null)
                        App.LogApp("Zusatz-Erkennung: HID-Eingabekanal geöffnet für " +
                                   (!string.IsNullOrEmpty(hidInfo.Name) ? hidInfo.Name : hidInfo.VidPid));
                }

                string displayName = ResolveAdditionalDisplayName(f, findings, knownDevices, hidProductName: channel?.ProductName);

                string sourceApi = f.Api == ControllerApi.DirectInput ? "DirectInput" : "Raw Input";
                newControllers.Add(new ControllerInfo
                {
                    SlotIndex = -1,
                    IsXInput = false,
                    Name = displayName,
                    Connected = true,
                    RealName = displayName,
                    ConnectionType = hidInfo.ConnectionType,
                    VidPid = hidInfo.VidPid,
                    InstanceId = instanceId,
                    HidChannel = channel,
                    AccessError = channel == null,
                    AccessErrorReason = channel == null
                        ? "Nur über " + sourceApi + " sichtbar, HID-Zugriff nicht möglich"
                        : ""
                });
                added++;
                App.LogApp("Merge: Zusatz-Gerät hinzugefügt '" + displayName + "' (" + f.VidPid + ") via " + sourceApi);
            }
            // Nur bei tatsächlicher Änderung loggen.
            if (added > 0)
                App.LogApp("MergeAdditionalSources: " + added + " neue Geräte hinzugefügt, " + duplicates + " Duplikate übersprungen");
        }

        /// <summary>
        /// Anzeigename für ein Zusatz-Gerät. Raw Input liefert keinen Produktnamen –
        /// ohne Auflösung stünde in der Liste nur „VID_2DC8&amp;PID_3024“. Reihenfolge:
        /// eigener echter Name → HID-Produktname des Geräts (HidD_GetProductString) →
        /// echter Windows-Gerätename der physischen Instanz (notfalls vom Elternknoten)
        /// → gleiche VID/PID in derselben Fundliste → bereits gelistete Instanz in der
        /// Pipeline → eigener (generischer) Name → Familienname aus VID → VID/PID als
        /// letzter Rückfall.
        /// </summary>
        /// <param name="windowsName">Nur für Tests: Ersatz für die SetupAPI-Abfrage.</param>
        /// <param name="hidProductName">Produktname aus dem HID-Descriptor, sofern der
        /// Eingabekanal geöffnet werden konnte – bei Bluetooth-Pads (z.B. 8BitDo) der
        /// einzige Ort, an dem der echte Produktname steht.</param>
        internal static string ResolveAdditionalDisplayName(
            DetectedDeviceInfo finding,
            IReadOnlyList<DetectedDeviceInfo> findings,
            IReadOnlyList<(string Name, string InstanceId)> pipeline,
            Func<string, ushort, ushort, string>? windowsName = null,
            string? hidProductName = null)
        {
            string own = (finding.Name ?? "").Trim();
            if (DeviceNameResolver.IsRealProductName(own)) return own;

            // Der Gerätename aus dem HID-Descriptor ist der Produktname, den der
            // Hersteller selbst hinterlegt hat – Windows zeigt ihn oft gar nicht an
            // (Bluetooth-Pad: dort steht nur „Bluetooth-HID-Gerät“).
            string hidName = (hidProductName ?? "").Trim();
            if (DeviceNameResolver.IsRealProductName(hidName)) return hidName;

            // Echter Produktname der physischen Instanz – auch über den Elternknoten.
            string resolved = (windowsName ?? PhysicalDeviceResolver.ResolveProductName)(
                finding.DeviceInstanceId, finding.Vid, finding.Pid);
            resolved = (resolved ?? "").Trim();
            if (DeviceNameResolver.IsRealProductName(resolved)) return resolved;

            // Andere API im selben Durchlauf hat das Gerät schon benannt (DirectInput).
            if (!string.IsNullOrEmpty(finding.VidPid))
            {
                foreach (var other in findings)
                {
                    if (!string.Equals(other.VidPid, finding.VidPid, StringComparison.OrdinalIgnoreCase)) continue;
                    if (DeviceNameResolver.IsRealProductName(other.Name)) return other.Name.Trim();
                }
            }

            // Derselbe Gerätepfad steht schon in der Pipeline (andere Schreibweise).
            if (!string.IsNullOrEmpty(finding.DeviceInstanceId))
            {
                foreach (var known in pipeline)
                {
                    if (!string.Equals(known.InstanceId, finding.DeviceInstanceId, StringComparison.OrdinalIgnoreCase)) continue;
                    if (DeviceNameResolver.IsRealProductName(known.Name)) return known.Name.Trim();
                }
            }

            // Eigener Name ist generisch, stammt aber vom Gerät – besser als eine VID/PID.
            if (!string.IsNullOrWhiteSpace(own) && !own.StartsWith("VID_", StringComparison.OrdinalIgnoreCase))
                return own;

            // Familienname („8BitDo Gamepad“) aus der VID, falls Windows nichts hergibt.
            if (!string.IsNullOrWhiteSpace(resolved) && !DeviceHelpers.IsDriverPlaceholderName(resolved))
                return resolved;

            return !string.IsNullOrEmpty(finding.VidPid) ? finding.VidPid : "Gamepad";
        }

        /// <summary>
        /// Erstellt den vollständigen Diagnosebericht: aktueller Pipeline-Zustand plus
        /// frische Mehr-API-Erkennung (XInput, WinMM, DirectInput, HID, Raw Input).
        /// </summary>
        public string BuildDiagnosticsReport()
        {
            var sb = new StringBuilder(8192);
            sb.AppendLine(ControllerDiagnostics.BuildReport(ControllerDiagnostics.Collect()));

            // Live-Sicht der Eingabe-Endpunkte: welches Gerät liest Windows über XInput,
            // welches über DirectInput – und was zählen Steam und das Windows-Panel daraus.
            sb.AppendLine(InputEndpoints.Format(InputEndpoints.Collect()));

            lock (_controllers)
            {
                sb.AppendLine("--- Aktive Pipeline-Auswahl ---");
                sb.AppendLine("Ausgewählt: " + (_selectedIndex >= 0 ? _selectedIndex.ToString() : "(keiner)"));
                sb.AppendLine("Zuletzt verwendet (StableId): " + (string.IsNullOrEmpty(_lastUsedStableId) ? "(keiner)" : _lastUsedStableId));
                sb.AppendLine("Wiederauswahl-Modus: " + ReselectMode);
                if (_vigemEnabled)
                {
                    sb.AppendLine("ViGEm-Xbox-Spiegelung: aktiv – " + _vigemBridge.Status);
                    int userIndex = _vigemBridge.UserIndex;
                    if (userIndex >= 0)
                    {
                        sb.AppendLine("Virtuelles Pad: XInput-Slot " + userIndex + (userIndex == 0
                            ? " – auch Spiele, die nur Slot 0 lesen, finden es."
                            : " – Slot 0 hält das physische Pad; Spiele, die nur Slot 0 lesen,"
                              + " sehen keinen Controller (Abhilfe: Duplikat-Schalter)."));
                    }
                }

                int connectedCount = 0;
                foreach (var ctrl in _controllers)
                {
                    if (!ctrl.Connected) continue;
                    connectedCount++;
                    sb.AppendLine($"[{connectedCount}] {ctrl.Name} | {ctrl.ConnectionType}" +
                                  (ctrl.AccessError ? " | ⚠ " + ctrl.AccessErrorReason : "") +
                                  $" | StableId: {ctrl.StableId}" +
                                  (connectedCount - 1 == _selectedIndex ? " | ◀ AKTIV" : ""));
                }
                if (connectedCount == 0)
                    sb.AppendLine("(kein Controller verbunden)");
            }

            sb.AppendLine();
            sb.AppendLine("--- Zuletzt zusammengefasste Listeneinträge ---");
            lock (_collapsedInstancesLock)
            {
                sb.Append(FormatCollapsedInstances(_collapsedInstances));
            }

            sb.AppendLine();
            sb.AppendLine("--- ViGEm-Xbox-Spiegelung (live) ---");
            sb.AppendLine("Status: " + (_vigemEnabled ? _vigemBridge.Status : "aus (Spiegelung nicht aktiv)"));

            string srcName = "(keiner)";
            lock (_controllers)
            {
                if (_selectedIndex >= 0)
                {
                    int pos = 0;
                    foreach (var c in _controllers)
                    {
                        if (!c.Connected) continue;
                        if (pos == _selectedIndex) { srcName = c.Name + " (" + c.ConnectionType + ")"; break; }
                        pos++;
                    }
                }
            }
            sb.AppendLine("Eingabequelle: " + srcName);
            sb.AppendLine("Eingabe gelesen: " + (_lastSourceInputAt == DateTime.MinValue
                ? "(noch keine)"
                : "vor " + FormatAge(DateTime.UtcNow - _lastSourceInputAt) + " – " + _lastSourcePressedCount + " Tasten"));
            if (_vigemEnabled && _vigemBridge.IsActive)
            {
                if (_vigemBridge.LastApplyAtUtc == DateTime.MinValue)
                    sb.AppendLine("Spiegelung: (noch keine Zustände gespiegelt)");
                else
                    sb.AppendLine("Spiegelung: vor " + FormatAge(DateTime.UtcNow - _vigemBridge.LastApplyAtUtc)
                        + " – " + _vigemBridge.LastPressedCount + " Tasten | gesamt: " + _vigemBridge.ApplyCount + " Zustände");
            }
            else
            {
                sb.AppendLine("Spiegelung: nicht aktiv (ViGEmBus-Treiber fehlt oder ausgeschaltet)");
            }

            sb.AppendLine();
            sb.AppendLine("--- Sichtbarkeit (HidHide) ---");
            bool cloak = HidHideAutomation.IsCloakActive();
            string physId = "";
            string physName = "";
            lock (_controllers)
            {
                // Aktive Eingabequelle suchen (nie die virtuelle Xbox 360).
                if (_selectedIndex >= 0)
                {
                    int pos = 0;
                    foreach (var c in _controllers)
                    {
                        if (!c.Connected) continue;
                        if (pos == _selectedIndex && !IsVirtualXboxDevice(c))
                        {
                            physId = c.InstanceId ?? "";
                            physName = c.Name;
                            break;
                        }
                        pos++;
                    }
                }
                // Fallback: erstes verbundenes physisches Gerät.
                if (physId == "")
                {
                    foreach (var c in _controllers)
                    {
                        if (c.Connected && !IsVirtualXboxDevice(c))
                        {
                            physId = c.InstanceId ?? "";
                            physName = c.Name;
                            break;
                        }
                    }
                }
            }
            sb.AppendLine("Cloaking: " + (cloak ? "aktiv" : "deaktiviert"));
            if (string.IsNullOrEmpty(physId))
            {
                sb.AppendLine("Physisches Gamepad: (keines verbunden)");
            }
            else
            {
                // Ein Pad hängt oft mit mehreren Schnittstellen am Bus (XInput, HID,
                // zweiter Composite-Zweig). Versteckt werden müssen ALLE – sonst bleibt
                // der Controller über die übrigen Schnittstellen sichtbar (Doppel-Input).
                var interfaces = PhysicalDeviceResolver.ResolveForThisMachine(physId);
                var hiddenEntries = HidHideAutomation.ListHiddenDevices();
                int hiddenCount = interfaces.Count(id => hiddenEntries.Any(
                    h => string.Equals(h, id, StringComparison.OrdinalIgnoreCase)));
                int hidCount = interfaces.Count(id => id.StartsWith("HID\\", StringComparison.OrdinalIgnoreCase));
                bool allHidden = interfaces.Count > 0 && hiddenCount == interfaces.Count;

                sb.AppendLine("Physisches Gamepad: " + physName);
                sb.AppendLine("Schnittstellen: " + interfaces.Count + " (davon HID: " + hidCount + ")");
                sb.AppendLine("Versteckt: " + hiddenCount + "/" + interfaces.Count);
                if (PhysicalDeviceResolver.LastReadErrors > 0)
                    sb.AppendLine("Hinweis: " + PhysicalDeviceResolver.LastReadErrors + " Gerät(e) konnten nicht gelesen werden – die Auflösung kann unvollständig sein.");
                sb.AppendLine("Für andere Programme: " + ((cloak && allHidden)
                    ? "unsichtbar – Spiele sehen nur die virtuelle Xbox 360"
                    : "sichtbar – Doppel-Input möglich"));
            }

            sb.AppendLine();
            sb.AppendLine("--- Latenz (live, seit App-Start) ---");
            sb.AppendLine(LatencyMonitor.FormatSummary());

            sb.AppendLine();
            sb.AppendLine("--- Eingabe-Ausgabe (SendInput) ---");
            sb.AppendLine(InputSimulator.FormatInjectionStatus());

            return sb.ToString();
        }

        /// <summary>Baut eine kompakte Tastenliste für die UI, z.B. „A, B, LS_LEFT“.</summary>
        private static string BuildPressedSummary(List<string>? pressed)
        {
            if (pressed == null || pressed.Count == 0) return "";
            var seen = new System.Collections.Generic.HashSet<string>();
            var parts = new List<string>();
            foreach (var b in pressed)
            {
                if (seen.Add(b)) parts.Add(b);
            }
            return string.Join(", ", parts);
        }

        /// <summary>Formatiert eine Zeitspanne kompakt, z.B. „0,3 s“, „1,2 min“, „3 h“.</summary>
        private static string FormatAge(TimeSpan d)
        {
            if (d.TotalSeconds < 1) return Math.Max(0, d.TotalMilliseconds).ToString("0") + " ms";
            if (d.TotalSeconds < 60) return d.TotalSeconds.ToString("0.0") + " s";
            if (d.TotalMinutes < 60) return d.TotalMinutes.ToString("0.0") + " min";
            return d.TotalHours.ToString("0.0") + " h";
        }

        // ============================================================
        //  READ XINPUT CONTROLLER
        // ============================================================

        /// <summary>
        /// Nimmt einen belegten XInput-Slot sofort auf, wenn die Controller-Liste leer ist.
        /// Hintergrund (gemessen 26.10.2026): Der Rescan dauert auf Systemen mit hängender
        /// DirectInput-Aufzählung 40-80 s. Bis dahin zeigte die App „nicht verbunden“,
        /// obwohl XInput das Pad bereits führte. Der Eintrag ist bewusst schlank – der
        /// nächste Rescan ersetzt ihn durch den vollständigen (mit HID-Namen/Akku).
        /// </summary>
        private void TryAdoptXInputSlot()
        {
            lock (_controllers)
                if (_controllers.Count > 0) return;

            for (int slot = 0; slot < 4; slot++)
            {
                if (InCooldown(_xinputCooldownUntil, slot)) continue;
                if (XInputGetState((uint)slot, out _) != ERROR_SUCCESS) continue;

                var ctrl = new ControllerInfo
                {
                    SlotIndex = slot,
                    IsXInput = true,
                    Name = "Xbox-Controller",
                    RealName = "Xbox-Controller",
                    Connected = true,
                    ConnectionType = ConnectionTypeUnknown
                };

                lock (_controllers) { _controllers.Add(ctrl); }
                App.LogApp("XInput-Controller sofort übernommen: Slot " + slot + " (Rescan läuft weiter)");
                try { GamepadsChanged?.Invoke(this, EventArgs.Empty); } catch { }
                return;
            }
        }

        /// <summary>
        /// Fragt die XInput-Slots erneut ab, wenn aktuell kein Controller ausgewählt ist.
        /// Ein als getrennt markierter Controller wird so sofort wieder „verbunden“,
        /// ohne auf den (potenziell minutenlangen) Rescan zu warten. Es wird nichts
        /// neu aufgebaut: nur der Zustand des vorhandenen Eintrags wird korrigiert.
        /// </summary>
        private void TryReconnectXInput()
        {
            List<ControllerInfo> candidates;
            lock (_controllers)
                candidates = _controllers.Where(c => c.IsXInput && !c.Connected).ToList();

            foreach (var ctrl in candidates)
            {
                if (InCooldown(_xinputCooldownUntil, ctrl.SlotIndex)) continue;
                if (XInputGetState((uint)ctrl.SlotIndex, out _) != ERROR_SUCCESS) continue;

                bool becameConnected = false;
                lock (_controllers)
                {
                    if (!ctrl.Connected) { ctrl.Connected = true; becameConnected = true; }
                }

                if (becameConnected)
                {
                    App.LogApp("XInput-Controller wieder verbunden: Slot " + ctrl.SlotIndex);
                    try { GamepadsChanged?.Invoke(this, EventArgs.Empty); } catch { }
                }
            }
        }

        private void ReadXInput(ControllerInfo ctrl)
        {
            // Skip polling a slot that recently disconnected (driver teardown in progress)
            if (InCooldown(_xinputCooldownUntil, ctrl.SlotIndex)) return;

            var rc = XInputGetState((uint)ctrl.SlotIndex, out var state);
            if (rc != ERROR_SUCCESS)
            {
                lock (_cooldownLock) { _xinputCooldownUntil[ctrl.SlotIndex] = DateTime.UtcNow + DisconnectCooldown; }
                if (ctrl.Connected)
                {
                    ctrl.Connected = false;
                    App.LogApp("XInput-Controller getrennt: Slot " + ctrl.SlotIndex);
                    try
                    {
                        StateChanged?.Invoke(this, new ControllerStateEventArgs
                        {
                            ControllerName = "None",
                            ConnectionType = "N/A",
                            PressedButtons = new List<string>()
                        });
                        GamepadsChanged?.Invoke(this, EventArgs.Empty);
                    }
                    catch { }
                }
                return;
            }

            if (!ctrl.Connected)
            {
                ctrl.Connected = true;
                App.LogApp("XInput-Controller verbunden: Slot " + ctrl.SlotIndex);
                try { GamepadsChanged?.Invoke(this, EventArgs.Empty); } catch { }
            }                lock (_cooldownLock) { _xinputCooldownUntil.Remove(ctrl.SlotIndex); }

                var g = state.Gamepad;
            var w = g.wButtons;
            var pressed = new List<string>();

            if ((w & XINPUT_A) != 0) pressed.Add("A");
            if ((w & XINPUT_B) != 0) pressed.Add("B");
            if ((w & XINPUT_X) != 0) pressed.Add("X");
            if ((w & XINPUT_Y) != 0) pressed.Add("Y");
            if ((w & XINPUT_L1) != 0) pressed.Add("L1");
            if ((w & XINPUT_R1) != 0) pressed.Add("R1");
            if (g.bLeftTrigger > 30) pressed.Add("L2");
            if (g.bRightTrigger > 30) pressed.Add("R2");
            if ((w & XINPUT_L3) != 0) pressed.Add("L3");
            if ((w & XINPUT_R3) != 0) pressed.Add("R3");
            if ((w & XINPUT_BACK) != 0) pressed.Add("SELECT");
            if ((w & XINPUT_START) != 0) pressed.Add("START");
            if ((w & XINPUT_DPAD_U) != 0) pressed.Add("DPAD_UP");
            if ((w & XINPUT_DPAD_D) != 0) pressed.Add("DPAD_DOWN");
            if ((w & XINPUT_DPAD_L) != 0) pressed.Add("DPAD_LEFT");
            if ((w & XINPUT_DPAD_R) != 0) pressed.Add("DPAD_RIGHT");

            float lx = Math.Clamp(g.sThumbLX / 32768f, -1f, 1f);
            float ly = Math.Clamp(g.sThumbLY / 32768f, -1f, 1f);
            if (lx < -0.7f) pressed.Add("LS_LEFT");
            else if (lx > 0.7f) pressed.Add("LS_RIGHT");
            if (ly > 0.7f) pressed.Add("LS_UP");
            else if (ly < -0.7f) pressed.Add("LS_DOWN");

            float rx = Math.Clamp(g.sThumbRX / 32768f, -1f, 1f);
            float ry = Math.Clamp(g.sThumbRY / 32768f, -1f, 1f);
            if (rx < -0.7f) pressed.Add("RS_LEFT");
            else if (rx > 0.7f) pressed.Add("RS_RIGHT");
            if (ry > 0.7f) pressed.Add("RS_UP");
            else if (ry < -0.7f) pressed.Add("RS_DOWN");

            try
            {
                StateChanged?.Invoke(this, new ControllerStateEventArgs
                {
                    ControllerName = ctrl.Name,
                    ConnectionType = ctrl.ConnectionType,
                    PressedButtons = pressed,
                    LeftStickX = lx, LeftStickY = ly,
                    RightStickX = rx, RightStickY = ry
                });
            }
            catch (Exception ex)
            {
                App.LogApp("Controller StateChanged-Fehler: " + ex.Message);
            }
        }

        // ============================================================
        //  READ HID CONTROLLER (direktes HID-Reading via hid.dll)
        // ============================================================

        private void ReadHid(ControllerInfo ctrl)
        {
            var channel = ctrl.HidChannel;
            if (channel == null || !channel.IsRunning) return;
            if (!channel.TryGetSnapshot(out var pressed, out float lx, out float ly, out float rx, out float ry)) return;

            try
            {
                StateChanged?.Invoke(this, new ControllerStateEventArgs
                {
                    ControllerName = ctrl.Name,
                    ConnectionType = ctrl.ConnectionType,
                    PressedButtons = pressed,
                    LeftStickX = lx, LeftStickY = ly,
                    RightStickX = rx, RightStickY = ry
                });
            }
            catch (Exception ex)
            {
                App.LogApp("Controller HID StateChanged-Fehler: " + ex.Message);
            }
        }

        // ============================================================
        //  READ WINMM JOYSTICK (8BitDo, PS, Switch, etc.)
        // ============================================================

        private void ReadWinMM(ControllerInfo ctrl)
        {
            try
            {
                // Skip polling a slot that recently disconnected (driver teardown in progress)
                if (InCooldown(_winmmCooldownUntil, ctrl.SlotIndex)) return;

                var joyInfo = new JOYINFOEX();
                joyInfo.dwSize = Marshal.SizeOf<JOYINFOEX>();
                joyInfo.dwFlags = JOY_RETURNALL | JOY_RETURNPOVCTS;

                int rc = joyGetPosEx((uint)ctrl.SlotIndex, out joyInfo);
                if (rc != JOYERR_NOERROR)
                {
                lock (_cooldownLock) { _winmmCooldownUntil[ctrl.SlotIndex] = DateTime.UtcNow + DisconnectCooldown; }
                if (ctrl.Connected)
                    {
                        ctrl.Connected = false;
                        App.LogApp("WinMM-Controller getrennt: Slot " + ctrl.SlotIndex);
                        try
                        {
                            StateChanged?.Invoke(this, new ControllerStateEventArgs
                            {
                                ControllerName = "None",
                                ConnectionType = "N/A",
                                PressedButtons = new List<string>()
                            });
                            GamepadsChanged?.Invoke(this, EventArgs.Empty);
                        }
                        catch { }
                    }
                    return;
                }

                if (!ctrl.Connected)
                {
                    ctrl.Connected = true;
                    App.LogApp("WinMM-Controller verbunden: Slot " + ctrl.SlotIndex);
                    try { GamepadsChanged?.Invoke(this, EventArgs.Empty); } catch { }
                }
                lock (_cooldownLock) { _winmmCooldownUntil.Remove(ctrl.SlotIndex); }

                var pressed = new List<string>();
                uint b = joyInfo.dwButtons;

                // Map buttons: A/B/X/Y are usually buttons 0-3
                if ((b & 0x01) != 0) pressed.Add("A");
                if ((b & 0x02) != 0) pressed.Add("B");
                if ((b & 0x04) != 0) pressed.Add("X");
                if ((b & 0x08) != 0) pressed.Add("Y");
                if ((b & 0x10) != 0) pressed.Add("L1");
                if ((b & 0x20) != 0) pressed.Add("R1");
                if ((b & 0x40) != 0) pressed.Add("L2");
                if ((b & 0x80) != 0) pressed.Add("R2");
                if ((b & 0x100) != 0) pressed.Add("SELECT");
                if ((b & 0x200) != 0) pressed.Add("START");
                if ((b & 0x400) != 0) pressed.Add("L3");
                if ((b & 0x800) != 0) pressed.Add("R3");

                // POV hat (D-Pad)
                uint pov = joyInfo.dwPOV;
                if (pov != 0xFFFF)
                {
                    if (pov >= 31500 || pov <= 4500) pressed.Add("DPAD_UP");
                    if (pov >= 13500 && pov <= 22500) pressed.Add("DPAD_DOWN");
                    if (pov >= 22500 && pov <= 31500) pressed.Add("DPAD_LEFT");
                    if (pov >= 4500 && pov <= 13500) pressed.Add("DPAD_RIGHT");
                }

                // Map axes to left/right sticks
                // X/Y = left stick, R/Z = right stick (typical mapping)
                int x = joyInfo.dwXpos;
                int y = joyInfo.dwYpos;
                int r = joyInfo.dwRpos;
                int u = joyInfo.dwUpos;

                // Get axis ranges from caps (JOYCAPSW ist jetzt nativ korrekt ausgerichtet)
                int xMin = 0, xMax = 65535;
                int yMin = 0, yMax = 65535;
                int rMin = 0, rMax = 65535;
                int uMin = 0, uMax = 65535;
                try
                {
                    joyGetDevCapsW((uint)ctrl.SlotIndex, out JOYCAPSW caps, Marshal.SizeOf<JOYCAPSW>());
                    xMin = (int)caps.wXmin; xMax = (int)caps.wXmax;
                    yMin = (int)caps.wYmin; yMax = (int)caps.wYmax;
                    rMin = (int)caps.wRmin; rMax = (int)caps.wRmax;
                    uMin = (int)caps.wUmin; uMax = (int)caps.wUmax;
                }
                catch { }

                float lx = NormalizeAxis(x, xMin, xMax);
                float ly = NormalizeAxis(y, yMin, yMax);
                float rx = NormalizeAxis(r, rMin, rMax);
                // Rechter Stick Y ist die U-Achse (HID Ry) – vorher wurde dwZpos (Z-Achse,
                // meist linker Trigger) gelesen, aber mit dem U-Bereich normiert.
                float ry = NormalizeAxis(u, uMin, uMax);

                if (lx < -0.7f) pressed.Add("LS_LEFT");
                else if (lx > 0.7f) pressed.Add("LS_RIGHT");
                if (ly > 0.7f) pressed.Add("LS_UP");
                else if (ly < -0.7f) pressed.Add("LS_DOWN");

                if (rx < -0.7f) pressed.Add("RS_LEFT");
                else if (rx > 0.7f) pressed.Add("RS_RIGHT");
                if (ry > 0.7f) pressed.Add("RS_UP");
                else if (ry < -0.7f) pressed.Add("RS_DOWN");

                try
                {
                    StateChanged?.Invoke(this, new ControllerStateEventArgs
                    {
                        ControllerName = ctrl.Name,
                        ConnectionType = ctrl.ConnectionType,
                        PressedButtons = pressed,
                        LeftStickX = lx, LeftStickY = ly,
                        RightStickX = rx, RightStickY = ry
                    });
                }
                catch (Exception ex)
                {
                    App.LogApp("Controller WinMM StateChanged-Fehler: " + ex.Message);
                }
            }
            catch { }
        }

        /// <summary>
        /// True für die HID-Fassade, die Windows zu jedem XInput-Pad anlegt
        /// (VID_045E&PID_02FF). Sie ist kein eigenständiges Gerät und darf weder
        /// als Zusatz-Controller erscheinen noch über den Fallback gelesen werden.
        /// </summary>
        private static bool IsXInputHidFacade(ushort vid, ushort pid)
            => vid == 0x045E && pid == 0x02FF;

        private static float NormalizeAxis(int value, int min, int max)
        {
            if (max <= min) return 0f;
            // Normalize to -1..1 range with center at (min+max)/2
            float center = (min + max) / 2f;
            float range = (max - min) / 2f;
            if (range < 1) return 0f;
            return Math.Clamp((value - center) / range, -1f, 1f);
        }
    }
}

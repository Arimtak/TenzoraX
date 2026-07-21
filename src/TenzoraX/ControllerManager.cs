using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace TenzoraX
{
    public class ControllerStateEventArgs : EventArgs
    {
        public string ControllerName { get; set; } = "";
        public string ConnectionType { get; set; } = "Unknown";
        public string BatteryInfo { get; set; } = "N/A";
        public List<string> PressedButtons { get; set; } = new();
        public double LeftStickX { get; set; }
        public double LeftStickY { get; set; }
        public double RightStickX { get; set; }
        public double RightStickY { get; set; }
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

        // XInput Battery
        [DllImport("xinput1_4.dll")]
        private static extern uint XInputGetBatteryInformation(uint dwUserIndex, byte devType, out XINPUT_BATTERY_INFORMATION pBatteryInformation);

        private const byte BATTERY_DEVTYPE_GAMEPAD = 0x00;
        private const byte BATTERY_TYPE_WIRED      = 0x01;
        private const byte BATTERY_TYPE_ALKALINE   = 0x02;
        private const byte BATTERY_TYPE_NIMH       = 0x03;
        private const byte BATTERY_TYPE_DISCONNECTED = 0x00;
        private const byte BATTERY_TYPE_UNKNOWN    = 0xFF;
        private const byte BATTERY_LEVEL_EMPTY     = 0x00;
        private const byte BATTERY_LEVEL_LOW       = 0x01;
        private const byte BATTERY_LEVEL_MEDIUM    = 0x02;
        private const byte BATTERY_LEVEL_FULL      = 0x03;

        [StructLayout(LayoutKind.Sequential)]
        private struct XINPUT_BATTERY_INFORMATION
        {
            public byte BatteryType;
            public byte BatteryLevel;
        }


        // ============================================================
        //  WINMM JOYSTICK API (8BitDo, PS4/5, Switch Pro, etc.)
        // ============================================================

        [DllImport("winmm.dll")]
        private static extern int joyGetNumDevs();

        [DllImport("winmm.dll")]
        private static extern int joyGetDevCapsW(IntPtr uJoyID, out JOYCAPSW pjc, int cbjc);

        [DllImport("winmm.dll")]
        private static extern int joyGetPosEx(uint uJoyID, out JOYINFOEX pji);

        private const int JOYERR_NOERROR = 0;
        private const int JOYERR_UNPLUGGED = 165;
        private const uint JOY_RETURNALL = 0x000000FF;
        private const uint JOY_RETURNPOVCTS = 0x00000200;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct JOYCAPSW
        {
            public ushort wMid;
            public ushort wPid;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string szPname;
            public int wXmin;
            public int wXmax;
            public int wYmin;
            public int wYmax;
            public int wZmin;
            public int wZmax;
            public int wNumButtons;
            public int wPeriodMin;
            public int wPeriodMax;
            public int wRmin;
            public int wRmax;
            public int wUmin;
            public int wUmax;
            public int wVmin;
            public int wVmax;
            public int wCaps;
            public int wMaxAxes;
            public int wNumAxes;
            public int wMaxButtons;
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
        //  COMBINED CONTROLLER INFO
        // ============================================================

        private class ControllerInfo
        {
            public int SlotIndex;       // 0-3 for XInput, 0-15 for WinMM
            public bool IsXInput;
            public string Name = "";
            public bool Connected;
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
        private int _selectedIndex = 0;
        private int _winmmCount = 0;

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

        public int ActiveGamepadIndex
        {
            get => _selectedIndex;
            set { if (value >= 0) _selectedIndex = value; }
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

        private ControllerManager() { }

        public void Initialize()
        {
            if (_isRunning) return;
            _isRunning = true;

            _pollThread = new Thread(PollLoop)
            {
                IsBackground = true,
                Priority = ThreadPriority.BelowNormal,
                Name = "TenzoraX-Poll"
            };
            _pollThread.Start();
        }

        public void Shutdown() => _isRunning = false;

        // ============================================================
        //  POLLING LOOP (EVERY 15ms = ~66Hz)
        // ============================================================

        private void PollLoop()
        {
            int scanTimer = 0;

            while (_isRunning)
            {
                try
                {
                    scanTimer++;

                    // Full rescan every ~300ms (every 20 iterations at 15ms)
                    if (scanTimer >= 20)
                    {
                        scanTimer = 0;
                        RescanControllers();
                    }

                    // Find the Nth connected controller for our selection
                    int connectedCount = 0;
                    ControllerInfo? selectedCtrl = null;

                    lock (_controllers)
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

                    if (selectedCtrl != null)
                    {
                        if (selectedCtrl.IsXInput)
                            ReadXInput(selectedCtrl);
                        else
                            ReadWinMM(selectedCtrl);
                    }
                    else
                    {
                        // No controller selected - fire empty state
                        if (scanTimer % 5 == 0)
                        {
                            try
                            {
                                StateChanged?.Invoke(this, new ControllerStateEventArgs
                                {
                                    ControllerName = "None",
                                    ConnectionType = "N/A",
                                    BatteryInfo = "N/A",
                                    PressedButtons = new List<string>()
                                });
                            }
                            catch { }
                        }
                    }
                }
                catch { }

                Thread.Sleep(15);
            }
        }

        // ============================================================
        //  RESCAN CONTROLLERS
        // ============================================================

        private void RescanControllers()
        {
            int oldCount = 0;
            lock (_controllers) { oldCount = _controllers.Count; }

            // 1. Scan all 4 XInput slots
            int xinputFound = 0;
            for (int i = 0; i < 4; i++)
            {
                uint rc = XInputGetState((uint)i, out _);
                bool connected = (rc == ERROR_SUCCESS);
                if (connected) xinputFound++;
            }

            // 2. Scan WinMM Joystick API (for 8BitDo, PS, Switch, etc.)
            int winmmDevices = 0;
            try
            {
                winmmDevices = joyGetNumDevs();
                if (winmmDevices < 0) winmmDevices = 0;
                if (winmmDevices > 16) winmmDevices = 16;
            }
            catch { winmmDevices = 0; }

            _winmmCount = winmmDevices;

            // 3. Build combined list
            var newControllers = new List<ControllerInfo>();

            // XInput first
            for (int i = 0; i < 4; i++)
            {
                uint rc = XInputGetState((uint)i, out _);
                bool connected = (rc == ERROR_SUCCESS);
                if (connected)
                {
                    newControllers.Add(new ControllerInfo
                    {
                        SlotIndex = i,
                        IsXInput = true,
                        Name = $"Controller {newControllers.Count + 1} (Xbox)",
                        Connected = true
                    });
                }
            }

            // WinMM second
            for (int i = 0; i < winmmDevices; i++)
            {
                try
                {
                    int rc = joyGetDevCapsW((IntPtr)i, out JOYCAPSW caps, Marshal.SizeOf<JOYCAPSW>());
                    if (rc == JOYERR_NOERROR)
                    {
                        string name = caps.szPname.Trim();
                        if (string.IsNullOrEmpty(name))
                            name = $"Controller {newControllers.Count + 1} (Gamepad)";

                        // Check if still connected
                        var joyInfo = new JOYINFOEX();
                        joyInfo.dwSize = Marshal.SizeOf<JOYINFOEX>();
                        joyInfo.dwFlags = JOY_RETURNALL;
                        int posRc = joyGetPosEx((uint)i, out joyInfo);

                        bool connected = (posRc == JOYERR_NOERROR);
                        if (connected)
                        {
                            newControllers.Add(new ControllerInfo
                            {
                                SlotIndex = i,
                                IsXInput = false,
                                Name = name,
                                Connected = true
                            });
                        }
                    }
                }
                catch { }
            }

            // 4. Swap lists
            lock (_controllers)
            {
                _controllers.Clear();
                _controllers.AddRange(newControllers);
            }

            // Fix selection
            int newCount = newControllers.Count;
            if (_selectedIndex >= newCount && newCount > 0)
                _selectedIndex = newCount - 1;

            // Notify on change
            if (oldCount != newCount)
            {
                try { GamepadsChanged?.Invoke(this, EventArgs.Empty); } catch { }
            }
        }

        // ============================================================
        //  READ XINPUT CONTROLLER
        // ============================================================

        private void ReadXInput(ControllerInfo ctrl)
        {
            var rc = XInputGetState((uint)ctrl.SlotIndex, out var state);
            if (rc != ERROR_SUCCESS) return;

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

            // Read battery level for XInput controller
            string batteryInfo = "N/A";
            try
            {
                uint brc = XInputGetBatteryInformation((uint)ctrl.SlotIndex, BATTERY_DEVTYPE_GAMEPAD, out var batt);
                if (brc == ERROR_SUCCESS)
                {
                    // Batteriestand immer anzeigen - egal ob Kabel oder Wireless
                    batteryInfo = batt.BatteryLevel switch
                    {
                        BATTERY_LEVEL_EMPTY  => "0%",
                        BATTERY_LEVEL_LOW    => "33%",
                        BATTERY_LEVEL_MEDIUM => "67%",
                        BATTERY_LEVEL_FULL   => "100%",
                        _                    => "N/A"
                    };
                }
            }
            catch { }

            try
            {
                StateChanged?.Invoke(this, new ControllerStateEventArgs
                {
                    ControllerName = ctrl.Name,
                    ConnectionType = "XInput",
                    BatteryInfo = batteryInfo,
                    PressedButtons = pressed,
                    LeftStickX = lx, LeftStickY = ly,
                    RightStickX = rx, RightStickY = ry
                });
            }
            catch { }
        }

        // ============================================================
        //  READ WINMM JOYSTICK (8BitDo, PS, Switch, etc.)
        // ============================================================

        private void ReadWinMM(ControllerInfo ctrl)
        {
            try
            {
                var joyInfo = new JOYINFOEX();
                joyInfo.dwSize = Marshal.SizeOf<JOYINFOEX>();
                joyInfo.dwFlags = JOY_RETURNALL | JOY_RETURNPOVCTS;

                int rc = joyGetPosEx((uint)ctrl.SlotIndex, out joyInfo);
                if (rc != JOYERR_NOERROR) return;

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
                int z = joyInfo.dwZpos;

                // Get axis ranges from caps
                int xMin = 0, xMax = 65535;
                int yMin = 0, yMax = 65535;
                int rMin = 0, rMax = 65535;
                int zMin = 0, zMax = 65535;
                try
                {
                    joyGetDevCapsW((IntPtr)ctrl.SlotIndex, out JOYCAPSW caps, Marshal.SizeOf<JOYCAPSW>());
                    xMin = caps.wXmin; xMax = caps.wXmax;
                    yMin = caps.wYmin; yMax = caps.wYmax;
                    rMin = caps.wRmin; rMax = caps.wRmax;
                    zMin = caps.wUmin; zMax = caps.wUmax;
                }
                catch { }

                float lx = NormalizeAxis(x, xMin, xMax);
                float ly = NormalizeAxis(y, yMin, yMax);
                float rx = NormalizeAxis(r, rMin, rMax);
                float ry = NormalizeAxis(z, zMin, zMax);

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
                        ConnectionType = "WinMM Joystick",
                        BatteryInfo = "N/A",
                        PressedButtons = pressed,
                        LeftStickX = lx, LeftStickY = ly,
                        RightStickX = rx, RightStickY = ry
                    });
                }
                catch { }
            }
            catch { }
        }

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
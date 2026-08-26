using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace TenzoraX.Detection
{
    /// <summary>
    /// Erkennung über die klassische WinMM-Joystick-API (winmm.dll, joyGetDevCapsW).
    /// Die JOYCAPSW-Definition wurde 1:1 aus dem bisherigen, funktionsfähigen Code
    /// übernommen (cbjc = Marshal.SizeOf = 386 Bytes).
    /// </summary>
    public sealed class WinMMControllerProvider : IControllerProvider
    {
        private const int JOYERR_NOERROR = 0;
        private const uint JOYCAPS_HASPOV = 0x00000010;

        [DllImport("winmm.dll")]
        private static extern int joyGetNumDevs();

        [DllImport("winmm.dll")]
        private static extern int joyGetDevCapsW(uint uJoyID, out JOYCAPSW pjc, int cbjc);

        /// <summary>
        /// JOYCAPSW exakt wie im bisherigen, auf diesem System erprobten Code
        /// (Feldreihenfolge/-typen unverändert übernommen; Marshal.SizeOf = 386).
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        internal struct JOYCAPSW
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
            public int wRmin;
            public int wRmax;
            public int wUmin;
            public int wUmax;
            public int wVmin;
            public int wVmax;
            public uint wCaps;
            public ushort wMaxAxes;
            public ushort wNumAxes;
            public ushort wMaxButtons;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string szRegKey;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string szOEMVxD;
        }

        public string ApiName => "WinMM";

        List<DetectedDeviceInfo> IControllerProvider.Enumerate() => Enumerate();

        /// <summary>Listet alle über WinMM sichtbaren Joysticks/Gamepads inklusive Caps auf.</summary>
        public static List<DetectedDeviceInfo> Enumerate()
        {
            var result = new List<DetectedDeviceInfo>();
            try
            {
                int devices = joyGetNumDevs();
                if (devices < 0) devices = 0;
                if (devices > 16) devices = 16;

                for (uint i = 0; i < devices; i++)
                {
                    try
                    {
                        if (joyGetDevCapsW(i, out JOYCAPSW caps, Marshal.SizeOf<JOYCAPSW>()) != JOYERR_NOERROR)
                            continue;

                        string vidPid = (caps.wMid != 0 || caps.wPid != 0)
                            ? DeviceHelpers.FormatVidPid(caps.wMid, caps.wPid) : "";
                        string capsName = caps.szPname?.Trim() ?? "";
                        if (string.IsNullOrEmpty(capsName) && string.IsNullOrEmpty(vidPid))
                            continue;   // unbenutzter Dummy-Slot

                        string oemName = DeviceHelpers.GetWinMmOemName(caps.szRegKey ?? "");

                        result.Add(new DetectedDeviceInfo
                        {
                            Api = ControllerApi.WinMM,
                            Name = !string.IsNullOrEmpty(oemName) ? oemName : capsName,
                            Manufacturer = DeviceHelpers.GetKnownVendorName(caps.wMid),
                            Vid = caps.wMid,
                            Pid = caps.wPid,
                            VidPid = vidPid,
                            SlotIndex = (int)i,
                            Axes = (int)caps.wNumAxes,
                            Buttons = (int)caps.wMaxButtons,
                            Povs = (caps.wCaps & JOYCAPS_HASPOV) != 0 ? 1 : 0
                        });
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                App.LogApp("WinMM-Enumeration fehlgeschlagen: " + ex.Message);
            }
            return result;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace TenzoraX.Detection
{
    /// <summary>
    /// Erkennung über die klassische WinMM-Joystick-API (winmm.dll, joyGetDevCapsW).
    /// Die JOYCAPSW-Definition entspricht dem nativen Windows-Layout
    /// (cbjc = Marshal.SizeOf = 720 Bytes).
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
    /// JOYCAPSW exakt nach dem nativen Windows-Layout (joystickapi.h):
    /// WORD wMid; WORD wPid; WCHAR szPname[32];
    /// DWORD wXmin..wZmax; DWORD wNumButtons; DWORD wPeriodMin; DWORD wPeriodMax;
    /// DWORD wRmin..wVmax; WORD wCaps; WORD wMaxAxes; WORD wNumAxes; WORD wMaxButtons;
    /// WCHAR szRegKey[32]; WCHAR szOEMVxD[260]. Native Größe = 720 Byte.
    /// Mit einer abweichenden Größe (vorher 388) antwortet joyGetDevCapsW für JEDEN
    /// Slot mit JOYERR_PARMS (165) – WinMM-Geräte waren damit nie sichtbar.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct JOYCAPSW
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

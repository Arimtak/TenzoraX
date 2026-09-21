using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace TenzoraX.Detection
{
    /// <summary>
    /// Erkennung über DirectInput8 (dinput8.dll) – dieselbe Schnittstelle, die auch
    /// joy.cpl und Steam verwenden ("Generic Gamepad"). Liefert Produktname, GUIDs
    /// und Achsen-/Tasten-/POV-Anzahl unabhängig vom Transport (USB/Bluetooth).
    /// </summary>
    public sealed class DirectInputControllerProvider : IControllerProvider
    {
        private const uint DIRECTINPUT_VERSION = 0x0800;
        internal const uint DI8DEVCLASS_GAMECTRL = 4;
        private const uint DIEDFL_ATTACHEDONLY = 0x00000001;

        // DI8DEVTYPE-Werte (niedriges Byte von dwDevType)
        internal const byte DI8DEVTYPE_JOYSTICK = 0x14;
        internal const byte DI8DEVTYPE_GAMEPAD = 0x15;

        private static readonly Guid IID_IDirectInput8W = new("BF798031-483A-4DA2-AA99-5D64ED369700");

        public string ApiName => "DirectInput";

        List<DetectedDeviceInfo> IControllerProvider.Enumerate() => Enumerate();

        /// <summary>Listet alle DirectInput-Joysticks/Gamepads auf; Fehler werden abgefangen.
        /// <paramref name="verboseLogging"/>: false unterdrückt die per-Device-Logs,
        /// die sonst bei jedem Rescan den Log (Datei-I/O) fluten.</summary>
        public static List<DetectedDeviceInfo> Enumerate(bool verboseLogging = true)
        {
            var result = new List<DetectedDeviceInfo>();
            IntPtr dinputPtr = IntPtr.Zero;
            object? dinputCom = null;
            try
            {
                Guid iid = IID_IDirectInput8W;   // lokale Kopie (ref-Parameter)
                // GetModuleHandle(null): echtes EXE-Modul-Handle. Marshal.GetHINSTANCE
                // liefert in Single-File-Bundles -1 → DirectInput8Create: E_INVALIDARG.
                int hr = DirectInput8Create(
                    GetModuleHandle(null),
                    DIRECTINPUT_VERSION, ref iid, out dinputPtr, IntPtr.Zero);
                if (hr != 0 || dinputPtr == IntPtr.Zero)
                {
                    App.LogApp("DirectInput: DirectInput8Create fehlgeschlagen (hr=0x" + hr.ToString("X8") + ")");
                    return result;
                }
                dinputCom = Marshal.GetObjectForIUnknown(dinputPtr);
                if (dinputCom is not IDirectInput8W dinput)
                {
                    App.LogApp("DirectInput: IDirectInput8W-Interface nicht verfügbar");
                    return result;
                }

                var callback = new EnumDevCallback(OnEnumDevice);
                var state = new EnumState(result, dinput, verboseLogging);
                var stateHandle = GCHandle.Alloc(state);
                try
                {
                    // COM-vTable-Aufruf: Delegat explizit als Funktionszeiger übergeben
                    // (direkte Delegate-Parameter werden von .NET nicht als solche
                    // gemarshallt → "_Delegate-Interface"-Fehler).
                    IntPtr fnPtr = Marshal.GetFunctionPointerForDelegate(callback);
                    dinput.EnumDevices(DI8DEVCLASS_GAMECTRL, fnPtr, (IntPtr)stateHandle, DIEDFL_ATTACHEDONLY);
                }
                finally { stateHandle.Free(); GC.KeepAlive(callback); }
                if (verboseLogging)
                    App.LogApp("DI-Enumeration: " + result.Count + " DirectInput-Gamepads gefunden");
            }
            catch (Exception ex)
            {
                App.LogApp("DirectInput-Enumeration fehlgeschlagen: " + ex.GetType().Name + ": " + ex.Message);
            }
            finally
            {
                if (dinputCom != null)
                {
                    try { Marshal.FinalReleaseComObject(dinputCom); } catch { }
                }
            }
            return result;
        }

        private sealed class EnumState
        {
            public readonly List<DetectedDeviceInfo> Result;
            public readonly IDirectInput8W Dinput;
            public readonly bool Verbose;
            public EnumState(List<DetectedDeviceInfo> result, IDirectInput8W dinput, bool verbose)
            {
                Result = result;
                Dinput = dinput;
                Verbose = verbose;
            }
        }

        private delegate int EnumDevCallback(ref DIDEVICEINSTANCEW lpddi, IntPtr pvRef);

        private static int OnEnumDevice(ref DIDEVICEINSTANCEW lpddi, IntPtr pvRef)
        {
            try
            {
                // Native dwSize ist 1100 (inkl. tszInstanceName), ältere Annahme 580 war falsch und verursachte Heap-Corruption 0xc0000374
                if (((GCHandle)pvRef).Target is not EnumState state) return 0;

                if (lpddi.dwSize < 580)
                {
                    if (state.Verbose)
                        App.LogApp("DI-Skip (dwSize=" + lpddi.dwSize + " <580): '" + lpddi.tszProductName + "'");
                    return 1;
                }
                if (state.Verbose && lpddi.dwSize != (uint)Marshal.SizeOf<DIDEVICEINSTANCEW>())
                    App.LogApp("DI-Warnung: dwSize=" + lpddi.dwSize + " != Marshal.SizeOf=" + Marshal.SizeOf<DIDEVICEINSTANCEW>());

                // HID-Geräte kodieren VID/PID in guidProduct.Data1 (VID | PID << 16)
                uint data1 = BitConverter.ToUInt32(lpddi.guidProduct.ToByteArray(), 0);
                ushort vid = (ushort)(data1 & 0xFFFF);
                ushort pid = (ushort)((data1 >> 16) & 0xFFFF);

                if (state.Verbose)
                    App.LogApp("DI-Device: '" + lpddi.tszProductName + "' VID=0x" + vid.ToString("X4") + " PID=0x" + pid.ToString("X4") + " DevType=0x" + lpddi.dwDevType.ToString("X8") + " Page=0x" + lpddi.wUsagePage.ToString("X4") + " Usage=0x" + lpddi.wUsage.ToString("X4") + " dwSize=" + lpddi.dwSize);

                var info = new DetectedDeviceInfo
                {
                    Api = ControllerApi.DirectInput,
                    Name = lpddi.tszProductName.Trim(),
                    Manufacturer = DeviceHelpers.GetKnownVendorName(vid),
                    Vid = vid,
                    Pid = pid,
                    VidPid = vid != 0 || pid != 0 ? DeviceHelpers.FormatVidPid(vid, pid) : "",
                    GuidInstance = lpddi.guidInstance.ToString("B").ToUpperInvariant(),
                    GuidProduct = lpddi.guidProduct.ToString("B").ToUpperInvariant(),
                    DevTypeRaw = lpddi.dwDevType,
                    UsagePage = lpddi.wUsagePage,
                    Usage = lpddi.wUsage
                };

                FillCapabilities(state.Dinput, ref lpddi.guidInstance, info, state.Verbose);
                if (state.Verbose)
                    App.LogApp("DI-Caps: '" + info.Name + "' Axes=" + info.Axes + " Buttons=" + info.Buttons + " POVs=" + info.Povs);

                lock (state.Result) { state.Result.Add(info); }
            }
            catch (Exception ex)
            {
                App.LogApp("DI-OnEnumDevice Fehler: " + ex.GetType().Name + ": " + ex.Message);
            }
            return 1;   // Enumeration fortsetzen
        }

        /// <summary>Ermittelt Achsen-/Tasten-/POV-Anzahl über IDirectInputDevice8::GetCapabilities.
        /// Nicht-fatale Diagnose-Logs sind hinter <paramref name="verbose"/> – sie würden
        /// sonst bei JEDEM Rescan erscheinen (z.B. GetCapabilities-Fehler eines Geräts).</summary>
        private static void FillCapabilities(IDirectInput8W dinput, ref Guid guidInstance, DetectedDeviceInfo info, bool verbose)
        {
            object? devCom = null;
            try
            {
                int hr = dinput.CreateDevice(ref guidInstance, out IntPtr devPtr, IntPtr.Zero);
                if (hr != 0 || devPtr == IntPtr.Zero)
                {
                    if (verbose)
                        App.LogApp("DI-CreateDevice fehlgeschlagen hr=0x" + hr.ToString("X8") + " GUID=" + guidInstance.ToString("B"));
                    return;
                }
                devCom = Marshal.GetObjectForIUnknown(devPtr);
                Marshal.Release(devPtr);   // RCW hält eigene Referenz
                if (devCom is not IDirectInputDevice8W device)
                {
                    if (verbose)
                        App.LogApp("DI-CreateDevice: kein IDirectInputDevice8W für " + info.Name);
                    return;
                }

                var caps = new DIDEVCAPS { dwSize = (uint)Marshal.SizeOf<DIDEVCAPS>() };
                int capsHr = device.GetCapabilities(ref caps);
                if (capsHr == 0)
                {
                    info.Axes = (int)caps.dwAxes;
                    info.Buttons = (int)caps.dwButtons;
                    info.Povs = (int)caps.dwPOVs;
                }
                else if (verbose)
                {
                    App.LogApp("DI-GetCapabilities fehlgeschlagen hr=0x" + capsHr.ToString("X8") + " für '" + info.Name + "' dwSize=" + caps.dwSize);
                }
            }
            catch (Exception ex)
            {
                App.LogApp("DI-FillCapabilities Fehler: " + ex.GetType().Name + ": " + ex.Message);
            }
            finally
            {
                if (devCom != null)
                {
                    try { Marshal.FinalReleaseComObject(devCom); } catch { }
                }
            }
        }

        // ============================================================
        //  COM-INTEROP (vtable-genaue Präfix-Deklarationen)
        // ============================================================

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        internal struct DIDEVICEINSTANCEW
        {
            public uint dwSize;                 // muss beim Aufruf sizeof(DIDEVICEINSTANCEW)=1100 sein (native)
            public Guid guidInstance;
            public Guid guidProduct;
            public uint dwDevType;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string tszInstanceName;      // MAX_PATH wchar – fehlte vorher, verursachte Heap-Corruption (0xc0000374)
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string tszProductName;       // MAX_PATH wchar
            public Guid guidFFDriver;
            public ushort wUsagePage;
            public ushort wUsage;
        }

        /// <summary>
        /// DIDEVCAPS – 11 DWORDs = 44 Bytes, exakt wie in dinput8.h definiert.
        /// dwSize muss die echte native Größe angeben, sonst antwortet GetCapabilities
        /// mit DIERR_INVALIDPARAM (0x80070057) und die Werte bleiben leer.
        ///
        /// Fehler bis 13.09.2026: dwDevType fehlte in dieser Struktur (40 statt 44 Bytes).
        /// Folge: GetCapabilities schlug für JEDES Gerät fehl, die App kannte Achsen-,
        /// Tasten- und POV-Anzahl nie (im Bericht stand "Achsen: ? | Tasten: ?").
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        internal struct DIDEVCAPS
        {
            public uint dwSize;
            public uint dwFlags;
            public uint dwDevType;
            public uint dwAxes;
            public uint dwButtons;
            public uint dwPOVs;
            public uint dwFFSamplePeriod;
            public uint dwFFMinTimeOut;
            public uint dwFirmwareRevision;
            public uint dwHardwareRevision;
            public uint dwFFDriverVersion;
        }

        [DllImport("dinput8.dll", PreserveSig = true)]
        private static extern int DirectInput8Create(IntPtr hinst, uint dwVersion, ref Guid riidltf, out IntPtr ppvOut, IntPtr pUnkOuter);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetModuleHandle(string? lpModuleName);

        /// <summary>IDirectInput8W – nur die genutzten vtable-Slots als Präfix deklariert.</summary>
        [ComImport]
        [Guid("BF798031-483A-4DA2-AA99-5D64ED369700")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IDirectInput8W
        {
            [PreserveSig]
            int CreateDevice(ref Guid rguid, out IntPtr lplpDirectInputDevice, IntPtr pUnkOuter);

            [PreserveSig]
            int EnumDevices(uint dwDevType, IntPtr lpCallback, IntPtr pvRef, uint dwFlags);
        }

        /// <summary>IDirectInputDevice8W – Präfix bis GetCapabilities (erster Slot nach IUnknown).</summary>
        [ComImport]
        [Guid("54D41080-DC15-4833-A41B-748F73A38179")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IDirectInputDevice8W
        {
            [PreserveSig]
            int GetCapabilities(ref DIDEVCAPS lpDidiCaps);
        }
    }
}

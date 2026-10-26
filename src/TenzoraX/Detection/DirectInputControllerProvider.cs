using System;
using System.Collections.Generic;
using System.Diagnostics;
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
        private const uint DI8DEVCLASS_ALL = 0;
        private const uint DIEDFL_ATTACHEDONLY = 0x00000001;

        // DI8DEVTYPE-Werte (niedriges Byte von dwDevType)
        internal const byte DI8DEVTYPE_JOYSTICK = 0x14;
        internal const byte DI8DEVTYPE_GAMEPAD = 0x15;

        private static readonly Guid IID_IDirectInput8W = new("BF798031-483A-4DA2-AA99-5D64ED369700");

        // ============================================================
        //  Gemeinsame Sperre mit dem Windows-Panel (joy.cpl)
        // ============================================================
        // joy.cpl zählt über dieselbe DirectInput/HID-Schnittstelle auf. Läuft unsere
        // Aufzählung gleichzeitig mit dem Panel, kann ein hängender HID-Treiber
        // rundll32 zum Absturz bringen („RunDLL“, gemessen 26.10.2026). Deshalb:
        //  - Enumerate/FindSlowDevices laufen nie, während das Panel startet oder offen ist.
        //  - Das Panel startet nur, wenn keine Aufzählung mehr läuft und die letzte
        //    nicht im Stau endete (sonst würde joy.cpl sicher hängen).
        private static readonly object PanelGate = new();
        private static int _activeCalls;
        private static bool _panelExclusive;
        private static long _lastCallMs;
        private static DateTime _lastCallEndedUtc = DateTime.MinValue;

        // Zuletzt gemessene Geräte, deren Capabilities-Abfrage in einen Timeout lief.
        // Die Liste bleibt stehen, bis wieder eines gefunden wird: Der Reparaturlauf
        // startet kurz nach der Messung, und die nächste (schnelle) Aufzählung darf sie
        // ihm nicht unter den Füßen wegziehen.
        private static volatile string[] _lastSlowDevices = Array.Empty<string>();

        /// <summary>Geräte (VID/PID) mit zuletzt gemessenem 10-s-Timeout, leer wenn keins.</summary>
        public static IReadOnlyList<string> LastSlowDevices => _lastSlowDevices;

        /// <summary>Ab dieser Dauer gilt eine Aufzählung als Stau (HID-Timeouts).</summary>
        internal const long SlowCallMs = 5000;

        /// <summary>So lange nach einem Stau wird das Panel nicht geöffnet.</summary>
        internal const long SlowCallWindowMs = 180000;

        /// <summary>
        /// True, wenn die letzte Aufzählung im Stau endete und das noch frisch ist:
        /// Der Stau liegt dann meist noch an, und joy.cpl würde daran abstürzen.
        /// Rein und damit testbar.
        /// </summary>
        public static bool ShouldRefuseForStall(long lastCallMs, double msSinceLastCall,
            long slowThresholdMs = SlowCallMs, long windowMs = SlowCallWindowMs)
        {
            return lastCallMs >= slowThresholdMs && msSinceLastCall >= 0 && msSinceLastCall <= windowMs;
        }

        /// <summary>
        /// Meldet eine Aufzählung an. False, solange das Panel die Schnittstelle für sich
        /// beansprucht – dann wird in diesem Zyklus gar nicht erst aufgezählt.
        /// </summary>
        private static bool EnterCall()
        {
            lock (PanelGate)
            {
                if (_panelExclusive) return false;
                _activeCalls++;
                return true;
            }
        }

        private static void ExitCall(Stopwatch watch)
        {
            watch.Stop();
            lock (PanelGate)
            {
                _lastCallMs = watch.ElapsedMilliseconds;
                _lastCallEndedUtc = DateTime.UtcNow;
                _activeCalls--;
                Monitor.PulseAll(PanelGate);
            }
        }

        /// <summary>
        /// Fordert die Schnittstelle für das Öffnen von joy.cpl an: wartet, bis keine
        /// Aufzählung mehr läuft, und sperrt neue. False, wenn nach dem Timeout noch eine
        /// Aufzählung hängt oder die letzte im Stau endete – dann darf das Panel nicht
        /// starten, weil Windows es als RunDLL zum Absturz bringt.
        /// </summary>
        public static bool TryBeginPanelExclusive(int timeoutMs)
        {
            var watch = Stopwatch.StartNew();
            lock (PanelGate)
            {
                while (_activeCalls > 0)
                {
                    int remaining = timeoutMs - (int)watch.ElapsedMilliseconds;
                    if (remaining <= 0) return false;
                    Monitor.Wait(PanelGate, remaining);
                }

                double sinceLast = (DateTime.UtcNow - _lastCallEndedUtc).TotalMilliseconds;
                if (ShouldRefuseForStall(_lastCallMs, sinceLast)) return false;

                _panelExclusive = true;
                return true;
            }
        }

        /// <summary>Gibt die Schnittstelle nach dem Panel-Start wieder frei.</summary>
        public static void EndPanelExclusive()
        {
            lock (PanelGate)
            {
                _panelExclusive = false;
                Monitor.PulseAll(PanelGate);
            }
        }

        public string ApiName => "DirectInput";

        List<DetectedDeviceInfo> IControllerProvider.Enumerate() => Enumerate();

        /// <summary>Listet alle DirectInput-Joysticks/Gamepads auf; Fehler werden abgefangen.
        /// <paramref name="verboseLogging"/>: false unterdrückt die per-Device-Logs,
        /// die sonst bei jedem Rescan den Log (Datei-I/O) fluten.</summary>
        public static List<DetectedDeviceInfo> Enumerate(bool verboseLogging = true)
        {
            var result = new List<DetectedDeviceInfo>();
            if (!EnterCall()) return result;

            var callWatch = Stopwatch.StartNew();
            IntPtr dinputPtr = IntPtr.Zero;
            object? dinputCom = null;
            EnumState? enumState = null;
            long createMs = 0, comMs = 0, enumMs = 0;
            var phaseWatch = new Stopwatch();
            try
            {
                Guid iid = IID_IDirectInput8W;   // lokale Kopie (ref-Parameter)
                // GetModuleHandle(null): echtes EXE-Modul-Handle. Marshal.GetHINSTANCE
                // liefert in Single-File-Bundles -1 → DirectInput8Create: E_INVALIDARG.
                phaseWatch.Restart();
                int hr = DirectInput8Create(
                    GetModuleHandle(null),
                    DIRECTINPUT_VERSION, ref iid, out dinputPtr, IntPtr.Zero);
                createMs = phaseWatch.ElapsedMilliseconds;
                if (hr != 0 || dinputPtr == IntPtr.Zero)
                {
                    App.LogApp("DirectInput: DirectInput8Create fehlgeschlagen (hr=0x" + hr.ToString("X8") + ")");
                    return result;
                }
                phaseWatch.Restart();
                dinputCom = Marshal.GetObjectForIUnknown(dinputPtr);
                comMs = phaseWatch.ElapsedMilliseconds;
                if (dinputCom is not IDirectInput8W dinput)
                {
                    App.LogApp("DirectInput: IDirectInput8W-Interface nicht verfügbar");
                    return result;
                }

                var callback = new EnumDevCallback(OnEnumDevice);
                var state = new EnumState(result, dinput, verboseLogging);
                enumState = state;
                var stateHandle = GCHandle.Alloc(state);
                try
                {
                    // COM-vTable-Aufruf: Delegat explizit als Funktionszeiger übergeben
                    // (direkte Delegate-Parameter werden von .NET nicht als solche
                    // gemarshallt → "_Delegate-Interface"-Fehler).
                    IntPtr fnPtr = Marshal.GetFunctionPointerForDelegate(callback);
                    phaseWatch.Restart();
                    dinput.EnumDevices(DI8DEVCLASS_GAMECTRL, fnPtr, (IntPtr)stateHandle, DIEDFL_ATTACHEDONLY);
                    enumMs = phaseWatch.ElapsedMilliseconds;
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

                // Ein 10-s-Timeout steckt häufig nicht zwischen den Rückrufen, sondern
                // in CreateDevice/GetCapabilities eines einzelnen Geräts (gemessen
                // 26.10.2026: Aufzählung 10,4 s, aber kein Lückenwert über 2 s).
                if (enumState != null && enumState.SlowVidPids.Count > 0)
                    _lastSlowDevices = enumState.SlowVidPids.ToArray();

                if (callWatch.ElapsedMilliseconds > 2000)
                    App.LogApp("DI-Phasen: Create=" + createMs + " ms, COM=" + comMs +
                               " ms, EnumDevices=" + enumMs + " ms, gesamt=" + callWatch.ElapsedMilliseconds + " ms");

                ExitCall(callWatch);
            }
            return result;
        }

        /// <summary>
        /// Sucht Geräte, die die DirectInput-Aufzählung blockieren. Dafür wird einmal
        /// über ALLE Geräteklassen aufgezählt und die Zeit zwischen zwei Rückrufen
        /// gemessen; alles ab <paramref name="thresholdMs"/> gilt als Stau.
        ///
        /// Nötig, weil die normale Gamepad-Aufzählung zwar dieselbe Verzögerung hat,
        /// die blockierenden Tastatur-/Receiver-Sammlungen aber nicht in ihrer Liste
        /// auftauchen. Der Aufruf kann selbst so lange dauern wie der Stau – deshalb
        /// nur im Hintergrund nach einer bereits gemessenen Verzögerung aufrufen.
        /// </summary>
        public static List<string> FindSlowDevices(int thresholdMs = 2000)
        {
            var result = new List<string>();
            if (!EnterCall()) return result;

            var callWatch = Stopwatch.StartNew();
            IntPtr dinputPtr = IntPtr.Zero;
            object? dinputCom = null;
            try
            {
                Guid iid = IID_IDirectInput8W;
                int hr = DirectInput8Create(GetModuleHandle(null), DIRECTINPUT_VERSION, ref iid, out dinputPtr, IntPtr.Zero);
                if (hr != 0 || dinputPtr == IntPtr.Zero)
                {
                    App.LogApp("DI-Stau: DirectInput8Create fehlgeschlagen (hr=0x" + hr.ToString("X8") + ")");
                    return result;
                }

                dinputCom = Marshal.GetObjectForIUnknown(dinputPtr);
                if (dinputCom is not IDirectInput8W dinput)
                {
                    App.LogApp("DI-Stau: IDirectInput8W-Interface nicht verfügbar");
                    return result;
                }

                var callback = new EnumDevCallback(OnSlowProbeDevice);
                var state = new SlowProbeState(thresholdMs);
                var stateHandle = GCHandle.Alloc(state);
                try
                {
                    IntPtr fnPtr = Marshal.GetFunctionPointerForDelegate(callback);
                    dinput.EnumDevices(DI8DEVCLASS_ALL, fnPtr, (IntPtr)stateHandle, DIEDFL_ATTACHEDONLY);
                }
                finally { stateHandle.Free(); GC.KeepAlive(callback); }

                result.AddRange(state.SlowVidPids);
                App.LogApp(result.Count == 0
                    ? "DI-Stau: kein einzelnes Gerät über " + thresholdMs + " ms gefunden."
                    : "DI-Stau: blockierende Geräte: " + string.Join(", ", result));
            }
            catch (Exception ex)
            {
                App.LogApp("DI-Stau: Messung fehlgeschlagen: " + ex.GetType().Name + ": " + ex.Message);
            }
            finally
            {
                if (dinputCom != null)
                {
                    try { Marshal.FinalReleaseComObject(dinputCom); } catch { }
                }
                ExitCall(callWatch);
            }
            return result;
        }

        private sealed class SlowProbeState
        {
            public readonly int ThresholdMs;
            public readonly Stopwatch Gap = Stopwatch.StartNew();
            public readonly List<string> SlowVidPids = new();
            public readonly HashSet<string> Seen = new(StringComparer.OrdinalIgnoreCase);

            public SlowProbeState(int thresholdMs) { ThresholdMs = thresholdMs; }
        }

        private static int OnSlowProbeDevice(ref DIDEVICEINSTANCEW lpddi, IntPtr pvRef)
        {
            try
            {
                if (((GCHandle)pvRef).Target is not SlowProbeState state) return 0;

                long gap = state.Gap.ElapsedMilliseconds;
                state.Gap.Restart();
                if (gap < state.ThresholdMs) return 1;

                uint data1 = BitConverter.ToUInt32(lpddi.guidProduct.ToByteArray(), 0);
                ushort vid = (ushort)(data1 & 0xFFFF);
                ushort pid = (ushort)((data1 >> 16) & 0xFFFF);
                if (vid == 0 && pid == 0) return 1;

                string vidPid = DeviceHelpers.FormatVidPid(vid, pid);
                if (state.Seen.Add(vidPid))
                {
                    state.SlowVidPids.Add(vidPid);
                    App.LogApp("DI-Stau: " + vidPid + " '" + lpddi.tszProductName + "' blockierte " + gap + " ms.");
                }
            }
            catch { }
            return 1;
        }

        private sealed class EnumState
        {
            public readonly List<DetectedDeviceInfo> Result;
            public readonly IDirectInput8W Dinput;
            public readonly bool Verbose;
            public readonly List<string> SlowVidPids = new();
            public readonly HashSet<string> SeenSlow = new(StringComparer.OrdinalIgnoreCase);
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

                var capsWatch = Stopwatch.StartNew();
                FillCapabilities(state.Dinput, ref lpddi.guidInstance, info, state.Verbose);
                capsWatch.Stop();

                // Ab 2 s ist es kein normaler Zugriff mehr, sondern ein HID-Timeout
                // (die Funk-Receiver brauchen exakt 10 s, wenn sie eingeschlafen sind).
                if (capsWatch.ElapsedMilliseconds >= 2000 && info.VidPid != "" && state.SeenSlow.Add(info.VidPid))
                {
                    state.SlowVidPids.Add(info.VidPid);
                    App.LogApp("DI-Stau: " + info.VidPid + " '" + info.Name + "' brauchte " +
                               capsWatch.ElapsedMilliseconds + " ms für Capabilities.");
                }

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

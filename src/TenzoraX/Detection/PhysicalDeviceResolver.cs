using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace TenzoraX.Detection
{
    /// <summary>
    /// Ein Knoten aus dem Windows-Gerätebaum: die eigene Instanz-ID und die des
    /// übergeordneten Geräts (DEVPKEY_Device_Parent).
    /// </summary>
    public sealed class DeviceNode
    {
        public DeviceNode(string instanceId, string parentInstanceId)
        {
            InstanceId = instanceId ?? "";
            ParentInstanceId = parentInstanceId ?? "";
        }

        public string InstanceId { get; }
        public string ParentInstanceId { get; }
    }

    /// <summary>
    /// Bestimmt, welche Geräte-Instanz-IDs zu EINEM physischen Controller gehören.
    ///
    /// Warum das nötig ist: viele Pads (z.B. 8BitDo Ultimate) hängen sich als
    /// Composite-Gerät mit mehreren Schnittstellen an den Bus – einer XInput-
    /// Schnittstelle ("Xbox 360-Controller für Windows"), mehreren HID-Sammlungen
    /// und teils einem zusätzlichen, zweiten Composite-Zweig ("…&amp;IG_00") mit
    /// einem generischen HID-Gamepad. HidHide versteckt aber nur genau den
    /// übergebenen Instanzpfad – ein einziger Pfad lässt also die übrigen
    /// Schnittstellen sichtbar, und Spiele sehen das physische Pad doppelt.
    ///
    /// Die Auflösung ist rein rechnend (testbar): sie bekommt die Geräteliste
    /// übergeben. <see cref="ResolveForThisMachine"/> holt sich diese Liste aus
    /// der SetupAPI.
    /// </summary>
    public static class PhysicalDeviceResolver
    {
        /// <summary>
        /// Alle Instanz-IDs, die zusammen das physische Gerät des Ziels bilden:
        /// das Ziel selbst, seine VID/PID-gleichen Vorfahren (Interface- und
        /// Composite-Geräte) und alle Nachfahren dieser Knoten – plus gleich
        /// aufgehängte Geschwister-Zweige desselben Modells.
        /// </summary>
        public static List<string> Resolve(string? targetInstanceId, IReadOnlyList<DeviceNode>? nodes)
        {
            var result = new List<string>();
            if (string.IsNullOrWhiteSpace(targetInstanceId)) return result;

            targetInstanceId = targetInstanceId.Trim();

            // Ohne Geräteliste bleibt nur das Gerät selbst – nie mehr verstecken als bekannt ist.
            if (nodes == null || nodes.Count == 0)
            {
                result.Add(targetInstanceId);
                return result;
            }

            var byId = new Dictionary<string, DeviceNode>(StringComparer.OrdinalIgnoreCase);
            foreach (var node in nodes)
            {
                if (node != null && !string.IsNullOrEmpty(node.InstanceId))
                    byId[node.InstanceId] = node;
            }

            if (!byId.ContainsKey(targetInstanceId) ||
                !DeviceHelpers.TryParseVidPid(targetInstanceId, out ushort vid, out ushort pid))
            {
                result.Add(targetInstanceId);
                return result;
            }

            // 1. Vorfahrenkette mit identischer VID/PID: HID-Gamepad → USB-Interface → Composite.
            var chain = new List<string> { targetInstanceId };
            string current = targetInstanceId;
            while (byId.TryGetValue(current, out var node) &&
                   !string.IsNullOrEmpty(node.ParentInstanceId) &&
                   byId.TryGetValue(node.ParentInstanceId, out var parent) &&
                   HasSameVendorProduct(parent.InstanceId, vid, pid))
            {
                chain.Add(parent.InstanceId);
                current = parent.InstanceId;
            }

            string top = chain[chain.Count - 1];
            string anchorParent = byId[top].ParentInstanceId;

            // 2. Geschwister-Zweige: gleiches Modell, gleicher Aufhängepunkt (z.B. der
            //    zweite Composite-Zweig "…&IG_00" neben dem XInput-Zweig).
            var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { top };
            foreach (var node in nodes)
            {
                if (node == null || string.IsNullOrEmpty(node.InstanceId)) continue;
                if (roots.Contains(node.InstanceId)) continue;

                string? root = BranchRoot(byId, node.InstanceId, vid, pid);
                if (root == null || roots.Contains(root)) continue;
                if (!string.Equals(byId[root].ParentInstanceId, anchorParent, StringComparison.OrdinalIgnoreCase)) continue;
                if (!IsSamePhysicalUnit(LastSegment(top), LastSegment(root))) continue;

                roots.Add(root);
            }

            // 3. Sicherheitsnetz: Sind mehrere Exemplare desselben Modells erkennbar
            //    (unterschiedliche USB-Seriennummern), ist die Zuordnung des zweiten
            //    Zweigs nicht eindeutig – dann nur den eigenen Zweig verstecken, statt
            //    womöglich das falsche Pad unsichtbar zu machen.
            var serials = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var root in roots)
            {
                string suffix = LastSegment(root);
                if (IsSerialLike(suffix)) serials.Add(suffix);
            }
            if (serials.Count > 1)
            {
                roots.Clear();
                roots.Add(top);
            }

            // 4. Das Ziel, die Kette, alle Zweig-Wurzeln und deren Nachfahren sammeln.
            //    Reihenfolge folgt dem Gerätebaum – so ist das Ergebnis reproduzierbar.
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var id in chain)
                if (seen.Add(id)) result.Add(id);

            foreach (var node in nodes)
            {
                if (node == null || string.IsNullOrEmpty(node.InstanceId) || seen.Contains(node.InstanceId)) continue;
                if (roots.Contains(node.InstanceId) || IsDescendantOfAny(byId, node.InstanceId, roots))
                {
                    seen.Add(node.InstanceId);
                    result.Add(node.InstanceId);
                }
            }

            return result;
        }

        /// <summary>Auflösung gegen den echten Gerätebaum dieses Rechners.</summary>
        public static List<string> ResolveForThisMachine(string? instanceId)
        {
            if (string.IsNullOrWhiteSpace(instanceId)) return new List<string>();
            return Resolve(instanceId, EnumeratePresentDevices());
        }

        /// <summary>
        /// Nur die HID-Schnittstellen einer Gerätegruppe.
        ///
        /// Sie sind der Grund, warum Steam und andere Launcher einen Controller doppelt
        /// führen: Neben dem XInput-Gerät erscheint dasselbe Pad zusätzlich als
        /// generisches HID-/DirectInput-Gamepad. Werden NUR diese versteckt, bleibt der
        /// Controller über XInput sichtbar – der Nutzer sieht ihn genau einmal, ganz
        /// ohne virtuellen Controller.
        /// </summary>
        public static List<string> SelectHidInterfaces(IEnumerable<string>? instanceIds)
        {
            var result = new List<string>();
            if (instanceIds == null) return result;

            foreach (var raw in instanceIds)
            {
                if (string.IsNullOrWhiteSpace(raw)) continue;
                string id = raw.Trim();
                if (!IsHidInstance(id)) continue;
                if (result.Any(existing => string.Equals(existing, id, StringComparison.OrdinalIgnoreCase))) continue;
                result.Add(id);
            }
            return result;
        }

        /// <summary>True, wenn die Instanz-ID ein HID-Gerät (HIDClass) beschreibt.</summary>
        public static bool IsHidInstance(string? instanceId)
            => !string.IsNullOrWhiteSpace(instanceId) &&
               instanceId!.TrimStart().StartsWith("HID\\", StringComparison.OrdinalIgnoreCase);

        /// <summary>Oberster Vorfahr (inklusive Knoten selbst) mit derselben VID/PID.</summary>
        private static string? BranchRoot(IReadOnlyDictionary<string, DeviceNode> byId, string instanceId, ushort vid, ushort pid)
        {
            if (!HasSameVendorProduct(instanceId, vid, pid)) return null;

            string current = instanceId;
            int guard = 0;
            while (byId.TryGetValue(current, out var node) &&
                   !string.IsNullOrEmpty(node.ParentInstanceId) &&
                   byId.TryGetValue(node.ParentInstanceId, out var parent) &&
                   HasSameVendorProduct(parent.InstanceId, vid, pid) &&
                   guard++ < 64)
            {
                current = parent.InstanceId;
            }
            return current;
        }

        private static bool IsDescendantOfAny(IReadOnlyDictionary<string, DeviceNode> byId, string instanceId, HashSet<string> roots)
        {
            string current = instanceId;
            int guard = 0;
            while (byId.TryGetValue(current, out var node) &&
                   !string.IsNullOrEmpty(node.ParentInstanceId) &&
                   guard++ < 64)
            {
                current = node.ParentInstanceId;
                if (roots.Contains(current)) return true;
            }
            return false;
        }

        private static bool HasSameVendorProduct(string instanceId, ushort vid, ushort pid)
            => DeviceHelpers.TryParseVidPid(instanceId, out ushort v, out ushort p) && v == vid && p == pid;

        /// <summary>Letzter Pfadabschnitt einer Instanz-ID (Seriennummer oder Port-Position).</summary>
        internal static string LastSegment(string instanceId)
        {
            if (string.IsNullOrEmpty(instanceId)) return "";
            int idx = instanceId.LastIndexOf('\\');
            return idx >= 0 && idx + 1 < instanceId.Length ? instanceId.Substring(idx + 1) : instanceId;
        }

        /// <summary>Windows hängt Seriennummern ohne '&amp;' an, Port-Positionen mit ('b&amp;362f5d08&amp;0&amp;00').</summary>
        internal static bool IsSerialLike(string segment) => !string.IsNullOrEmpty(segment) && segment.IndexOf('&') < 0;

        /// <summary>
        /// True, wenn beide Abschnitte dieselbe physische Einheit beschreiben können.
        /// Zwei verschiedene Seriennummern schließen das aus; fehlt auf einer Seite die
        /// Seriennummer (Port-Position), bleibt das Ergebnis offen.
        /// </summary>
        internal static bool IsSamePhysicalUnit(string segmentA, string segmentB)
        {
            if (!IsSerialLike(segmentA) || !IsSerialLike(segmentB)) return true;
            return string.Equals(segmentA, segmentB, StringComparison.OrdinalIgnoreCase);
        }

        // ============================================================
        //  Gerätebaum des laufenden Systems (SetupAPI)
        // ============================================================

        private const uint DIGCF_PRESENT = 0x00000002;
        private const uint DIGCF_ALLCLASSES = 0x00000004;

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

        // Wichtig: Die Klasse wird als NULL übergeben, der Bustreiber aber explizit
        // benannt. Ein Aufruf ohne Klassen-GUID UND ohne Enumerator schlägt auf
        // manchen Systemen mit ERROR_INVALID_DATA (13) fehl – mit dem Enumerator
        // ("USB", "HID", …) liefert der Aufruf zuverlässig einen gültigen Handle.
        [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Auto, EntryPoint = "SetupDiGetClassDevs")]
        private static extern IntPtr SetupDiGetClassDevsByEnumerator(IntPtr classGuid, string? enumerator, IntPtr hwndParent, uint flags);

        /// <summary>Bustreiber, unter denen Gamecontroller hängen: USB, HID und Bluetooth.</summary>
        private static readonly string[] Enumerators = { "USB", "HID", "BTHENUM", "BTHLE" };

        [DllImport("setupapi.dll", SetLastError = true)]
        private static extern bool SetupDiEnumDeviceInfo(IntPtr deviceInfoSet, uint memberIndex, ref SP_DEVINFO_DATA deviceInfoData);

        [DllImport("setupapi.dll", SetLastError = true)]
        private static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

        [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern bool SetupDiGetDeviceInstanceId(IntPtr deviceInfoSet, ref SP_DEVINFO_DATA deviceInfoData, StringBuilder deviceInstanceId, uint deviceInstanceIdSize, out uint requiredSize);

        // CharSet.Auto ist zwingend: ohne Suffix sucht der Loader die nicht existierende
        // Funktion "…A" und wirft EntryPointNotFoundException – die dann im catch der
        // Enumeration versickern würde (genau dieser Fehler hat die Auflösung anfangs
        // still auf 0 Geräte fallen lassen).
        [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern bool SetupDiGetDeviceProperty(IntPtr deviceInfoSet, ref SP_DEVINFO_DATA deviceInfoData, ref DEVPROPKEY propertyKey, out uint propertyType, IntPtr propertyBuffer, uint propertyBufferSize, out uint requiredSize, uint flags);

        [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern bool SetupDiGetDeviceRegistryProperty(IntPtr deviceInfoSet, ref SP_DEVINFO_DATA deviceInfoData, uint property, out uint propertyRegDataType, byte[]? propertyBuffer, uint propertyBufferSize, out uint requiredSize);

        [DllImport("cfgmgr32.dll")]
        private static extern uint CM_Get_DevNode_Status(out uint status, out uint problemNumber, uint devInst, uint flags);

        /// <summary>
        /// Anzahl der Geräte, deren Daten beim letzten Durchlauf nicht gelesen werden
        /// konnten. Wird für die Diagnose gemeldet, statt Fehler stillschweigend zu
        /// verschlucken.
        /// </summary>
        public static int LastReadErrors { get; private set; }

        private const uint DEVPROP_TYPE_STRING = 0x00000012;

        /// <summary>
        /// Listet alle aktuell angeschlossenen Geräte mit ihrer Eltern-Instanz-ID auf.
        /// Fehler einzelner Geräte werden übersprungen; das Ergebnis ist nie null.
        /// </summary>
        public static List<DeviceNode> EnumeratePresentDevices()
        {
            var nodes = new List<DeviceNode>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            LastReadErrors = 0;

            foreach (var enumerator in Enumerators)
            {
                IntPtr set;
                try
                {
                    set = SetupDiGetClassDevsByEnumerator(IntPtr.Zero, enumerator, IntPtr.Zero, DIGCF_PRESENT | DIGCF_ALLCLASSES);
                }
                catch { continue; }
                if (set == IntPtr.Zero || set == new IntPtr(-1)) continue;

                try
                {
                    uint index = 0;
                    while (true)
                    {
                        var data = new SP_DEVINFO_DATA { cbSize = (uint)Marshal.SizeOf<SP_DEVINFO_DATA>() };
                        if (!SetupDiEnumDeviceInfo(set, index, ref data)) break;
                        index++;

                        try
                        {
                            string instanceId = ReadInstanceId(set, ref data);
                            if (string.IsNullOrEmpty(instanceId) || !seen.Add(instanceId)) continue;
                            nodes.Add(new DeviceNode(instanceId, ReadParentInstanceId(set, ref data)));
                        }
                        catch { LastReadErrors++; }
                    }
                }
                finally
                {
                    try { SetupDiDestroyDeviceInfoList(set); } catch { }
                }
            }
            return nodes;
        }

        // ============================================================
        //  Gerätezustände: auch deaktivierte und nicht angeschlossene Knoten
        // ============================================================

        /// <summary>Zustand eines Geräteknotens aus Windows-Sicht.</summary>
        public enum DeviceState
        {
            Unknown,

            /// <summary>Läuft (Gerät ist aktiv).</summary>
            Started,

            /// <summary>Im Geräte-Manager deaktiviert (Problemcode 22).</summary>
            Disabled,

            /// <summary>Installiert, aber nicht angeschlossen (Problemcode 24 oder 45).</summary>
            NotPresent
        }

        /// <summary>Ein Geräteknoten mit Zustand, Anzeigename und Problemcode.</summary>
        public sealed class DeviceStateInfo
        {
            public string InstanceId { get; init; } = "";
            public string FriendlyName { get; init; } = "";
            public DeviceState State { get; init; }
            public uint ProblemNumber { get; init; }
        }

        private const uint CM_PROB_DISABLED = 22;
        private const uint CM_PROB_DEVICE_NOT_THERE = 24;
        private const uint CM_PROB_PHANTOM = 45;
        private const uint DN_HAS_PROBLEM = 0x00000400;
        private const uint DN_STARTED = 0x00000008;

        /// <summary>
        /// Reine Zuordnung: Statusflags + Problemcode → Zustand.
        /// Getrennt gehalten, damit die Regel ohne Gerätetreiber prüfbar ist.
        /// </summary>
        public static DeviceState ClassifyState(uint statusFlags, uint problemNumber)
        {
            if (problemNumber == CM_PROB_DISABLED) return DeviceState.Disabled;
            if (problemNumber == CM_PROB_DEVICE_NOT_THERE || problemNumber == CM_PROB_PHANTOM) return DeviceState.NotPresent;
            if ((statusFlags & DN_HAS_PROBLEM) != 0) return DeviceState.Unknown;
            if ((statusFlags & DN_STARTED) != 0) return DeviceState.Started;
            return DeviceState.Unknown;
        }

        /// <summary>
        /// Alle installierten Geräte der Controller-Bustreiber mit Zustand — auch die,
        /// die im Geräte-Manager deaktiviert wurden. Genau diese fehlen in DirectInput,
        /// im Windows-Panel und damit auch bei Steam; sichtbar sind sie nur hier.
        ///
        /// Bewusst OHNE DIGCF_PRESENT: sonst liefert Windows nur die laufenden Geräte.
        /// </summary>
        public static List<DeviceStateInfo> EnumerateDeviceStates()
        {
            var result = new List<DeviceStateInfo>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var enumerator in Enumerators)
            {
                IntPtr set;
                try { set = SetupDiGetClassDevsByEnumerator(IntPtr.Zero, enumerator, IntPtr.Zero, DIGCF_ALLCLASSES); }
                catch { continue; }
                if (set == IntPtr.Zero || set == new IntPtr(-1)) continue;

                try
                {
                    uint index = 0;
                    while (true)
                    {
                        var data = new SP_DEVINFO_DATA { cbSize = (uint)Marshal.SizeOf<SP_DEVINFO_DATA>() };
                        if (!SetupDiEnumDeviceInfo(set, index, ref data)) break;
                        index++;

                        try
                        {
                            string instanceId = ReadInstanceId(set, ref data);
                            if (string.IsNullOrEmpty(instanceId) || !seen.Add(instanceId)) continue;

                            uint status = 0, problem = 0;
                            if (CM_Get_DevNode_Status(out status, out problem, data.devInst, 0) != 0) continue;

                            result.Add(new DeviceStateInfo
                            {
                                InstanceId = instanceId,
                                FriendlyName = ReadFriendlyName(set, ref data),
                                State = ClassifyState(status, problem),
                                ProblemNumber = problem
                            });
                        }
                        catch { LastReadErrors++; }
                    }
                }
                finally
                {
                    try { SetupDiDestroyDeviceInfoList(set); } catch { }
                }
            }
            return result;
        }

        /// <summary>
        /// Anzeigename eines Knotens: bevorzugt der freundliche Name (so wie ihn der
        /// Geräte-Manager zeigt, z. B. „HID-konformes Benutzersteuergerät“), sonst die
        /// Gerätebeschreibung. Leer, wenn beides nicht lesbar ist.
        ///
        /// Hinweis: Der erste Aufruf mit Puffergröße 0 liefert die benötigte Länge NICHT
        /// zuverlässig – deshalb wird mit fester Puffergröße gelesen.
        /// </summary>
        private static string ReadFriendlyName(IntPtr set, ref SP_DEVINFO_DATA data)
        {
            const uint SPDRP_DEVICEDESC = 0x00000000;
            const uint SPDRP_FRIENDLYNAME = 0x0000000C;

            string name = ReadRegistryString(set, ref data, SPDRP_FRIENDLYNAME);
            if (string.IsNullOrEmpty(name)) name = ReadRegistryString(set, ref data, SPDRP_DEVICEDESC);
            return name;
        }

        private static string ReadRegistryString(IntPtr set, ref SP_DEVINFO_DATA data, uint property)
        {
            var buffer = new byte[1024];
            uint type = 0, required = 0;
            if (!SetupDiGetDeviceRegistryProperty(set, ref data, property, out type, buffer, (uint)buffer.Length, out required))
                return "";

            int length = 0;
            while (length + 1 < buffer.Length && !(buffer[length] == 0 && buffer[length + 1] == 0))
                length += 2;
            return System.Text.Encoding.Unicode.GetString(buffer, 0, length).Trim();
        }

        private static string ReadInstanceId(IntPtr set, ref SP_DEVINFO_DATA data)
        {
            uint required = 0;
            SetupDiGetDeviceInstanceId(set, ref data, new StringBuilder(1), 0, out required);
            if (required == 0 || required > 1024) return "";
            var sb = new StringBuilder((int)required);
            if (!SetupDiGetDeviceInstanceId(set, ref data, sb, required, out _)) return "";
            return sb.ToString();
        }

        private static string ReadParentInstanceId(IntPtr set, ref SP_DEVINFO_DATA data)
        {
            // DEVPKEY_Device_Parent {4340a6c5-93fa-4706-972c-7b648008a5a7}, PID 8
            var key = new DEVPROPKEY
            {
                fmtid = new Guid("4340a6c5-93fa-4706-972c-7b648008a5a7"),
                pid = 8
            };

            uint type = 0, required = 0;
            SetupDiGetDeviceProperty(set, ref data, ref key, out type, IntPtr.Zero, 0, out required, 0);
            if (required == 0 || required > 4096 || type != DEVPROP_TYPE_STRING) return "";

            IntPtr buffer = Marshal.AllocHGlobal((int)required);
            try
            {
                if (!SetupDiGetDeviceProperty(set, ref data, ref key, out type, buffer, required, out _, 0))
                    return "";
                string value = Marshal.PtrToStringUni(buffer) ?? "";
                return value.Trim();
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
    }
}

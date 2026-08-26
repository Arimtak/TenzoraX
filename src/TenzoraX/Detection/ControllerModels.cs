using System;
using System.Collections.Generic;

namespace TenzoraX.Detection
{
    /// <summary>Windows-Schnittstelle, über die ein Gerät gefunden wurde.</summary>
    public enum ControllerApi
    {
        XInput,
        WinMM,
        DirectInput,
        Hid,
        RawInput
    }

    public static class ControllerApiNames
    {
        public static string Of(ControllerApi api) => api switch
        {
            ControllerApi.XInput => "XInput",
            ControllerApi.WinMM => "WinMM",
            ControllerApi.DirectInput => "DirectInput",
            ControllerApi.Hid => "HID",
            _ => "Raw Input"
        };
    }

    /// <summary>
    /// Ein von einer Erkennungs-API gemeldetes Gerät (Rohbefund vor dem Zusammenführen).
    /// </summary>
    public class DetectedDeviceInfo
    {
        public ControllerApi Api { get; set; }
        public string Name { get; set; } = "";
        public string Manufacturer { get; set; } = "";
        public ushort Vid { get; set; }
        public ushort Pid { get; set; }
        public string VidPid { get; set; } = "";
        public string Transport { get; set; } = "Unknown";     // USB | Bluetooth | 2.4 GHz | Unknown
        public string GuidInstance { get; set; } = "";
        public string DeviceInstanceId { get; set; } = "";
        public string DevicePath { get; set; } = "";
        public int Axes { get; set; }
        public int Buttons { get; set; }
        public int Povs { get; set; }
        public int SlotIndex { get; set; } = -1;

        // Optional: DirectInput-Details
        public string GuidProduct { get; set; } = "";
        public uint DevTypeRaw { get; set; }
        public ushort UsagePage { get; set; }
        public ushort Usage { get; set; }

        /// <summary>Alle APIs, die dieses (zusammengeführte) Gerät gesehen haben.</summary>
        public List<ControllerApi> SeenApis { get; } = new();

        public override string ToString() => $"{(string.IsNullOrEmpty(Name) ? "?" : Name)} [{ControllerApiNames.Of(Api)}]";
    }

    /// <summary>
    /// Kapselt genau eine Erkennungsmethode (XInput, WinMM, DirectInput, HID, Raw Input).
    /// </summary>
    public interface IControllerProvider
    {
        string ApiName { get; }
        /// <summary>Listet alle aktuell vorhandenen Geräte dieser Schnittstelle auf.</summary>
        List<DetectedDeviceInfo> Enumerate();
    }

    /// <summary>
    /// Reines Zusammenführen von Gerätebefunden mehrerer APIs.
    /// Gleichungsregel: identische Instance-ID ⇒ dasselbe Gerät; gleiche VID/PID wird nur
    /// zusammengeführt, wenn mindestens eine Seite keine Instance-ID hat (sonst wären zwei
    /// physisch vorhandene Exemplare desselben Modells nicht unterscheidbar).
    /// </summary>
    public static class DetectionMerge
    {
        /// <summary>Stabile Kennung eines Rohbefunds für Vergleiche/Tests.</summary>
        public static string StableKey(DetectedDeviceInfo d)
        {
            if (!string.IsNullOrEmpty(d.DeviceInstanceId))
                return "IID:" + d.DeviceInstanceId.ToUpperInvariant();
            if (!string.IsNullOrEmpty(d.VidPid))
                return "VP:" + d.VidPid.ToUpperInvariant();
            return "NAME:" + d.Name.ToUpperInvariant();
        }

        /// <summary>True, wenn beide Befunde sehr wahrscheinlich dasselbe physische Gerät beschreiben.</summary>
        public static bool SamePhysicalDevice(DetectedDeviceInfo a, DetectedDeviceInfo b)
        {
            if (a == null || b == null) return false;
            if (ReferenceEquals(a, b)) return true;

            bool idA = !string.IsNullOrEmpty(a.DeviceInstanceId);
            bool idB = !string.IsNullOrEmpty(b.DeviceInstanceId);
            if (idA && idB)
                return string.Equals(a.DeviceInstanceId, b.DeviceInstanceId, StringComparison.OrdinalIgnoreCase);

            bool vpA = !string.IsNullOrEmpty(a.VidPid);
            bool vpB = !string.IsNullOrEmpty(b.VidPid);
            if (vpA && vpB)
            {
                if (!string.Equals(a.VidPid, b.VidPid, StringComparison.OrdinalIgnoreCase)) return false;
                // Gleiche VID/PID: nur zusammenführen, wenn nicht BEIDE eine unterschiedliche
                // Instance-ID besitzen (dann sind es zwei Exemplare desselben Modells).
                if (idA && idB)
                    return string.Equals(a.DeviceInstanceId, b.DeviceInstanceId, StringComparison.OrdinalIgnoreCase);
                return true;
            }
            return false;
        }

        /// <summary>Führt Quellbefunde in die Zielliste ein; vorhandene Geräte werden angereichert.</summary>
        public static void MergeInto(List<DetectedDeviceInfo> target, IEnumerable<DetectedDeviceInfo> source)
        {
            foreach (var s in source)
            {
                DetectedDeviceInfo? existing = null;
                foreach (var t in target)
                {
                    if (SamePhysicalDevice(t, s)) { existing = t; break; }
                }
                if (existing == null)
                {
                    if (!s.SeenApis.Contains(s.Api)) s.SeenApis.Add(s.Api);
                    target.Add(s);
                }
                else
                {
                    Enrich(existing, s);
                    if (!existing.SeenApis.Contains(s.Api)) existing.SeenApis.Add(s.Api);
                }
            }
        }

        /// <summary>Überträgt fehlende Details vom Quellbefund auf das Zielgerät.</summary>
        public static void Enrich(DetectedDeviceInfo target, DetectedDeviceInfo source)
        {
            if (string.IsNullOrEmpty(target.Name) && !string.IsNullOrEmpty(source.Name)) target.Name = source.Name;
            if (string.IsNullOrEmpty(target.Manufacturer) && !string.IsNullOrEmpty(source.Manufacturer)) target.Manufacturer = source.Manufacturer;
            if (string.IsNullOrEmpty(target.VidPid) && !string.IsNullOrEmpty(source.VidPid))
            {
                target.VidPid = source.VidPid;
                target.Vid = source.Vid;
                target.Pid = source.Pid;
            }
            if (target.Transport == "Unknown" && source.Transport != "Unknown") target.Transport = source.Transport;
            if (string.IsNullOrEmpty(target.GuidInstance) && !string.IsNullOrEmpty(source.GuidInstance)) target.GuidInstance = source.GuidInstance;
            if (string.IsNullOrEmpty(target.DeviceInstanceId) && !string.IsNullOrEmpty(source.DeviceInstanceId)) target.DeviceInstanceId = source.DeviceInstanceId;
            if (string.IsNullOrEmpty(target.DevicePath) && !string.IsNullOrEmpty(source.DevicePath)) target.DevicePath = source.DevicePath;
            if (target.Axes <= 0 && source.Axes > 0) target.Axes = source.Axes;
            if (target.Buttons <= 0 && source.Buttons > 0) target.Buttons = source.Buttons;
            if (target.Povs <= 0 && source.Povs > 0) target.Povs = source.Povs;
        }

        /// <summary>
        /// Leitet die Verbindungsart rein aus einer Parent-/Geräte-Instanz-ID ab (testbar).
        /// Liefert "" wenn die Präfixe keine Aussage zulassen (weitere Baum-Analyse nötig).
        /// </summary>
        public static string TransportFromParentInstance(string instanceId)
        {
            if (string.IsNullOrEmpty(instanceId)) return "";
            string upper = instanceId.ToUpperInvariant();
            if (upper.StartsWith("BTHENUM\\") || upper.StartsWith("BTHLE\\") || upper.StartsWith("BTH\\"))
                return "Bluetooth";
            if (upper.StartsWith("USB\\"))
                return "USB";
            return "";
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace TenzoraX
{
    public class ControllerHistoryEntry
    {
        public string Name { get; set; } = "";
        public string ConnectionType { get; set; } = "Unknown";
        public string VidPid { get; set; } = "";
        public string InstanceId { get; set; } = "";
        public string StableId { get; set; } = "";
        public string FirstSeen { get; set; } = "";
        public string LastSeen { get; set; } = "";
        public int TimesConnected { get; set; }
    }

    /// <summary>
    /// Speichert den Verlauf aller jemals erkannten Controller in
    /// Documents\TenzoraX\controller-history.json
    /// </summary>
    public class ControllerHistoryManager
    {
        private static readonly Lazy<ControllerHistoryManager> _instance = new(() => new ControllerHistoryManager());
        public static ControllerHistoryManager Instance => _instance.Value;

        private readonly object _lock = new();
        private readonly string _filePath;
        private List<ControllerHistoryEntry> _entries = new();

        private ControllerHistoryManager()
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "TenzoraX");
            _filePath = Path.Combine(dir, "controller-history.json");
            Load();
        }

        public List<ControllerHistoryEntry> GetEntries()
        {
            lock (_lock)
            {
                return new List<ControllerHistoryEntry>(_entries);
            }
        }

        /// <summary>
        /// Schlüssel eines Verlaufseintrags: Instanz-ID, sonst VID/PID, sonst Name.
        /// Windows meldet dieselbe Instanz je nach Schnittstelle in anderer
        /// Groß-/Kleinschreibung (HID-Pfad groß, Raw-Input-Pfad klein) – deshalb
        /// wird der Schlüssel für Vergleiche normalisiert (26.10.2026: dadurch
        /// entstanden doppelte Einträge wie „VID_045E&amp;PID_02FF (Unknown)“
        /// neben „Xbox-Controller (USB)“).
        /// </summary>
        public static string StableKey(string instanceId, string vidPid, string name)
        {
            string key = !string.IsNullOrWhiteSpace(instanceId) ? instanceId
                       : !string.IsNullOrWhiteSpace(vidPid) ? vidPid
                       : name ?? "";
            return key.Trim();
        }

        /// <summary>True, wenn der Name nur ein Platzhalter ist (leer oder VID/PID).</summary>
        public static bool IsPlaceholderName(string name, string vidPid)
        {
            if (string.IsNullOrWhiteSpace(name)) return true;
            string n = name.Trim();
            if (n.Equals("Gamepad", StringComparison.OrdinalIgnoreCase)) return true;
            return !string.IsNullOrWhiteSpace(vidPid) && n.Equals(vidPid.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Übernimmt die besseren Werte eines Eintrags in einen anderen (Name/Verbindung).</summary>
        private static void Improve(ControllerHistoryEntry target, ControllerHistoryEntry source)
        {
            if (IsPlaceholderName(target.Name, target.VidPid) && !IsPlaceholderName(source.Name, source.VidPid))
                target.Name = source.Name;
            if (string.Equals(target.ConnectionType, "Unknown", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(source.ConnectionType, "Unknown", StringComparison.OrdinalIgnoreCase))
                target.ConnectionType = source.ConnectionType;
            if (string.IsNullOrWhiteSpace(target.VidPid) && !string.IsNullOrWhiteSpace(source.VidPid))
                target.VidPid = source.VidPid;
            if (string.IsNullOrWhiteSpace(target.InstanceId) && !string.IsNullOrWhiteSpace(source.InstanceId))
                target.InstanceId = source.InstanceId;
        }

        /// <summary>
        /// Fasst Einträge zusammen, die dieselbe Instanz beschreiben (Vergleich ohne
        /// Rücksicht auf Groß-/Kleinschreibung). Zählt Verbindungen zusammen und
        /// behält den frühesten und spätesten Zeitpunkt. Gibt true zurück, wenn
        /// sich die Liste verändert hat.
        /// </summary>
        public static bool FoldDuplicates(List<ControllerHistoryEntry> entries)
        {
            if (entries == null || entries.Count < 2) return false;
            bool changed = false;
            var byKey = new Dictionary<string, ControllerHistoryEntry>(StringComparer.OrdinalIgnoreCase);
            var kept = new List<ControllerHistoryEntry>(entries.Count);

            foreach (var e in entries)
            {
                string key = StableKey(e.InstanceId, e.VidPid, e.Name);
                if (!byKey.TryGetValue(key, out var existing))
                {
                    e.StableId = key;
                    byKey[key] = e;
                    kept.Add(e);
                    continue;
                }

                changed = true;
                Improve(existing, e);
                existing.TimesConnected += e.TimesConnected;
                if (ParseSeen(e.FirstSeen) is DateTime first && ParseSeen(existing.FirstSeen) is DateTime curFirst && first < curFirst)
                    existing.FirstSeen = e.FirstSeen;
                if (ParseSeen(e.LastSeen) is DateTime last && ParseSeen(existing.LastSeen) is DateTime curLast && last > curLast)
                    existing.LastSeen = e.LastSeen;
            }

            if (!changed) return false;
            entries.Clear();
            entries.AddRange(kept);
            return true;
        }

        private static DateTime? ParseSeen(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            return DateTime.TryParse(value, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.RoundtripKind, out var dt) ? dt : null;
        }

        /// <summary>Wird beim Verbinden eines Controllers aufgerufen (auch beim App-Start, wenn bereits verbunden).</summary>
        public void RecordConnect(string name, string connectionType, string vidPid, string instanceId)
        {
            string stable = StableKey(instanceId, vidPid, name);
            if (string.IsNullOrEmpty(stable)) return;
            string now = DateTime.UtcNow.ToString("o");

            lock (_lock)
            {
                var entry = _entries.Find(e =>
                    string.Equals(StableKey(e.InstanceId, e.VidPid, e.Name), stable, StringComparison.OrdinalIgnoreCase));
                if (entry == null)
                {
                    _entries.Add(new ControllerHistoryEntry
                    {
                        Name = name,
                        ConnectionType = connectionType,
                        VidPid = vidPid,
                        InstanceId = instanceId,
                        StableId = stable,
                        FirstSeen = now,
                        LastSeen = now,
                        TimesConnected = 1
                    });
                }
                else
                {
                    // Ein Platzhalter („VID_…", leer) darf einen echten Namen nie
                    // überschreiben, und „Unknown" nie eine bekannte Verbindung.
                    var incoming = new ControllerHistoryEntry
                    {
                        Name = name,
                        ConnectionType = connectionType,
                        VidPid = vidPid,
                        InstanceId = instanceId
                    };
                    Improve(entry, incoming);
                    entry.InstanceId = string.IsNullOrWhiteSpace(entry.InstanceId) ? instanceId : entry.InstanceId;
                    entry.VidPid = string.IsNullOrWhiteSpace(entry.VidPid) ? vidPid : entry.VidPid;
                    entry.LastSeen = now;
                    entry.TimesConnected++;
                }
                Save();
            }
        }

        public void RemoveEntry(string stableId)
        {
            lock (_lock)
            {
                // Wie überall beim Vergleich von Gerätekennungen: Schreibweise ist keine Identität.
                _entries.RemoveAll(e => string.Equals(e.StableId, stableId, StringComparison.OrdinalIgnoreCase));
                Save();
            }
        }

        public void ClearAll()
        {
            lock (_lock)
            {
                _entries.Clear();
                Save();
            }
        }

        private void Load()
        {
            try
            {
                if (File.Exists(_filePath))
                {
                    var loaded = JsonSerializer.Deserialize<List<ControllerHistoryEntry>>(File.ReadAllText(_filePath));
                    if (loaded != null) _entries = loaded;
                    // Altbestand aufräumen: gleiche Instanz in anderer Schreibweise
                    // zusammenfassen (entstand bis 26.10.2026 durch case-sensiblen
                    // Vergleich der Instanz-ID).
                    if (FoldDuplicates(_entries)) Save();
                }
            }
            catch (Exception ex)
            {
                App.LogApp("Controller-Verlauf konnte nicht geladen werden: " + ex.Message);
                _entries = new List<ControllerHistoryEntry>();
            }
        }

        private void Save()
        {
            try
            {
                string dir = Path.GetDirectoryName(_filePath) ?? "";
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(_filePath, JsonSerializer.Serialize(_entries, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception ex)
            {
                App.LogApp("Controller-Verlauf konnte nicht gespeichert werden: " + ex.Message);
            }
        }
    }
}
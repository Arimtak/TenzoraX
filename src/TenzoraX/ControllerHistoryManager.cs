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

        /// <summary>Wird beim Verbinden eines Controllers aufgerufen (auch beim App-Start, wenn bereits verbunden).</summary>
        public void RecordConnect(string name, string connectionType, string vidPid, string instanceId)
        {
            string stable = !string.IsNullOrEmpty(instanceId) ? instanceId : vidPid;
            if (string.IsNullOrEmpty(stable)) stable = name;
            string now = DateTime.UtcNow.ToString("o");

            lock (_lock)
            {
                var entry = _entries.Find(e => e.StableId == stable);
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
                    entry.Name = name;
                    entry.ConnectionType = connectionType;
                    entry.VidPid = vidPid;
                    entry.InstanceId = instanceId;
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
                _entries.RemoveAll(e => e.StableId == stableId);
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
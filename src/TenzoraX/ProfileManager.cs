using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace TenzoraX
{
    public class MappingPart
    {
        public string Text { get; set; } = "";
        public bool IsButton { get; set; }
    }

    public class Mapping
    {
        private static readonly Dictionary<string, string> _symbolMap = new()
        {
            { "DPAD_UP", "↑" },
            { "DPAD_DOWN", "↓" },
            { "DPAD_LEFT", "←" },
            { "DPAD_RIGHT", "→" },
            { "A", "A" },
            { "B", "B" },
            { "X", "X" },
            { "Y", "Y" },
            { "L1", "LB" },
            { "R1", "RB" },
            { "L2", "LT" },
            { "R2", "RT" },
            { "L3", "L3" },
            { "R3", "R3" },
            { "SELECT", "⏵" },
            { "START", "⏭" },
            { "LS_UP", "L-Stick ↑" },
            { "LS_DOWN", "L-Stick ↓" },
            { "LS_LEFT", "L-Stick ←" },
            { "LS_RIGHT", "L-Stick →" },
            { "RS_UP", "R-Stick ↑" },
            { "RS_DOWN", "R-Stick ↓" },
            { "RS_LEFT", "R-Stick ←" },
            { "RS_RIGHT", "R-Stick →" },
        };

        public List<string> Combo { get; set; } = new();
        public List<string> Action { get; set; } = new();

        /// <summary>
        /// Text-Aktion: wenn gesetzt, wird dieser Text (inkl. Emojis) getippt,
        /// statt die Action-Tasten zu senden. Der Text wird einmalig pro
        /// Tastendruck ausgegeben (kein Halten/Loslassen).
        /// </summary>
        public string? Text { get; set; }

        /// <summary>
        /// Optionaler Name/Beschriftung der Kombination (z.B. „Steam Overlay“).
        /// Wird NUR angezeigt (Liste + Benachrichtigung), NIE als Tastatur-Eingabe
        /// ausgegeben – dafür ist die Text-Aktion (Text) da.
        /// </summary>
        public string? Name { get; set; }

        public bool HasTextAction => !string.IsNullOrEmpty(Text);
        public bool HasName => !string.IsNullOrEmpty(Name);

        public string DisplayCombo => string.Join(" + ", Combo.Select(b => _symbolMap.TryGetValue(b, out var s) ? s : b));
        public string DisplayAction => HasTextAction ? Text! : string.Join(" + ", Action);

        /// <summary>
        /// Anzeige-Text der Aktion: Ausgabe-Tasten (bzw. Text-Aktion) und danach der
        /// Name – z.B. „F13 ➔ Steam Overlay“. Ohne Name nur die Ausgabe, ohne Ausgabe
        /// nur der Name.
        /// </summary>
        public string DisplayLabel
        {
            get
            {
                if (HasName)
                {
                    // Ausgabe = Text-Aktion (falls vorhanden) sonst Tasten – exakt
                    // dieselbe Priorität wie beim Auslösen (DisplayAction).
                    string output = DisplayAction;
                    return string.IsNullOrEmpty(output) ? Name! : output + " ➔ " + Name;
                }
                return DisplayAction;
            }
        }

        public List<MappingPart> ComboParts
        {
            get
            {
                var list = new List<MappingPart>();
                for (int i = 0; i < Combo.Count; i++)
                {
                    if (i > 0)
                        list.Add(new MappingPart { Text = "+", IsButton = false });
                    string symbol = _symbolMap.TryGetValue(Combo[i], out var s) ? s : Combo[i];
                    list.Add(new MappingPart { Text = symbol, IsButton = true });
                }
                return list;
            }
        }

        /// <summary>
        /// Ausgabe-Teile (Tasten-Boxen bzw. Text-Aktion) OHNE den Namen – der Name wird
        /// separat als umbrechender Text hinter den Ausgabe-Tasten gerendert (Liste),
        /// damit auch lange Beschriftungen vollständig lesbar bleiben.
        /// </summary>
        public List<MappingPart> ActionParts
        {
            get
            {
                var list = new List<MappingPart>();

                // Ausgabe: Text-Aktion (falls vorhanden – wird beim Auslösen getippt,
                // Tasten werden dann ignoriert), sonst die Ausgabe-Tasten als Boxen.
                if (HasTextAction)
                {
                    list.Add(new MappingPart { Text = Text!, IsButton = false });
                }
                else
                {
                    for (int i = 0; i < Action.Count; i++)
                    {
                        if (i > 0)
                            list.Add(new MappingPart { Text = "+", IsButton = false });
                        list.Add(new MappingPart { Text = Action[i], IsButton = true });
                    }
                }

                return list;
            }
        }

        /// <summary>Name (Beschriftung) oder null – wird hinter den Ausgabe-Tasten gerendert.</summary>
        public string? ActionName => HasName ? Name : null;

        /// <summary>true, wenn eine Ausgabe existiert (Tasten oder Text-Aktion).</summary>
        public bool HasActionOutput => HasTextAction || Action.Count > 0;

        /// <summary>true, wenn zwischen Ausgabe und Name ein ➔ gezeigt werden soll.</summary>
        public bool ShowNameArrow => HasName && HasActionOutput;
    }

    public class MappingProfile
    {
        public string Name { get; set; } = "Default";
        public List<Mapping> Mappings { get; set; } = new();
    }

    public class ProfileManager
    {
        private static ProfileManager? _instance;
        public static ProfileManager Instance => _instance ??= new ProfileManager();

        /// <summary>Pausiert die Mapping-Ausgabe (Tray-Menü / Statusanzeige).</summary>
        private static volatile bool _isPaused;
        public static bool IsPaused
        {
            get => _isPaused;
            private set => _isPaused = value;
        }

        /// <summary>Interner Test-Seam: ersetzt das Profil-Verzeichnis (Unit-Tests).</summary>
        internal static string? ProfileDirOverride { get; set; }

        /// <summary>Interner Test-Seam: fängt KeyDown/KeyUp ab statt SendInput zu feuern (Unit-Tests).</summary>
        internal static Action<List<string>, bool>? KeyOutputOverride { get; set; }

        /// <summary>Interner Test-Seam: fängt Text-Aktionen ab statt SendInput zu feuern (Unit-Tests).</summary>
        internal static Action<string>? TextOutputOverride { get; set; }

        /// <summary>Interner Test-Seam: setzt den Trigger-Zustand zurück (Unit-Tests).</summary>
        internal void ResetStateForTesting()
        {
            lock (_lock)
            {
                _triggeredMappings.Clear();
            }
        }

        private const string ProfileDirName = "Profiles";
        private MappingProfile _activeProfile = new();
        private readonly HashSet<Mapping> _triggeredMappings = new();
        private readonly object _lock = new();

        public MappingProfile ActiveProfile
        {
            get => _activeProfile;
            set => _activeProfile = value;
        }

        private ProfileManager()
        {
            EnsureProfileDirectoryExists();
            LoadDefaultProfile();
        }

        private string ProfileDirPath => ProfileDirOverride ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "TenzoraX", ProfileDirName);

        private void EnsureProfileDirectoryExists()
        {
            if (!Directory.Exists(ProfileDirPath))
            {
                Directory.CreateDirectory(ProfileDirPath);
            }
        }

        private void LoadDefaultProfile()
        {
            lock (_lock)
            {
                // Gehaltene Combos freigeben, bevor das Profil ersetzt wird (sonst
                // bleiben deren Tasten hängen, wenn das neue Profil sie nicht kennt).
                ReleaseAllTriggered();

                string defaultPath = Path.Combine(ProfileDirPath, "Default.json");
                if (File.Exists(defaultPath))
                {
                    _activeProfile = LoadProfile(defaultPath) ?? new MappingProfile { Name = "Default" };
                }
                else
                {
                    _activeProfile = new MappingProfile
                    {
                        Name = "Default",
                        Mappings = new List<Mapping>
                        {
                            new Mapping { Combo = new List<string> { "L1", "SELECT" }, Action = new List<string> { "F13" } },
                            new Mapping { Combo = new List<string> { "START", "SELECT" }, Action = new List<string> { "SHIFT", "TAB" } }
                        }
                    };
                    SaveProfile(_activeProfile);
                }
            }
        }

        public List<string> GetProfileNames()
        {
            EnsureProfileDirectoryExists();
            var files = Directory.GetFiles(ProfileDirPath, "*.json");
            var names = new List<string>();
            foreach (var file in files)
            {
                names.Add(Path.GetFileNameWithoutExtension(file));
            }
            return names;
        }

        public MappingProfile? LoadProfileByName(string name)
        {
            string path = Path.Combine(ProfileDirPath, $"{name}.json");
            if (File.Exists(path))
            {
                var profile = LoadProfile(path);
                if (profile != null)
                {
                    lock (_lock)
                    {
                        // Gehaltene Combos des alten Profils freigeben, bevor das Profil
                        // gewechselt wird – sonst bleibt die Taste hängen, wenn das neue
                        // Profil die Combo nicht mehr kennt. Der Wechsel + Release passiert
                        // atomar zum Poll-Thread, der die Mappings parallel liest.
                        ReleaseAllTriggered();
                        _activeProfile = profile;
                    }
                    return profile;
                }
            }
            return null;
        }

        public void SaveActiveProfile()
        {
            SaveProfile(_activeProfile);
        }

        public void SaveProfile(MappingProfile profile)
        {
            EnsureProfileDirectoryExists();
            string path = Path.Combine(ProfileDirPath, $"{profile.Name}.json");
            string json = JsonSerializer.Serialize(profile, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json);
        }

        public void DeleteProfile(string name)
        {
            string path = Path.Combine(ProfileDirPath, $"{name}.json");
            if (File.Exists(path))
            {
                File.Delete(path);
            }
            if (_activeProfile.Name == name)
            {
                LoadDefaultProfile();
            }
        }

        /// <summary>
        /// Pausiert bzw. setzt die Mapping-Ausgabe fort. Beim Pausieren werden
        /// aktuell gehaltene Kombinationen sauber freigegeben (KeyUp).
        /// </summary>
        public static void SetPaused(bool paused)
        {
            if (IsPaused == paused) return;
            IsPaused = paused;
            if (paused)
                Instance.ReleaseAllTriggered();
        }

        private void ReleaseAllTriggered()
        {
            lock (_lock)
            {
                foreach (var mapping in _triggeredMappings)
                {
                    // Text-Aktionen werden nicht gehalten und brauchen kein KeyUp
                    if (mapping.HasTextAction) continue;
                    if (mapping.Action == null || mapping.Action.Count == 0) continue;
                    try
                    {
                        if (KeyOutputOverride != null)
                            KeyOutputOverride(mapping.Action, true);
                        else
                            InputSimulator.SimulateKeyUp(mapping.Action);
                    }
                    catch (Exception ex)
                    {
                        App.LogApp("Hotkey-Release-Fehler (Pause): " + ex.Message);
                    }
                }
                _triggeredMappings.Clear();
            }
        }

        /// <summary>
        /// Fügt ein Mapping thread-sicher hinzu (kann parallel zum Poll-Thread
        /// aufgerufen werden, der die Mappings liest).
        /// </summary>
        public void AddMapping(Mapping mapping)
        {
            lock (_lock)
            {
                var profile = _activeProfile;
                if (profile?.Mappings == null) return;
                profile.Mappings.Add(mapping);
            }
        }

        /// <summary>
        /// Entfernt ein Mapping thread-sicher (kann parallel zum Poll-Thread
        /// aufgerufen werden, der die Mappings liest).
        /// </summary>
        public void RemoveMapping(Mapping mapping)
        {
            lock (_lock)
            {
                var profile = _activeProfile;
                if (profile?.Mappings == null) return;
                profile.Mappings.Remove(mapping);
            }
        }

        /// <summary>
        /// Verarbeitet die aktuell gedrückten Buttons und löst passende Mappings aus.
        /// Wird vom ControllerManager direkt auf dem Poll-Thread aufgerufen (nicht
        /// über den WPF-Dispatcher), damit UI-Stau die Tastatur-Ausgabe nicht
        /// verzögert. Alle geteilten Zustände sind daher per _lock gesichert.
        /// </summary>
        public void ProcessControllerInput(List<string> pressedButtons)
        {
            if (IsPaused) return;
            var profile = _activeProfile;
            if (profile == null || profile.Mappings == null) return;

            lock (_lock)
            {
                // Re-Check unter dem Lock: SetPaused könnte parallel reingefahren sein
                if (IsPaused) return;

                foreach (var mapping in profile.Mappings)
                {
                    if (mapping.Combo == null || mapping.Combo.Count == 0) continue;

                    // Check if ALL buttons in the combo are currently pressed
                    bool allPressed = true;
                    foreach (var button in mapping.Combo)
                    {
                        if (!pressedButtons.Contains(button))
                        {
                            allPressed = false;
                            break;
                        }
                    }

                    if (allPressed)
                    {
                        if (!_triggeredMappings.Contains(mapping))
                        {
                            _triggeredMappings.Add(mapping);
                            string combo = mapping.DisplayCombo;
                            string label = mapping.DisplayLabel;
                            bool isText = mapping.HasTextAction;
                            bool hasName = mapping.HasName;
                            // Trigger – synchron auf dem Poll-Thread. Task.Run
                            // würde die Reihenfolge KeyDown→KeyUp unter ThreadPool-Last
                            // nicht garantieren (KeyUp könnte vor KeyDown ausgeführt
                            // werden → Taste bleibt hängen).
                            try
                            {
                                if (isText)
                                {
                                    // Text-Aktion: einmalig tippen (kein Halten/Loslassen)
                                    if (TextOutputOverride != null)
                                        TextOutputOverride(mapping.Text!);
                                    else
                                        InputSimulator.SimulateText(mapping.Text!);
                                    SoundManager.PlayConfirmation();
                                }
                                else if (KeyOutputOverride != null)
                                {
                                    KeyOutputOverride(mapping.Action, false);
                                }
                                else
                                {
                                    InputSimulator.SimulateKeyDown(mapping.Action);
                                    SoundManager.PlayConfirmation();
                                }
                            }
                            catch (Exception ex)
                            {
                                App.LogApp("Hotkey-Ausgabe-Fehler: " + ex.Message);
                            }
                            // Latenz messen: StateChanged → SendInput fertig (Poll-Thread)
                            LatencyMonitor.RecordOutputDone();
                            // Benachrichtigung: bei benannten Kombinationen Combo + Name
                            // zeigen („LB + ↑ ➔ Steam Overlay“). Bei Text-Aktionen ohne
                            // Namen nur den ausgelösten Text zeigen, sonst wie bisher.
                            try
                            {
                                var app = System.Windows.Application.Current;
                                if (app != null)
                                {
                                    // BeginInvoke statt Invoke: nicht den Poll-Thread blockieren
                                    app.Dispatcher.BeginInvoke(() =>
                                        NotificationManager.Show((isText && !hasName) ? "" : combo, label));
                                }
                            }
                            catch (Exception ex)
                            {
                                App.LogApp("Notification-Fehler: " + ex.Message);
                            }
                        }
                    }
                    else
                    {
                        if (_triggeredMappings.Contains(mapping))
                        {
                            _triggeredMappings.Remove(mapping);
                            // Text-Aktionen werden nicht gehalten – kein KeyUp nötig
                            if (mapping.HasTextAction) continue;
                            // Trigger key up – synchron, siehe KeyDown
                            try
                            {
                                if (KeyOutputOverride != null)
                                    KeyOutputOverride(mapping.Action, true);
                                else
                                    InputSimulator.SimulateKeyUp(mapping.Action);
                            }
                            catch (Exception ex)
                            {
                                App.LogApp("Hotkey-Release-Fehler: " + ex.Message);
                            }
                        }
                    }
                }
            }
        }

        private MappingProfile? LoadProfile(string path)
        {
            try
            {
                string json = File.ReadAllText(path);
                return JsonSerializer.Deserialize<MappingProfile>(json);
            }
            catch
            {
                return null;
            }
        }
    }
}

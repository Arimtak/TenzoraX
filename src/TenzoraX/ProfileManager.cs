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

        public string DisplayCombo => string.Join(" + ", Combo.Select(b => _symbolMap.TryGetValue(b, out var s) ? s : b));
        public string DisplayAction => string.Join(" + ", Action);

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

        public List<MappingPart> ActionParts
        {
            get
            {
                var list = new List<MappingPart>();
                for (int i = 0; i < Action.Count; i++)
                {
                    if (i > 0)
                        list.Add(new MappingPart { Text = "+", IsButton = false });
                    list.Add(new MappingPart { Text = Action[i], IsButton = true });
                }
                return list;
            }
        }
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

        private const string ProfileDirName = "Profiles";
        private MappingProfile _activeProfile = new();
        private readonly HashSet<Mapping> _triggeredMappings = new();

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

        private string ProfileDirPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "TenzoraX", ProfileDirName);

        private void EnsureProfileDirectoryExists()
        {
            if (!Directory.Exists(ProfileDirPath))
            {
                Directory.CreateDirectory(ProfileDirPath);
            }
        }

        private void LoadDefaultProfile()
        {
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
                    _activeProfile = profile;
                    _triggeredMappings.Clear();
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
        /// Process the list of currently pressed buttons and trigger mapped keyboard actions
        /// </summary>
        public void ProcessControllerInput(List<string> pressedButtons)
        {
            // isPaused wird ignoriert - gespeicherte Kombinationen funktionieren immer!
            if (_activeProfile == null || _activeProfile.Mappings == null) return;

            foreach (var mapping in _activeProfile.Mappings)
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
                        string action = mapping.DisplayAction;
                        // Trigger key down in background thread
                        Task.Run(() =>
                        {
                            try
                            {
                                InputSimulator.SimulateKeyDown(mapping.Action);
                                SoundManager.PlayConfirmation();
                            }
                            catch (Exception ex)
                            {
                                App.LogApp("Hotkey-Ausgabe-Fehler: " + ex.Message);
                            }
                        });
                        // Show notification on UI thread
                        try
                        {
                            var app = System.Windows.Application.Current;
                            if (app != null)
                            {
                                app.Dispatcher.Invoke(() =>
                                    NotificationManager.Show(combo, action));
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
                        // Trigger key up in background thread
                        Task.Run(() =>
                        {
                            try
                            {
                                InputSimulator.SimulateKeyUp(mapping.Action);
                            }
                            catch (Exception ex)
                            {
                                App.LogApp("Hotkey-Release-Fehler: " + ex.Message);
                            }
                        });
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

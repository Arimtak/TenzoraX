using System.ComponentModel;
using System.Reflection;
using System.Windows;

namespace TenzoraX
{
    public class LanguageManager : INotifyPropertyChanged
    {
        private string _currentLang = "en";
        private readonly Dictionary<string, Dictionary<string, string>> _data = new();

        public static LanguageManager Instance { get; } = new();

        public event PropertyChangedEventHandler? PropertyChanged;

        public string CurrentLang => _currentLang;

        private LanguageManager()
        {
            Populate();
        }

        public string this[string key] =>
            _data.TryGetValue(_currentLang, out var dict) && dict.TryGetValue(key, out var val)
                ? val
                : _data.GetValueOrDefault("en", new())?.GetValueOrDefault(key, key) ?? key;

        public bool HasLanguage(string lang) => _data.ContainsKey(lang);

        public void Switch(string lang)
        {
            if (_data.ContainsKey(lang) && _currentLang != lang)
            {
                _currentLang = lang;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(""));
            }
        }

        public string Format(string key, params object[] args) =>
            string.Format(this[key], args);

        #region Properties for XAML binding

        public string Title_Window => this["Title_Window"];
        public string Status_Label => this["Status_Label"];
        public string Status_Connected => this["Status_Connected"];
        public string Status_Disconnected => this["Status_Disconnected"];
        public string Status_Paused => this["Status_Paused"];
        public string Battery_Label => this["Battery_Label"];
        public string Battery_NA => this["Battery_NA"];
        public string Combo_NoController => this["Combo_NoController"];

        public string KB_Heading => this["KB_Heading"];
        public string KB_FnKeys => this["KB_FnKeys"];
        public string KB_GamingShortcuts => this["KB_GamingShortcuts"];
        public string KB_NumberRow => this["KB_NumberRow"];
        public string KB_Letters => this["KB_Letters"];
        public string KB_Modifiers => this["KB_Modifiers"];
        public string KB_NumPad => this["KB_NumPad"];

        public string Profile_Title => this["Profile_Title"];
        public string Profile_New => this["Profile_New"];
        public string Profile_Delete => this["Profile_Delete"];
        public string Mapping_Title => this["Mapping_Title"];
        public string Mapping_On => this["Mapping_On"];
        public string Mapping_Off => this["Mapping_Off"];
        public string Mapping_ControllerLabel => this["Mapping_ControllerLabel"];
        public string Mapping_ComboPlaceholder => this["Mapping_ComboPlaceholder"];
        public string Mapping_ActionLabel => this["Mapping_ActionLabel"];
        public string Mapping_ActionPlaceholder => this["Mapping_ActionPlaceholder"];
        public string Mapping_Save => this["Mapping_Save"];
        public string Mapping_ListTitle => this["Mapping_ListTitle"];
        public string Mapping_Record => this["Mapping_Record"];
        public string Mapping_StopRecord => this["Mapping_StopRecord"];

        public string Edit_ModeEnabled => this["Edit_ModeEnabled"];
        public string Edit_ImageMode => this["Edit_ImageMode"];
        public string Edit_ButtonsMode => this["Edit_ButtonsMode"];
        public string Edit_ChangeImage => this["Edit_ChangeImage"];
        public string Edit_ResetImage => this["Edit_ResetImage"];
        public string Edit_Tip => this["Edit_Tip"];
        public string Edit_Pause => this["Edit_Pause"];
        public string Edit_Autostart => this["Edit_Autostart"];
        public string Edit_StartMinimized => this["Edit_StartMinimized"];
        public string Edit_MinimizeToTray => this["Edit_MinimizeToTray"];
        public string Edit_ResetPositions => this["Edit_ResetPositions"];
        public string Edit_AdminMode => this["Edit_AdminMode"];
        public string Edit_AdminUser => this["Edit_AdminUser"];
        public string Edit_AdminElevated => this["Edit_AdminElevated"];

        public string Battery_Title => this["Battery_Title"];
        public string Battery_Enable => this["Battery_Enable"];
        public string Battery_HoursLabel => this["Battery_HoursLabel"];
        public string Battery_HoursUnit => this["Battery_HoursUnit"];
        public string Battery_TrayEnable => this["Battery_TrayEnable"];
        public string Battery_Animation => this["Battery_Animation"];
        public string Battery_Reset => this["Battery_Reset"];
        public string Battery_Calculated => this["Battery_Calculated"];

        public string Tray_Open => this["Tray_Open"];
        public string Tray_SwitchProfile => this["Tray_SwitchProfile"];
        public string Tray_Pause => this["Tray_Pause"];
        public string Tray_Exit => this["Tray_Exit"];
        public string Tray_BalloonText => this["Tray_BalloonText"];

        public string Dialog_NewProfileTitle => this["Dialog_NewProfileTitle"];
        public string Dialog_NewProfilePrompt => this["Dialog_NewProfilePrompt"];
        public string Dialog_NewProfileDefault => this["Dialog_NewProfileDefault"];
        public string Dialog_DeleteProfileTitle => this["Dialog_DeleteProfileTitle"];
        public string Dialog_DeleteProfileMsg => this["Dialog_DeleteProfileMsg"];
        public string Dialog_MappingIncompleteTitle => this["Dialog_MappingIncompleteTitle"];
        public string Dialog_MappingIncompleteMsg => this["Dialog_MappingIncompleteMsg"];
        public string Dialog_AutostartErrorTitle => this["Dialog_AutostartErrorTitle"];
        public string Dialog_AutostartErrorMsg => this["Dialog_AutostartErrorMsg"];
        public string Dialog_ResetImageTitle => this["Dialog_ResetImageTitle"];
        public string Dialog_ResetImageMsg => this["Dialog_ResetImageMsg"];
        public string Dialog_RestartAsAdminTitle => this["Dialog_RestartAsAdminTitle"];
        public string Dialog_RestartAsAdminMsg => this["Dialog_RestartAsAdminMsg"];

        public string ImageFilter => this["ImageFilter"];
        public string ImagePickerTitle => this["ImagePickerTitle"];

        public string Btn_TestOutput => this["Btn_TestOutput"];

        public string Sound_Enable => this["Sound_Enable"];
        public string Sound_VolumeLabel => this["Sound_VolumeLabel"];

        public string OutputMode_Label => this["OutputMode_Label"];
        public string OutputMode_VK => this["OutputMode_VK"];
        public string OutputMode_ScanCode => this["OutputMode_ScanCode"];

        #endregion

        private void Populate()
        {
            var en = new Dictionary<string, string>
            {
                ["Title_Window"] = "TenzoraX - Modern Controller Mapper",

                ["Status_Label"] = "Status:",
                ["Status_Connected"] = "Connected",
                ["Status_Disconnected"] = "Disconnected",
                ["Status_Paused"] = "Paused",
                ["Battery_Label"] = "Battery:",
                ["Battery_NA"] = "N/A",
                ["Combo_NoController"] = "No controller detected",

                ["KB_Heading"] = "Keyboard (click a key to assign)",
                ["KB_FnKeys"] = "Function keys F1-F12",
                ["KB_GamingShortcuts"] = "GAMING SHORTCUTS F13-F24 (click to assign)",
                ["KB_NumberRow"] = "Number row",
                ["KB_Letters"] = "Letters",
                ["KB_Modifiers"] = "Modifiers & Mouse",
                ["KB_NumPad"] = "Numpad",

                ["Profile_Title"] = "Profile Management",
                ["Profile_New"] = "New",
                ["Profile_Delete"] = "Del",
                ["Mapping_Title"] = "New Mapping",
                ["Mapping_On"] = "On",
                ["Mapping_Off"] = "Off",
                ["Mapping_ControllerLabel"] = "Controller buttons (capture):",
                ["Mapping_ComboPlaceholder"] = "(empty) e.g. L1 + SELECT",
                ["Mapping_ActionLabel"] = "Output keys:",
                ["Mapping_ActionPlaceholder"] = "(empty) e.g. F13",
                ["Mapping_Save"] = "Save combination",
                ["Mapping_ListTitle"] = "Saved Combinations",
                ["Mapping_Record"] = "Record",
                ["Mapping_StopRecord"] = "Stop",

                ["Edit_ModeEnabled"] = "Edit mode",
                ["Edit_ImageMode"] = "Image (move/scale)",
                ["Edit_ButtonsMode"] = "Buttons (select/drag)",
                ["Edit_ChangeImage"] = "Change image",
                ["Edit_ResetImage"] = "Reset",
                ["Edit_Tip"] = "Tip: Drag the image with the mouse and scale with the scroll wheel.",
                ["Edit_Pause"] = "Pause mapping",
                ["Edit_Autostart"] = "Start with Windows",
                ["Edit_StartMinimized"] = "Start minimized",
                ["Edit_MinimizeToTray"] = "Minimize (not close)",
                ["Edit_ResetPositions"] = "Reset button positions",
                ["Edit_AdminMode"] = "Run as administrator",
                ["Edit_AdminUser"] = "● User",
                ["Edit_AdminElevated"] = "● Administrator",

                ["Battery_Title"] = "Battery",
                ["Battery_Enable"] = "Battery system",
                ["Battery_HoursLabel"] = "Estimated battery life",
                ["Battery_HoursUnit"] = "Hours",
                ["Battery_TrayEnable"] = "Battery in tray",
                ["Battery_Animation"] = "Animation",
                ["Battery_Reset"] = "Reset battery to 100%",
                ["Battery_Calculated"] = "Calculated: {0} min ({1:F1}%/min)",

                ["Tray_Open"] = "Open TenzoraX",
                ["Tray_SwitchProfile"] = "Switch profile",
                ["Tray_Pause"] = "Pause",
                ["Tray_Exit"] = "Exit",
                ["Tray_BalloonText"] = "The program is running in the system tray.",

                ["Dialog_NewProfileTitle"] = "Create new profile",
                ["Dialog_NewProfilePrompt"] = "Enter the name of the new profile:",
                ["Dialog_NewProfileDefault"] = "Gaming",
                ["Dialog_DeleteProfileTitle"] = "Delete profile",
                ["Dialog_DeleteProfileMsg"] = "Are you sure you want to delete the profile '{0}'?",
                ["Dialog_MappingIncompleteTitle"] = "Incomplete mapping",
                ["Dialog_MappingIncompleteMsg"] = "Please select at least one controller button and one keyboard key.",
                ["Dialog_AutostartErrorTitle"] = "Error",
                ["Dialog_AutostartErrorMsg"] = "Could not configure autostart: {0}",
                ["Dialog_ResetImageTitle"] = "Reset image",
                ["Dialog_ResetImageMsg"] = "Are you sure you want to reset the controller image and its position/size?",
                ["Dialog_RestartAsAdminTitle"] = "Restart as administrator",
                ["Dialog_RestartAsAdminMsg"] = "TenzoraX must be restarted as administrator for this feature. Do you want to restart now?",

                ["ImageFilter"] = "Image files (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp|All files (*.*)|*.*",
                ["ImagePickerTitle"] = "Select controller image",

                // Button labels that are also displayed as content
                ["Btn_ESC"] = "ESC",
                ["Btn_TAB"] = "TAB",
                ["Btn_ENTER"] = "ENTER",
                ["Btn_SHIFT"] = "SHIFT",
                ["Btn_CTRL"] = "CTRL",
                ["Btn_ALT"] = "ALT",
                ["Btn_SPACE"] = "SPACE",
                ["Btn_WIN"] = "WIN",
                ["Btn_MouseL"] = "Mouse L",
                ["Btn_MouseR"] = "Mouse R",
                ["Btn_MouseM"] = "Mouse M",
                ["Btn_NumLock"] = "NumLock",
                ["Btn_NumDiv"] = "Num /",
                ["Btn_NumMul"] = "Num *",
                ["Btn_NumMinus"] = "Num -",
                ["Btn_NumPlus"] = "Num +",
                ["Btn_NumDot"] = "Num .",
                ["Btn_Num0"] = "Num 0",
                ["Btn_TestOutput"] = "Test output",
                ["Sound_Enable"] = "Sound feedback on output",
                ["Sound_VolumeLabel"] = "Volume:",
                ["OutputMode_Label"] = "Output mode:",
                ["OutputMode_VK"] = "Normal (VK)",
                ["OutputMode_ScanCode"] = "Compatibility (Scan code)",
            };

            var de = new Dictionary<string, string>
            {
                ["Title_Window"] = "TenzoraX - Moderner Controller-Mapper",

                ["Status_Label"] = "Status:",
                ["Status_Connected"] = "Verbunden",
                ["Status_Disconnected"] = "Getrennt",
                ["Status_Paused"] = "Pausiert",
                ["Battery_Label"] = "Batterie:",
                ["Battery_NA"] = "N/A",
                ["Combo_NoController"] = "Kein Controller erkannt",

                ["KB_Heading"] = "Tastatur (Taste anklicken zum Zuweisen)",
                ["KB_FnKeys"] = "Funktionstasten F1-F12",
                ["KB_GamingShortcuts"] = "GAMING-SHORTCUTS F13-F24 (anklicken zum Zuweisen)",
                ["KB_NumberRow"] = "Zahlenreihe",
                ["KB_Letters"] = "Buchstaben",
                ["KB_Modifiers"] = "Modifikatoren und Maus",
                ["KB_NumPad"] = "Nummernblock",

                ["Profile_Title"] = "Profilverwaltung",
                ["Profile_New"] = "Neu",
                ["Profile_Delete"] = "Lösch",
                ["Mapping_Title"] = "Neues Mapping",
                ["Mapping_On"] = "An",
                ["Mapping_Off"] = "Aus",
                ["Mapping_ControllerLabel"] = "Controller-Tasten (Aufnahme):",
                ["Mapping_ComboPlaceholder"] = "(leer) z.B. L1 + SELECT",
                ["Mapping_ActionLabel"] = "Ausgabe-Tasten:",
                ["Mapping_ActionPlaceholder"] = "(leer) z.B. F13",
                ["Mapping_Save"] = "Kombination speichern",
                ["Mapping_ListTitle"] = "Gespeicherte Kombinationen",
                ["Mapping_Record"] = "Aufnehmen",
                ["Mapping_StopRecord"] = "Stop",

                ["Edit_ModeEnabled"] = "Bearbeitungsmodus",
                ["Edit_ImageMode"] = "Bild (verschieben/skalieren)",
                ["Edit_ButtonsMode"] = "Tasten (auswählen/ziehen)",
                ["Edit_ChangeImage"] = "Bild ändern",
                ["Edit_ResetImage"] = "Zurücksetzen",
                ["Edit_Tip"] = "Tipp: Verschiebe das Bild mit der Maus und skaliere es mit dem Mausrad.",
                ["Edit_Pause"] = "Mapping pausieren",
                ["Edit_Autostart"] = "Mit Windows starten",
                ["Edit_StartMinimized"] = "Minimiert starten",
                ["Edit_MinimizeToTray"] = "Minimieren (nicht schließen)",
                ["Edit_ResetPositions"] = "Tastenpositionen zurücksetzen",
                ["Edit_AdminMode"] = "Als Administrator ausführen",
                ["Edit_AdminUser"] = "● Benutzer",
                ["Edit_AdminElevated"] = "● Administrator",

                ["Battery_Title"] = "Batterie",
                ["Battery_Enable"] = "Batterie-System",
                ["Battery_HoursLabel"] = "Geschätzte Akkulaufzeit",
                ["Battery_HoursUnit"] = "Stunden",
                ["Battery_TrayEnable"] = "Batterie im Tray",
                ["Battery_Animation"] = "Animation",
                ["Battery_Reset"] = "Batterie auf 100% zurücksetzen",
                ["Battery_Calculated"] = "Berechnet: {0} Min ({1:F1}%/Min)",

                ["Tray_Open"] = "TenzoraX öffnen",
                ["Tray_SwitchProfile"] = "Profil wechseln",
                ["Tray_Pause"] = "Pause",
                ["Tray_Exit"] = "Beenden",
                ["Tray_BalloonText"] = "Das Programm läuft im Hintergrund im System-Tray.",

                ["Dialog_NewProfileTitle"] = "Neues Profil erstellen",
                ["Dialog_NewProfilePrompt"] = "Geben Sie den Namen des neuen Profils ein:",
                ["Dialog_NewProfileDefault"] = "Gaming",
                ["Dialog_DeleteProfileTitle"] = "Profil löschen",
                ["Dialog_DeleteProfileMsg"] = "Möchten Sie das Profil '{0}' wirklich löschen?",
                ["Dialog_MappingIncompleteTitle"] = "Mapping unvollständig",
                ["Dialog_MappingIncompleteMsg"] = "Bitte wählen Sie mindestens eine Controller-Taste und eine Tastatur-Taste aus.",
                ["Dialog_AutostartErrorTitle"] = "Fehler",
                ["Dialog_AutostartErrorMsg"] = "Autostart konnte nicht konfiguriert werden: {0}",
                ["Dialog_ResetImageTitle"] = "Bild zurücksetzen",
                ["Dialog_ResetImageMsg"] = "Möchten Sie das Controller-Bild und seine Position/Größe wirklich zurücksetzen?",
                ["Dialog_RestartAsAdminTitle"] = "Als Administrator neu starten",
                ["Dialog_RestartAsAdminMsg"] = "TenzoraX muss als Administrator neu gestartet werden. Jetzt neu starten?",

                ["ImageFilter"] = "Bilddateien (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp|Alle Dateien (*.*)|*.*",
                ["ImagePickerTitle"] = "Controller-Bild auswählen",

                // Button labels
                ["Btn_ESC"] = "ESC",
                ["Btn_TAB"] = "TAB",
                ["Btn_ENTER"] = "ENTER",
                ["Btn_SHIFT"] = "SHIFT",
                ["Btn_CTRL"] = "CTRL",
                ["Btn_ALT"] = "ALT",
                ["Btn_SPACE"] = "SPACE",
                ["Btn_WIN"] = "WIN",
                ["Btn_MouseL"] = "Maus L",
                ["Btn_MouseR"] = "Maus R",
                ["Btn_MouseM"] = "Maus M",
                ["Btn_NumLock"] = "NumLock",
                ["Btn_NumDiv"] = "Num /",
                ["Btn_NumMul"] = "Num *",
                ["Btn_NumMinus"] = "Num -",
                ["Btn_NumPlus"] = "Num +",
                ["Btn_NumDot"] = "Num .",
                ["Btn_Num0"] = "Num 0",
                ["Btn_TestOutput"] = "Testausgabe",
                ["Sound_Enable"] = "Sound-Feedback bei Ausgabe",
                ["Sound_VolumeLabel"] = "Lautstärke:",
                ["OutputMode_Label"] = "Ausgabemodus:",
                ["OutputMode_VK"] = "Normal (VK)",
                ["OutputMode_ScanCode"] = "Kompatibilität (Scan code)",
            };

            _data["en"] = en;
            _data["de"] = de;
        }

        public static void Init(Window mainWindow)
        {
            var lang = Instance;
            mainWindow.Resources["Lang"] = lang;
        }
    }
}

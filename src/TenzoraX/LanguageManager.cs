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
        public string Combo_NoController => this["Combo_NoController"];
        public string Status_AccessError => this["Status_AccessError"];
        public string Input_Label => this["Input_Label"];
        public string Input_Active => this["Input_Active"];
        public string Input_Inactive => this["Input_Inactive"];
        public string History_Button => this["History_Button"];
        public string Gamepad_Collapse => this["Gamepad_Collapse"];
        public string Gamepad_Expand => this["Gamepad_Expand"];
        public string History_Title => this["History_Title"];
        public string History_Empty => this["History_Empty"];
        public string History_LastUsed => this["History_LastUsed"];
        public string History_Used => this["History_Used"];

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
        public string Mapping_NameLabel => this["Mapping_NameLabel"];
        public string Mapping_NameHint => this["Mapping_NameHint"];
        public string Mapping_ToggleLabel => this["Mapping_ToggleLabel"];
        public string Mapping_ToggleHint => this["Mapping_ToggleHint"];
        public string Mapping_ToggleBadge => this["Mapping_ToggleBadge"];
        public string Mapping_TextActionLabel => this["Mapping_TextActionLabel"];
        public string Mapping_TextActionApply => this["Mapping_TextActionApply"];
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

        public string Design_Section => this["Design_Section"];
        public string Design_Label => this["Design_Label"];
        public string Design_Flat => this["Design_Flat"];
        public string Design_3D => this["Design_3D"];
        public string Design_Shine => this["Design_Shine"];
        public string Design_Hint => this["Design_Hint"];

        public string Background_Section => this["Background_Section"];
        public string Background_EnableTitle => this["Background_EnableTitle"];
        public string Background_EnableHint => this["Background_EnableHint"];
        public string Background_QualityLabel => this["Background_QualityLabel"];
        public string Background_QualityAuto => this["Background_QualityAuto"];
        public string Background_QualityFull => this["Background_QualityFull"];
        public string Background_QualityReduced => this["Background_QualityReduced"];
        public string Background_QualityMinimal => this["Background_QualityMinimal"];
        public string Background_QualityHint => this["Background_QualityHint"];

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
        public string Btn_MouseL => this["Btn_MouseL"];
        public string Btn_MouseR => this["Btn_MouseR"];
        public string Btn_MouseM => this["Btn_MouseM"];

        public string Diag_Button => this["Diag_Button"];
        public string Diag_Title => this["Diag_Title"];
        public string Diag_Endpoints => this["Diag_Endpoints"];

        public string Sound_Enable => this["Sound_Enable"];
        public string Sound_VolumeLabel => this["Sound_VolumeLabel"];

        public string OutputMode_Label => this["OutputMode_Label"];
        public string OutputMode_VK => this["OutputMode_VK"];
        public string OutputMode_ScanCode => this["OutputMode_ScanCode"];

        public string Settings_Title => this["Settings_Title"];
        public string Settings_GeneralSection => this["Settings_GeneralSection"];
        public string Settings_UpdateCheck => this["Settings_UpdateCheck"];
        public string Settings_Close => this["Settings_Close"];
        public string Settings_LanguageLabel => this["Settings_LanguageLabel"];
        public string Settings_ReselectLabel => this["Settings_ReselectLabel"];
        public string Reselect_First => this["Reselect_First"];
        public string Reselect_Last => this["Reselect_Last"];
        public string Settings_Version => this["Settings_Version"];
        public string Settings_UpdateCurrent => this["Settings_UpdateCurrent"];
        public string Settings_RestartFailed => this["Settings_RestartFailed"];
        public string Settings_AutostartHint => this["Settings_AutostartHint"];
        public string Settings_StartMinimizedHint => this["Settings_StartMinimizedHint"];
        public string Settings_MinimizeToTrayHint => this["Settings_MinimizeToTrayHint"];
        public string Settings_AdminHint => this["Settings_AdminHint"];

        public string Notify_Section => this["Notify_Section"];
        public string Notify_EnableTitle => this["Notify_EnableTitle"];
        public string Notify_EnableHint => this["Notify_EnableHint"];
        public string Notify_Duration => this["Notify_Duration"];
        public string Notify_DurationHint => this["Notify_DurationHint"];

        public string Vigem_Section => this["Vigem_Section"];
        public string Vigem_EnableTitle => this["Vigem_EnableTitle"];
        public string Vigem_EnableHint => this["Vigem_EnableHint"];
        public string Vigem_TreiberHint => this["Vigem_TreiberHint"];
        public string Vigem_NoDriverTitle => this["Vigem_NoDriverTitle"];
        public string Vigem_NoDriverMsg => this["Vigem_NoDriverMsg"];
        public string Vigem_InstallPrompt => this["Vigem_InstallPrompt"];
        public string Vigem_InstallLocalPrompt => this["Vigem_InstallLocalPrompt"];

        public string Hidhide_Title => this["Hidhide_Title"];
        public string Hidhide_Intro => this["Hidhide_Intro"];
        public string Hidhide_Installed => this["Hidhide_Installed"];
        public string Hidhide_Missing => this["Hidhide_Missing"];
        public string Hidhide_Install => this["Hidhide_Install"];
        public string Hidhide_OpenClient => this["Hidhide_OpenClient"];
        public string Hidhide_MissingTitle => this["Hidhide_MissingTitle"];
        public string Hidhide_LaunchError => this["Hidhide_LaunchError"];
        public string Hidhide_AutoActive => this["Hidhide_AutoActive"];
        public string Hidhide_AutoNeedsAdmin => this["Hidhide_AutoNeedsAdmin"];
        public string Hidhide_AutoFailed => this["Hidhide_AutoFailed"];
        public string Admin_StartupTitle => this["Admin_StartupTitle"];
        public string Admin_StartupHint => this["Admin_StartupHint"];
        public string Admin_ToggleTitle => this["Admin_ToggleTitle"];
        public string Admin_TogglePrompt => this["Admin_TogglePrompt"];
        public string Hidhide_StepHeader => this["Hidhide_StepHeader"];
        public string Hidhide_Step1 => this["Hidhide_Step1"];
        public string Hidhide_Step2 => this["Hidhide_Step2"];
        public string Hidhide_Step3 => this["Hidhide_Step3"];
        public string Hidhide_Note => this["Hidhide_Note"];

        public string InProgress_Badge => this["InProgress_Badge"];
        public string Vigem_OpenControllers => this["Vigem_OpenControllers"];
        public string Vigem_OpenControllersHint => this["Vigem_OpenControllersHint"];
        public string GamepadPanel_FailedTitle => this["GamepadPanel_FailedTitle"];
        public string GamepadPanel_FailedMsg => this["GamepadPanel_FailedMsg"];
        public string Main_InProgressHint => this["Main_InProgressHint"];
        public string Tabs_Combinations => this["Tabs_Combinations"];
        public string Tabs_Controller => this["Tabs_Controller"];
        public string Drivers_CheckButton => this["Drivers_CheckButton"];
        public string Drivers_CheckTitle => this["Drivers_CheckTitle"];
        public string Drivers_CheckResult => this["Drivers_CheckResult"];
        public string Drivers_Installed => this["Drivers_Installed"];
        public string Drivers_Missing => this["Drivers_Missing"];
        public string Drivers_AdminAvailable => this["Drivers_AdminAvailable"];
        public string Drivers_AdminMissing => this["Drivers_AdminMissing"];
        public string Drivers_HeaderTooltip => this["Drivers_HeaderTooltip"];
        public string Drivers_HeaderTooltipOk => this["Drivers_HeaderTooltipOk"];
        public string Drivers_HeaderTooltipMissing => this["Drivers_HeaderTooltipMissing"];
        public string Drivers_HeaderAllInstalled => this["Drivers_HeaderAllInstalled"];
        public string Drivers_HeaderDownloadFailed => this["Drivers_HeaderDownloadFailed"];

        public string Sound_Section => this["Sound_Section"];
        public string Sound_EnableTitle => this["Sound_EnableTitle"];
        public string Sound_EnableHint => this["Sound_EnableHint"];
        public string Sound_VolumeHint => this["Sound_VolumeHint"];

        public string Update_NewAvailable => this["Update_NewAvailable"];
        public string Update_Installed => this["Update_Installed"];
        public string Update_New => this["Update_New"];
        public string Update_Later => this["Update_Later"];
        public string Update_Now => this["Update_Now"];
        public string Update_Loading => this["Update_Loading"];
        public string Update_Downloading => this["Update_Downloading"];
        public string Update_Installing => this["Update_Installing"];
        public string Update_Failed => this["Update_Failed"];
        public string Update_Retry => this["Update_Retry"];

        public string Tray_Resume => this["Tray_Resume"];

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
                ["Combo_NoController"] = "No controller detected",
                ["Status_AccessError"] = "Detected, but access not possible",
                ["Input_Label"] = "Input:",
                ["Input_Active"] = "Active",
                ["Input_Inactive"] = "No activity",
                ["History_Button"] = "Controller history",
                ["Gamepad_Collapse"] = "Hide controller field (enlarge keyboard)",
                ["Gamepad_Expand"] = "Show controller field",
                ["History_Title"] = "Controller History",
                ["History_Empty"] = "No history yet.",
                ["History_LastUsed"] = "Last used: {0}",
                ["History_Used"] = "Used: {0}x",
                ["History_DeleteConfirm"] = "Delete this entry?",
                ["History_ClearAllConfirm"] = "Delete entire history?",

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
                ["Mapping_NameLabel"] = "Name (label):",
                ["Mapping_NameHint"] = "Shown in the list and notification – never typed. Use the text action below if the combo should actually type text.",
                ["Mapping_ToggleLabel"] = "Toggle (hold key permanently)",
                ["Mapping_ToggleHint"] = "First press holds the output keys down, the second press releases them. Applies to key output – text actions are still typed once per press.",
                ["Mapping_ToggleBadge"] = "Toggle",
                ["Mapping_TextActionLabel"] = "Text action (type emojis/text instead of keys):",
                ["Mapping_TextActionApply"] = "Apply",
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

                ["Design_Section"] = "🎨 DESIGN",
                ["Design_Label"] = "Surface style:",
                ["Design_Flat"] = "Flat",
                ["Design_3D"] = "3D",
                ["Design_Shine"] = "Shine",
                ["Design_Hint"] = "3D gives panels, buttons and keys light from above, edges and depth. Shine adds a glossy highlight edge. Applies immediately.",

                ["Background_Section"] = "🎛 APPEARANCE",
                ["Background_EnableTitle"] = "Animated background (GhostFibers)",
                ["Background_EnableHint"] = "Draws a slow-moving field of glowing fibers behind the interface (GPU pixel shader). Turning it off saves power in battery mode.",
                ["Background_QualityLabel"] = "Quality",
                ["Background_QualityAuto"] = "Automatic (measured)",
                ["Background_QualityFull"] = "Full - 30 fps, 4 fiber layers",
                ["Background_QualityReduced"] = "Economy - 20 fps, 3 layers",
                ["Background_QualityMinimal"] = "Minimal - 15 fps, 2 layers, no grain",
                ["Background_QualityHint"] = "Automatic makes the background cheaper instead of switching it off when the display cannot keep up: first fewer fiber layers and fewer frames per second, and only as a last resort off.",

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
                ["Diag_Button"] = "Controller diagnostics",
                ["Diag_Title"] = "Controller diagnostics",
                ["Diag_Endpoints"] = "Input endpoints (live)",
                ["Sound_Enable"] = "Sound feedback on output",
                ["Sound_VolumeLabel"] = "Volume:",
                ["OutputMode_Label"] = "Output mode:",
                ["OutputMode_VK"] = "Normal (VK)",
                ["OutputMode_ScanCode"] = "Compatibility (Scan code)",

                ["Settings_Title"] = "⚙ Settings",
                ["Settings_LanguageLabel"] = "Language:",
                ["Settings_ReselectLabel"] = "Controller reselect:",
                ["Reselect_First"] = "First connected controller",
                ["Reselect_Last"] = "Last used controller",
                ["Settings_GeneralSection"] = "⚙ GENERAL",
                ["Settings_UpdateCheck"] = "Check for updates",
                ["Settings_Close"] = "Close",
                ["Settings_Version"] = "Version {0}",
                ["Settings_UpdateCurrent"] = "You already have the latest version installed.",
                ["Settings_RestartFailed"] = "Failed to restart as administrator.",
                ["Settings_AutostartHint"] = "Starts TenzoraX automatically in the background when Windows starts.",
                ["Settings_StartMinimizedHint"] = "TenzoraX starts minimized to the taskbar.",
                ["Settings_MinimizeToTrayHint"] = "Closing the window minimizes TenzoraX to the tray instead of exiting.",
                ["Settings_AdminHint"] = "Allows input in games that require administrator rights. Requires a restart.",

                ["Notify_Section"] = "🔔 HOTKEY NOTIFICATIONS",
                ["Notify_EnableTitle"] = "Show notifications",
                ["Notify_EnableHint"] = "Shows a visual notification on the screen when a saved controller combination is triggered.",
                ["Notify_Duration"] = "Display duration:",
                ["Notify_DurationHint"] = "Determines how long the notification stays visible on the screen.",
                ["Notify_CornerTitle"] = "Notification position:",
                ["Notify_CornerTopRight"] = "Top right",
                ["Notify_CornerTopLeft"] = "Top left",
                ["Notify_CornerBottomRight"] = "Bottom right",
                ["Notify_CornerBottomLeft"] = "Bottom left",

                ["Vigem_Section"] = "🎮 USE AS XBOX 360",
                ["Vigem_EnableTitle"] = "Mirror active controller as a virtual Xbox 360 controller",
                ["Vigem_EnableHint"] = "Spiele, die nur XInput verstehen, sehen den angeschlossenen Controller dann als echten Xbox-Controller (ViGEm).",
                ["Vigem_TreiberHint"] = "Requires the ViGEmBus driver to be installed on this PC. Without it, no virtual controller is shown – the status below indicates whether it is present.",
                ["Vigem_NoDriverTitle"] = "ViGEmBus driver missing",
                ["Vigem_NoDriverMsg"] = "The ViGEmBus driver is not installed. Please install ViGEmBus and restart TenzoraX so the game recognizes the controller as an Xbox 360 controller.",
                ["Vigem_InstallPrompt"] = "The ViGEmBus driver is not installed. Without it, no virtual Xbox 360 controller can be created.\n\nDo you want to open the download page of the official ViGEmBus driver?",
                ["Vigem_InstallLocalPrompt"] = "The ViGEmBus driver is not installed. A local installer was found.\n\nStart the installer now? (Windows will ask once for administrator confirmation)",

                ["Hidhide_Title"] = "🔒 HIDE THE PHYSICAL GAMEPAD (HidHide)",
                ["Hidhide_Intro"] = "To prevent double input in games, TenzoraX is only able to make the virtual Xbox 360 visible to games. The physical Bluetooth/6-axis controller can additionally be hidden from games using the free HidHide driver (same developer as ViGEmBus).",
                ["Hidhide_Installed"] = "HidHide is installed. Configure it below.",
                ["Hidhide_Missing"] = "HidHide is not installed yet.",
                ["Hidhide_Install"] = "⬇ Download HidHide (official page)",
                ["Hidhide_OpenClient"] = "⚙ Open HidHide",
                ["Hidhide_MissingTitle"] = "HidHide missing",
                ["Hidhide_LaunchError"] = "Could not start HidHide:\n{0}",
                ["Hidhide_AutoActive"] = "Automatically hidden – games only see the virtual Xbox 360.",
                ["Hidhide_AutoNeedsAdmin"] = "Automation needs administrator rights – please run as administrator.",
                ["Hidhide_AutoFailed"] = "Automation failed (HidHideCLI not found).",
                ["Admin_StartupTitle"] = "Administrator rights needed",
                ["Admin_StartupHint"] = "The Xbox 360 mirroring works without administrator rights too – Windows asks once via UAC when needed. Only input into games that themselves run with elevated rights requires admin.\n\nRestart as administrator now?",
                ["Admin_ToggleTitle"] = "Run as administrator?",
                ["Admin_TogglePrompt"] = "For fully automatic Xbox 360 mirroring (hiding the physical gamepad without UAC prompts), TenzoraX should run as administrator.\n\nRun permanently as administrator and restart now?",
                ["Hidhide_StepHeader"] = "Setup:",
                ["Hidhide_Step1"] = "1. Activate \"Mirror as virtual Xbox 360 controller\" above (requires ViGEmBus).",
                ["Hidhide_Step2"] = "2. Open the HidHide Configuration Client, mark your physical gamepad as \"Hidden\" and add TenzoraX.exe to the allowed applications.",
                ["Hidhide_Step3"] = "3. Games now only see the one virtual Xbox 360 – no more double input.",
                ["Hidhide_Note"] = "Important: Keep TenzoraX.exe in HidHide's allowed list, otherwise TenzoraX can no longer read the physical controller as an input source.",
                ["InProgress_Badge"] = "In progress",
                ["Vigem_OpenControllers"] = "Open Game Controllers window",
                ["Vigem_OpenControllersHint"] = "Shows all installed controllers – the virtual Xbox 360 also appears there.",
                ["GamepadPanel_FailedTitle"] = "Game Controllers window",
                ["GamepadPanel_FailedMsg"] = "Windows did not show the Game Controllers window. The panel is probably stuck in a device change – please try again in a moment. Details are in the log (app.log).",
                ["Main_InProgressHint"] = "Xbox 360 mirroring & HidHide: in progress",
                ["Tabs_Combinations"] = "Saved combinations",
                ["Tabs_Controller"] = "Xbox 360 & HidHide",
                ["Drivers_CheckButton"] = "🔎 Check drivers",
                ["Drivers_CheckTitle"] = "Driver check",
                ["Drivers_CheckResult"] = "ViGEmBus (Xbox 360 mirroring): {0}\nHidHide (hide gamepad): {1}\nAdministrator rights: {2}\n\nMissing drivers can be installed using the buttons below. The HidHide automation (hiding the gamepad automatically) requires administrator rights – otherwise Windows asks once via UAC on every toggle.",
                ["Drivers_Installed"] = "installed",
                ["Drivers_Missing"] = "not installed",
                ["Drivers_AdminAvailable"] = "available",
                ["Drivers_AdminMissing"] = "missing – please run as administrator",
                ["Drivers_HeaderTooltip"] = "Drivers: click to install missing ones",
                ["Drivers_HeaderTooltipOk"] = "Drivers installed – ViGEmBus and HidHide are ready. Click for details.",
                ["Drivers_HeaderTooltipMissing"] = "Drivers missing (ViGEmBus: {0}, HidHide: {1}). Click to download and install.",
                ["Drivers_HeaderAllInstalled"] = "Both drivers (ViGEmBus and HidHide) are already installed.",
                ["Drivers_HeaderDownloadFailed"] = "Download failed: {0}. Please check your internet connection and try again.",

                ["Sound_Section"] = "🔊 SOUND FEEDBACK",
                ["Sound_EnableTitle"] = "Enable sound feedback",
                ["Sound_EnableHint"] = "Plays a short confirmation tone when a saved hotkey combination is executed.",
                ["Sound_VolumeHint"] = "Sets the volume of the sound feedback.",

                ["Update_NewAvailable"] = "A new version of TenzoraX is available.",
                ["Update_Installed"] = "Installed:",
                ["Update_New"] = "New:",
                ["Update_Later"] = "Later",
                ["Update_Now"] = "Update now",
                ["Update_Loading"] = "Loading...",
                ["Update_Downloading"] = "Downloading update...",
                ["Update_Installing"] = "Installing update...",
                ["Update_Failed"] = "Download failed. Please try again.",
                ["Update_Retry"] = "Retry",
                ["Tray_Resume"] = "Resume",

                ["Injection_UipiBlocked"] = "Keyboard output blocked: The active window runs with higher privileges than TenzoraX. Restart TenzoraX as administrator (Settings) or close the elevated application.",
                ["Injection_SendInputFailed"] = "Keyboard output failed (Windows error {0}).",
            };

            var de = new Dictionary<string, string>
            {
                ["Title_Window"] = "TenzoraX - Moderner Controller-Mapper",

                ["Status_Label"] = "Status:",
                ["Status_Connected"] = "Verbunden",
                ["Status_Disconnected"] = "Getrennt",
                ["Status_Paused"] = "Pausiert",
                ["Combo_NoController"] = "Kein Controller erkannt",
                ["Status_AccessError"] = "Erkannt, aber Zugriff nicht möglich",
                ["Input_Label"] = "Eingabe:",
                ["Input_Active"] = "Aktiv",
                ["Input_Inactive"] = "Keine Aktivität",
                ["History_Button"] = "Controller-Verlauf",
                ["Gamepad_Collapse"] = "Controller-Feld ausblenden (Tastatur vergrößern)",
                ["Gamepad_Expand"] = "Controller-Feld einblenden",
                ["History_Title"] = "Controller-Verlauf",
                ["History_Empty"] = "Noch kein Verlauf.",
                ["History_LastUsed"] = "Zuletzt verwendet: {0}",
                ["History_Used"] = "Verwendet: {0}x",
                ["History_DeleteConfirm"] = "Eintrag wirklich löschen?",
                ["History_ClearAllConfirm"] = "Wirklich den gesamten Verlauf löschen?",

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
                ["Mapping_NameLabel"] = "Name (Beschriftung):",
                ["Mapping_NameHint"] = "Erscheint in der Liste und in der Benachrichtigung – wird NIE getippt. Die Text-Aktion darunter nur nutzen, wenn die Kombination wirklich Text tippen soll.",
                ["Mapping_ToggleLabel"] = "Umschalter (Taste dauerhaft halten)",
                ["Mapping_ToggleHint"] = "Erster Druck hält die Ausgabe-Tasten gedrückt, zweiter Druck lässt sie wieder los. Gilt für die Tasten-Ausgabe – Text-Aktionen tippen weiterhin einmal pro Druck.",
                ["Mapping_ToggleBadge"] = "Umschalter",
                ["Mapping_TextActionLabel"] = "Text-Aktion (Emojis/Text statt Tasten):",
                ["Mapping_TextActionApply"] = "Übernehmen",
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

                ["Design_Section"] = "🎨 DESIGN",
                ["Design_Label"] = "Oberflächen-Stil:",
                ["Design_Flat"] = "Flach",
                ["Design_3D"] = "3D",
                ["Design_Shine"] = "Shine",
                ["Design_Hint"] = "3D gibt Flächen, Tasten und Feldern Licht von oben, Kanten und Tiefe. Shine setzt zusätzlich eine glänzende Kante. Wirkt sofort.",

                ["Background_Section"] = "🎛 OBERFLÄCHE",
                ["Background_EnableTitle"] = "Animierter Hintergrund (GhostFibers)",
                ["Background_EnableHint"] = "Zeichnet ein langsam ziehendes Feld leuchtender Fasern hinter der Oberfläche (GPU-Pixel-Shader). Ausschalten spart Strom im Akkubetrieb.",
                ["Background_QualityLabel"] = "Qualität",
                ["Background_QualityAuto"] = "Automatisch (gemessen)",
                ["Background_QualityFull"] = "Voll – 30 Bilder/s, 4 Faserschichten",
                ["Background_QualityReduced"] = "Sparsam – 20 Bilder/s, 3 Schichten",
                ["Background_QualityMinimal"] = "Minimal – 15 Bilder/s, 2 Schichten, ohne Grain",
                ["Background_QualityHint"] = "Automatisch macht den Hintergrund sparsamer, statt ihn abzuschalten, wenn die Anzeige nicht mitkommt: erst weniger Faserschichten und weniger Bilder/s, zuletzt ganz aus.",

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
                ["Diag_Button"] = "Controller-Diagnose",
                ["Diag_Title"] = "Controller-Diagnose",
                ["Diag_Endpoints"] = "Eingabe-Endpunkte (live)",
                ["Sound_Enable"] = "Sound-Feedback bei Ausgabe",
                ["Sound_VolumeLabel"] = "Lautstärke:",
                ["OutputMode_Label"] = "Ausgabemodus:",
                ["OutputMode_VK"] = "Normal (VK)",
                ["OutputMode_ScanCode"] = "Kompatibilität (Scan code)",

                ["Settings_Title"] = "⚙ Einstellungen",
                ["Settings_LanguageLabel"] = "Sprache:",
                ["Settings_ReselectLabel"] = "Controller-Wiederauswahl:",
                ["Reselect_First"] = "Erster verbundener Controller",
                ["Reselect_Last"] = "Zuletzt verwendeter Controller",
                ["Settings_GeneralSection"] = "⚙ ALLGEMEIN",
                ["Settings_UpdateCheck"] = "Nach Updates suchen",
                ["Settings_Close"] = "Schließen",
                ["Settings_Version"] = "Version {0}",
                ["Settings_UpdateCurrent"] = "Es ist bereits die aktuellste Version installiert.",
                ["Settings_RestartFailed"] = "Neustart als Administrator fehlgeschlagen.",
                ["Settings_AutostartHint"] = "Startet TenzoraX automatisch beim Windows-Start im Hintergrund.",
                ["Settings_StartMinimizedHint"] = "TenzoraX wird beim Start minimiert in die Taskleiste gestartet.",
                ["Settings_MinimizeToTrayHint"] = "Beim Schließen wird TenzoraX minimiert statt beendet.",
                ["Settings_AdminHint"] = "Ermöglicht die Eingabe in Spielen, die Admin-Rechte benötigen. Erfordert einen Neustart.",

                ["Notify_Section"] = "🔔 HOTKEY-BENACHRICHTIGUNGEN",
                ["Notify_EnableTitle"] = "Benachrichtigungen anzeigen",
                ["Notify_EnableHint"] = "Zeigt eine visuelle Benachrichtigung auf dem Bildschirm an, wenn eine gespeicherte Controller-Kombination ausgelöst wird.",
                ["Notify_Duration"] = "Anzeigedauer:",
                ["Notify_DurationHint"] = "Legt fest, wie lange die Benachrichtigung auf dem Bildschirm sichtbar bleibt.",
                ["Notify_CornerTitle"] = "Position der Benachrichtigung:",
                ["Notify_CornerTopRight"] = "Oben rechts",
                ["Notify_CornerTopLeft"] = "Oben links",
                ["Notify_CornerBottomRight"] = "Unten rechts",
                ["Notify_CornerBottomLeft"] = "Unten links",

                ["Vigem_Section"] = "🎮 ALS XBOX 360 VERWENDEN",
                ["Vigem_EnableTitle"] = "Aktiven Controller als virtuellen Xbox-360-Controller spiegeln",
                ["Vigem_EnableHint"] = "Spiele, die nur XInput verstehen, sehen den angeschlossenen Controller dann als echten Xbox-Controller (ViGEm).",
                ["Vigem_TreiberHint"] = "Benötigt den ViGEmBus-Treiber auf diesem PC. Ohne ihn wird kein virtueller Controller angezeigt – der Status unten zeigt an, ob er vorhanden ist.",
                ["Vigem_NoDriverTitle"] = "ViGEmBus-Treiber fehlt",
                ["Vigem_NoDriverMsg"] = "Der ViGEmBus-Treiber ist nicht installiert. Bitte installiere ViGEmBus und starte TenzoraX neu, damit das Spiel den Controller als Xbox-360-Controller erkennt.",
                ["Vigem_InstallPrompt"] = "Der ViGEmBus-Treiber ist nicht installiert. Ohne ihn kann kein virtueller Xbox-360-Controller erzeugt werden.\n\nMöchtest du die Download-Seite der offiziellen ViGEmBus-Treiber öffnen?",
                ["Vigem_InstallLocalPrompt"] = "Der ViGEmBus-Treiber ist nicht installiert. Es wurde ein lokaler Installer gefunden.\n\nInstaller jetzt starten? (Windows fragt einmalig nach Administrator-Bestätigung)",

                ["Hidhide_Title"] = "🔒 PHYSISCHES GAMEPAD VERSTECKEN (HidHide)",
                ["Hidhide_Intro"] = "Gegen Doppel-Input in Spielen: TenzoraX kann Spielen nur die virtuelle Xbox 360 sichtbar machen. Das physische Bluetooth-/6-Achsen-Gerät lässt sich zusätzlich mit dem kostenlosen HidHide-Treiber für Spiele ausblenden (gleicher Entwickler wie ViGEmBus).",
                ["Hidhide_Installed"] = "HidHide ist installiert. Konfiguriere es wie unten beschrieben.",
                ["Hidhide_Missing"] = "HidHide ist noch nicht installiert.",
                ["Hidhide_Install"] = "⬇ HidHide herunterladen (offizielle Seite)",
                ["Hidhide_OpenClient"] = "⚙ HidHide öffnen",
                ["Hidhide_MissingTitle"] = "HidHide fehlt",
                ["Hidhide_LaunchError"] = "HidHide konnte nicht gestartet werden:\n{0}",
                ["Hidhide_AutoActive"] = "Automatisch versteckt – Spiele sehen nur die virtuelle Xbox 360.",
                ["Hidhide_AutoNeedsAdmin"] = "Automatik benötigt Administrator-Rechte – bitte als Administrator ausführen.",
                ["Hidhide_AutoFailed"] = "Automatik fehlgeschlagen (HidHideCLI nicht gefunden).",
                ["Admin_StartupTitle"] = "Administrator-Rechte nötig",
                ["Admin_StartupHint"] = "Die Xbox-360-Spiegelung funktioniert auch ohne Administrator-Rechte – Windows fragt bei Bedarf einmalig per UAC nach. Nur die Eingabe in Spiele, die selbst mit erhöhten Rechten laufen, benötigt Admin-Rechte.\n\nJetzt als Administrator neu starten?",
                ["Admin_ToggleTitle"] = "Als Administrator ausführen?",
                ["Admin_TogglePrompt"] = "Für eine vollautomatische Xbox-360-Spiegelung (physisches Gamepad verstecken ohne UAC-Rückfragen) sollte TenzoraX als Administrator laufen.\n\nDauerhaft als Administrator ausführen und jetzt neu starten?",
                ["Hidhide_StepHeader"] = "Einrichtung:",
                ["Hidhide_Step1"] = "1. Aktiviere oben \"Als virtuellen Xbox-360-Controller spiegeln\" (benötigt ViGEmBus).",
                ["Hidhide_Step2"] = "2. Öffne den HidHide Configuration Client, markiere dein physisches Gamepad als \"Versteckt\" und füge TenzoraX.exe zu den erlaubten Anwendungen hinzu.",
                ["Hidhide_Step3"] = "3. Spiele sehen jetzt nur noch die eine virtuelle Xbox 360 – kein Doppel-Input mehr.",
                ["Hidhide_Note"] = "Wichtig: Lasse TenzoraX.exe in HidHides erlaubter Liste, sonst kann TenzoraX das physische Gerät nicht mehr als Eingabequelle lesen.",
                ["InProgress_Badge"] = "In Bearbeitung",
                ["Vigem_OpenControllers"] = "Game Controller-Fenster öffnen",
                ["Vigem_OpenControllersHint"] = "Zeigt alle installierten Controller – auch die virtuelle Xbox 360 erscheint dort.",
                ["GamepadPanel_FailedTitle"] = "Gamecontroller-Fenster",
                ["GamepadPanel_FailedMsg"] = "Windows hat das Fenster „Gamecontroller“ nicht angezeigt. Das Panel hängt vermutlich in einer Geräteänderung – bitte gleich noch einmal versuchen. Einzelheiten stehen im Log (app.log).",
                ["Main_InProgressHint"] = "Xbox-360-Spiegelung & HidHide: in Bearbeitung",
                ["Tabs_Combinations"] = "Gespeicherte Kombinationen",
                ["Tabs_Controller"] = "Xbox 360 & HidHide",
                ["Drivers_CheckButton"] = "🔎 Treiber prüfen",
                ["Drivers_CheckTitle"] = "Treiberprüfung",
                ["Drivers_CheckResult"] = "ViGEmBus (Xbox-360-Spiegelung): {0}\nHidHide (Gamepad verstecken): {1}\nAdministrator-Rechte: {2}\n\nFehlende Treiber lassen sich mit den Buttons unten installieren. Die HidHide-Automatik (Gamepad automatisch verstecken) benötigt Administrator-Rechte – sonst fragt Windows bei jedem Umschalten einmalig per UAC nach.",
                ["Drivers_Installed"] = "installiert",
                ["Drivers_Missing"] = "nicht installiert",
                ["Drivers_AdminAvailable"] = "vorhanden",
                ["Drivers_AdminMissing"] = "fehlt – bitte als Administrator ausführen",
                ["Drivers_HeaderTooltip"] = "Treiber: Klick zum Installieren fehlender Treiber",
                ["Drivers_HeaderTooltipOk"] = "Treiber installiert – ViGEmBus und HidHide sind bereit. Klick für Details.",
                ["Drivers_HeaderTooltipMissing"] = "Treiber fehlen (ViGEmBus: {0}, HidHide: {1}). Klick zum Herunterladen und Installieren.",
                ["Drivers_HeaderAllInstalled"] = "Beide Treiber (ViGEmBus und HidHide) sind bereits installiert.",
                ["Drivers_HeaderDownloadFailed"] = "Download fehlgeschlagen: {0}. Bitte Internetverbindung prüfen und erneut versuchen.",

                ["Sound_Section"] = "🔊 SOUNDFEEDBACK",
                ["Sound_EnableTitle"] = "Soundfeedback aktivieren",
                ["Sound_EnableHint"] = "Spielt einen kurzen Bestätigungston ab, wenn eine gespeicherte Hotkey-Kombination erfolgreich ausgeführt wurde.",
                ["Sound_VolumeHint"] = "Legt die Lautstärke des Soundfeedbacks fest.",

                ["Update_NewAvailable"] = "Eine neue Version von TenzoraX ist verfügbar.",
                ["Update_Installed"] = "Installiert:",
                ["Update_New"] = "Neu:",
                ["Update_Later"] = "Später",
                ["Update_Now"] = "Jetzt aktualisieren",
                ["Update_Loading"] = "Wird geladen...",
                ["Update_Downloading"] = "Update wird heruntergeladen...",
                ["Update_Installing"] = "Update wird installiert...",
                ["Update_Failed"] = "Download fehlgeschlagen. Bitte erneut versuchen.",
                ["Update_Retry"] = "Wiederholen",
                ["Tray_Resume"] = "Fortsetzen",

                ["Injection_UipiBlocked"] = "Tastatur-Ausgabe blockiert: Das aktive Fenster läuft mit höheren Rechten als TenzoraX. Starte TenzoraX als Administrator (Einstellungen) oder beende die Anwendung mit Administrator-Rechten.",
                ["Injection_SendInputFailed"] = "Tastatur-Ausgabe fehlgeschlagen (Windows-Fehler {0}).",
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

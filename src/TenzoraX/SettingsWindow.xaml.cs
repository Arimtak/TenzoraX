using System;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace TenzoraX;

public partial class SettingsWindow : Window
{
    private readonly AppSettings _settings;
    private readonly Action _save;
    private readonly Action<string>? _onLanguageChanged;

    /// <summary>
    /// Verhindert, dass ValueChanged/Checked-Handler während des Ladens gespeicherte
    /// Werte überschreiben: Die Slider tragen im XAML einen festen Default (z.B.
    /// Value="5.0"), wodurch der ValueChanged-Handler bereits bei InitializeComponent()
    /// feuert – also BEVOR LoadSavedValues() den echten Wert aus der Config anwendet.
    /// Ohne Sperre würde z.B. die gespeicherte Benachrichtigungsdauer (2.5) beim
    /// Öffnen des Fensters still auf 5.0 zurückgesetzt.
    /// </summary>
    private bool _loading;

    public SettingsWindow(AppSettings settings, Action save, Action<string>? onLanguageChanged = null)
    {
        _settings = settings;
        _save = save;
        _onLanguageChanged = onLanguageChanged;
            Owner = System.Windows.Application.Current.MainWindow;
        DataContext = LanguageManager.Instance;
        _loading = true;
        InitializeComponent();
        // Dunkle Titelleiste wie beim Hauptfenster (Standard-Chrome ist sonst hell)
        SourceInitialized += (s, e) => ThemeHelper.ApplyImmersiveDarkMode(this);
        LoadSavedValues();
        _loading = false;
    }

    private void LoadSavedValues()
    {
        ChkBatteryEnable.IsChecked = _settings.BatteryEnabled;
        TxtBatteryHours.Text = _settings.BatteryHours.ToString("0.#");
        ChkBatteryTray.IsChecked = _settings.BatteryTrayEnabled;
        ChkBatteryAnimation.IsChecked = _settings.BatteryAnimationEnabled;
        UpdateBatteryCalculatedLabel();

        ChkAnimatedBackground.IsChecked = _settings.AnimatedBackgroundEnabled;

        // Qualitätsstufe des Hintergrunds (0 = automatisch) passend vorbelegen.
        ComboBackgroundQuality.SelectedIndex = 0;
        foreach (ComboBoxItem item in ComboBackgroundQuality.Items)
        {
            if (item.Tag is string tag && int.TryParse(tag, out int stage)
                && stage == _settings.BackgroundQualityOverride)
            {
                ComboBackgroundQuality.SelectedItem = item;
                break;
            }
        }

        ChkNotification.IsChecked = _settings.HotkeyNotificationsEnabled;
        SliderNotificationDuration.Value = _settings.NotificationDuration;
        UpdateCornerButtons();

        ChkSound.IsChecked = _settings.SoundEnabled;
        SliderSoundVolume.Value = _settings.SoundVolume * 100;

        ChkAutostart.IsChecked = _settings.AutostartEnabled;
        ChkStartMinimized.IsChecked = _settings.StartMinimized;
        ChkMinimizeToTray.IsChecked = _settings.MinimizeToTray;
        ChkAdminMode.IsChecked = _settings.RunAsAdministrator;
        UpdateAdminStatus();

        // Aktuelle Sprache in der Auswahl vorbelegen.
        string currentLang = LanguageManager.Instance.CurrentLang;
        foreach (ComboBoxItem item in ComboLanguage.Items)
        {
            if (item.Tag is string lang && lang == currentLang)
            {
                ComboLanguage.SelectedItem = item;
                break;
            }
        }

        // Controller-Wiederauswahl vorbelegen.
        foreach (ComboBoxItem item in ComboReselectMode.Items)
        {
            if (item.Tag is string mode && mode == _settings.ControllerReselectMode)
            {
                ComboReselectMode.SelectedItem = item;
                break;
            }
        }

        TxtSettingsVersion.Text = LanguageManager.Instance.Format("Settings_Version", AppVersion.Current);
    }

    /// <summary>Wechselt die Sprache sofort (ohne Neustart) und speichert die Auswahl.</summary>
    private void ComboLanguage_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (ComboLanguage.SelectedItem is not ComboBoxItem item || item.Tag is not string lang)
            return;
        if (LanguageManager.Instance.CurrentLang == lang)
            return;

        _settings.Language = lang;
        LanguageManager.Instance.Switch(lang);
        SaveNow();

        RefreshLocalizedTexts();
        _onLanguageChanged?.Invoke(lang);
    }

    /// <summary>Aktualisiert Texte, die im Code gesetzt werden (nicht per XAML-Binding).</summary>
    private void RefreshLocalizedTexts()
    {
        UpdateAdminStatus();
        UpdateBatteryCalculatedLabel();
        TxtSettingsVersion.Text = LanguageManager.Instance.Format("Settings_Version", AppVersion.Current);
    }

    private void ComboReselectMode_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (ComboReselectMode.SelectedItem is not ComboBoxItem item || item.Tag is not string mode)
            return;
        if (_settings.ControllerReselectMode == mode)
            return;

        _settings.ControllerReselectMode = mode;
        SaveNow();
    }

    private void SaveNow()
    {
        try { _save(); } catch { }
    }

    private void UpdateBatteryCalculatedLabel()
    {
        double totalMinutes = _settings.BatteryHours * 60;
        double consumptionPerMinute = totalMinutes > 0 ? 100.0 / totalMinutes : 0;
        TxtBatteryCalculated.Text = LanguageManager.Instance.Format("Battery_Calculated", (int)totalMinutes, consumptionPerMinute);
    }

    private void ChkBatteryEnable_Changed(object sender, RoutedEventArgs e)
    {
        _settings.BatteryEnabled = ChkBatteryEnable.IsChecked == true;
        SaveNow();
    }

    private void TxtBatteryHours_LostFocus(object sender, RoutedEventArgs e)
    {
        ApplyBatteryHours();
    }

    private void TxtBatteryHours_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Enter)
            ApplyBatteryHours();
    }

    private void ApplyBatteryHours()
    {
        if (double.TryParse(TxtBatteryHours.Text, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double val))
        {
            val = Math.Max(1, Math.Min(99, val));
            _settings.BatteryHours = val;
            TxtBatteryHours.Text = val.ToString("0.#");
            UpdateBatteryCalculatedLabel();
            SaveNow();
        }
    }

    private void ChkBatteryTray_Changed(object sender, RoutedEventArgs e)
    {
        _settings.BatteryTrayEnabled = ChkBatteryTray.IsChecked == true;
        SaveNow();
    }

    private void ChkAnimatedBackground_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _settings.AnimatedBackgroundEnabled = ChkAnimatedBackground.IsChecked == true;
        _save();
    }

    private void ComboBackgroundQuality_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;

        if (ComboBackgroundQuality.SelectedItem is ComboBoxItem item
            && item.Tag is string tag && int.TryParse(tag, out int stage))
        {
            // 0 = automatisch nach gemessenem Anzeigetakt,
            // 1 = minimal, 2 = sparsam, 3 = voll (siehe BackgroundQuality).
            _settings.BackgroundQualityOverride = stage;
            SaveNow();
        }
    }

    private void ChkBatteryAnimation_Changed(object sender, RoutedEventArgs e)
    {
        _settings.BatteryAnimationEnabled = ChkBatteryAnimation.IsChecked == true;
        SaveNow();
    }

    private void BtnResetBattery_Click(object sender, RoutedEventArgs e)
    {
        _settings.BatteryActiveMinutes = 0;
        _settings.BatteryHours = 15;
        TxtBatteryHours.Text = "15";
        UpdateBatteryCalculatedLabel();
        SaveNow();
    }

    private void BtnConfirmBattery_Click(object sender, RoutedEventArgs e)
    {
        ApplyBatteryHours();
    }

    private void ChkNotification_Changed(object sender, RoutedEventArgs e)
    {
        _settings.HotkeyNotificationsEnabled = ChkNotification.IsChecked == true;
        NotificationManager.Enabled = _settings.HotkeyNotificationsEnabled;
        SaveNow();
    }

    private void SliderNotificationDuration_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading) return;
        _settings.NotificationDuration = SliderNotificationDuration.Value;
        NotificationManager.Duration = _settings.NotificationDuration;
        SaveNow();
    }

    private void CornerButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn && btn.Tag is string corner)
        {
            _settings.NotificationCorner = corner;
            NotificationManager.Corner = corner;
            // Benutzerdefinierte Drag-Position zurücksetzen → die Ecke gilt sofort
            NotificationManager.ResetSavedPosition();
            _settings.NotificationPositionX = -1;
            _settings.NotificationPositionY = -1;
            SaveNow();
            UpdateCornerButtons();
        }
    }

    private void UpdateCornerButtons()
    {
        var accent = (System.Windows.Media.Brush)FindResource("AccentBrush");
        var border = (System.Windows.Media.Brush)FindResource("BorderBrush");
        var selectedBg = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(15, 44, 58));
        var normalBg = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(26, 26, 36));

        SetCornerButton(BtnCornerTopLeft, DotTopLeft, NotificationManager.CornerTopLeft, accent, border, selectedBg, normalBg);
        SetCornerButton(BtnCornerTopRight, DotTopRight, NotificationManager.CornerTopRight, accent, border, selectedBg, normalBg);
        SetCornerButton(BtnCornerBottomLeft, DotBottomLeft, NotificationManager.CornerBottomLeft, accent, border, selectedBg, normalBg);
        SetCornerButton(BtnCornerBottomRight, DotBottomRight, NotificationManager.CornerBottomRight, accent, border, selectedBg, normalBg);
    }

    private static void SetCornerButton(System.Windows.Controls.Button btn, System.Windows.Shapes.Ellipse dot, string corner,
        System.Windows.Media.Brush accent, System.Windows.Media.Brush border,
        System.Windows.Media.Brush selectedBg, System.Windows.Media.Brush normalBg)
    {
        bool selected = NotificationManager.Corner == corner;
        btn.Background = selected ? selectedBg : normalBg;
        btn.BorderBrush = selected ? accent : border;
        dot.Visibility = selected ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ChkSound_Changed(object sender, RoutedEventArgs e)
    {
        _settings.SoundEnabled = ChkSound.IsChecked == true;
        SoundManager.Enabled = _settings.SoundEnabled;
        SaveNow();
    }

    private void SliderSoundVolume_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading) return;
        _settings.SoundVolume = SliderSoundVolume.Value / 100.0;
        SoundManager.Volume = _settings.SoundVolume;
        SaveNow();
    }

    private void ChkAutostart_Changed(object sender, RoutedEventArgs e)
    {
        _settings.AutostartEnabled = ChkAutostart.IsChecked == true;
        SaveNow();
        SetAutostartRegistry(_settings.AutostartEnabled);
    }

    private void ChkStartMinimized_Changed(object sender, RoutedEventArgs e)
    {
        _settings.StartMinimized = ChkStartMinimized.IsChecked == true;
        SaveNow();
    }

    private void ChkMinimizeToTray_Changed(object sender, RoutedEventArgs e)
    {
        _settings.MinimizeToTray = ChkMinimizeToTray.IsChecked == true;
        SaveNow();
    }

    private void ChkAdminMode_Changed(object sender, RoutedEventArgs e)
    {
        bool newValue = ChkAdminMode.IsChecked == true;
        _settings.RunAsAdministrator = newValue;
        SaveNow();
        UpdateAdminStatus();

        if (newValue && !InputSimulator.IsRunningAsAdmin())
        {
            var result = System.Windows.MessageBox.Show(
                LanguageManager.Instance.Dialog_RestartAsAdminMsg,
                LanguageManager.Instance.Dialog_RestartAsAdminTitle,
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    var restArgs = Environment.GetCommandLineArgs().Skip(1).ToList();
                    if (!restArgs.Contains("--admin-restart")) restArgs.Add("--admin-restart");
                    var argStr = string.Join(" ", restArgs
                        .Select(a => a.Contains(' ') ? $"\"{a}\"" : a));
                    var psi = new ProcessStartInfo
                    {
                        FileName = Environment.ProcessPath,
                        UseShellExecute = true,
                        Verb = "runas",
                        Arguments = argStr
                    };
                    Process.Start(psi);
                    App.ReleaseInstanceMutex();
                    Environment.Exit(0);
                }
                catch
                {
                    System.Windows.MessageBox.Show(
                        LanguageManager.Instance.Settings_RestartFailed,
                        LanguageManager.Instance.Dialog_AutostartErrorTitle,
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
    }

    private void UpdateAdminStatus()
    {
        bool isAdmin = InputSimulator.IsRunningAsAdmin();
        TxtAdminStatus.Text = isAdmin
            ? LanguageManager.Instance.Edit_AdminElevated
            : LanguageManager.Instance.Edit_AdminUser;
        TxtAdminStatus.Foreground = isAdmin
            ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(245, 158, 11))
            : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(148, 163, 184));
    }

    /// <summary>
    /// Autostart über den Windows-Task-Scheduler mit höchsten Rechten (/RL HIGHEST).
    /// Dadurch startet TenzoraX beim Anmelden DIREKT erhöht – ohne UAC-Prompt und ohne
    /// sichtbares Aufblitzen. Der alte Registry-Run-Eintrag wird dabei immer entfernt.
    /// Das Anlegen/Löschen des Tasks braucht nur EINMALIG Administratorrechte (genau
    /// beim Aktivieren/Deaktivieren in den Einstellungen); beim Boot selbst erscheint
    /// danach keinerlei Abfrage mehr.
    /// </summary>
    private void SetAutostartRegistry(bool enable)
    {
        try
        {
            const string taskName = "TenzoraX";
            string exePath = Process.GetCurrentProcess().MainModule?.FileName ?? "";
            if (string.IsNullOrEmpty(exePath)) return;

            // Alten Registry-Run-Eintrag immer entfernen (Migration zur Task-Lösung).
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
                key?.DeleteValue(taskName, false);
            }
            catch { }

            string schtasksArgs;
            if (enable)
                schtasksArgs = $"/Create /TN \"{taskName}\" /TR \"\\\"{exePath}\\\" --minimized\" /SC ONLOGON /RL HIGHEST /F";
            else
                schtasksArgs = $"/Delete /TN \"{taskName}\" /F";

            var psi = new ProcessStartInfo("schtasks.exe", schtasksArgs)
            {
                UseShellExecute = true,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            // /RL HIGHEST verlangt Admin-Rechte beim Anlegen/Löschen – genau hier
            // erscheint einmalig die UAC-Abfrage (Nutzeraktion in den Einstellungen).
            if (!InputSimulator.IsRunningAsAdmin())
                psi.Verb = "runas";

            using var p = Process.Start(psi);
            p?.WaitForExit(15000);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                LanguageManager.Instance.Format("Dialog_AutostartErrorMsg", ex.Message),
                LanguageManager.Instance.Dialog_AutostartErrorTitle,
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// Migration beim App-Start: Falls „Mit Windows starten“ aktiv ist und noch der
    /// alte Registry-Run-Eintrag existiert (kein Task-Scheduler-Eintrag), wird der
    /// Autostart auf den Task-Scheduler mit höchsten Rechten umgestellt. Läuft die App
    /// bereits erhöht (wie nach der Migration üblich), passiert das völlig still.
    /// </summary>
    internal static void MigrateAutostartToTaskScheduler()
    {
        try
        {
            const string taskName = "TenzoraX";

            // Existiert der Task bereits? Dann ist nichts zu tun.
            var queryPsi = new ProcessStartInfo("schtasks.exe", $"/Query /TN \"{taskName}\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using (var q = Process.Start(queryPsi))
            {
                if (q == null) return;
                q.WaitForExit(5000);
                if (q.ExitCode == 0) return; // Task existiert bereits
            }

            // Task anlegen – nur möglich, wenn die App erhöht läuft. Sonst still
            // überspringen (der nächste Besuch der Einstellungen legt ihn an).
            if (!InputSimulator.IsRunningAsAdmin()) return;

            string exePath = Process.GetCurrentProcess().MainModule?.FileName ?? "";
            if (string.IsNullOrEmpty(exePath)) return;

            var createPsi = new ProcessStartInfo("schtasks.exe",
                $"/Create /TN \"{taskName}\" /TR \"\\\"{exePath}\\\" --minimized\" /SC ONLOGON /RL HIGHEST /F")
            {
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using (var c = Process.Start(createPsi))
                c?.WaitForExit(15000);

            // Alten Registry-Run-Eintrag entfernen.
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
                key?.DeleteValue(taskName, false);
            }
            catch { }
        }
        catch { }
    }

    private async void BtnCheckUpdate_Click(object sender, RoutedEventArgs e)
    {
        BtnCheckUpdate.IsEnabled = false;
        try
        {
            var info = await UpdateManager.CheckForUpdate();
            if (info == null)
            {
                ThemeHelper.ShowMessage(this,
                    LanguageManager.Instance.Settings_UpdateCurrent,
                    LanguageManager.Instance.Update_New);
                return;
            }

            var dlg = UpdateDialog.ShowUpdate(this, info);
            if (dlg?.DownloadedExePath != null && System.IO.File.Exists(dlg.DownloadedExePath))
            {
                try { UpdateManager.InstallUpdate(dlg.DownloadedExePath); } catch { }
            }
        }
        finally
        {
            BtnCheckUpdate.IsEnabled = true;
        }
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}

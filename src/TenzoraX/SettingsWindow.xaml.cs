using System;
using System.Windows;

namespace TenzoraX;

public partial class SettingsWindow : Window
{
    private readonly AppSettings _settings;
    private readonly Action _save;

    public SettingsWindow(AppSettings settings, Action save)
    {
        _settings = settings;
        _save = save;
            Owner = System.Windows.Application.Current.MainWindow;
        InitializeComponent();
        LoadSavedValues();
    }

    private void LoadSavedValues()
    {
        ChkBatteryEnable.IsChecked = _settings.BatteryEnabled;
        TxtBatteryHours.Text = _settings.BatteryHours.ToString("0.#");
        ChkBatteryTray.IsChecked = _settings.BatteryTrayEnabled;
        ChkBatteryAnimation.IsChecked = _settings.BatteryAnimationEnabled;
        UpdateBatteryCalculatedLabel();

        ChkNotification.IsChecked = _settings.HotkeyNotificationsEnabled;
        SliderNotificationDuration.Value = _settings.NotificationDuration;

        ChkSound.IsChecked = _settings.SoundEnabled;
        SliderSoundVolume.Value = _settings.SoundVolume * 100;

        TxtSettingsVersion.Text = "Version " + AppVersion.Current;
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
        _settings.NotificationDuration = SliderNotificationDuration.Value;
        NotificationManager.Duration = _settings.NotificationDuration;
        SaveNow();
    }

    private void ChkSound_Changed(object sender, RoutedEventArgs e)
    {
        _settings.SoundEnabled = ChkSound.IsChecked == true;
        SoundManager.Enabled = _settings.SoundEnabled;
        SaveNow();
    }

    private void SliderSoundVolume_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        _settings.SoundVolume = SliderSoundVolume.Value / 100.0;
        SoundManager.Volume = _settings.SoundVolume;
        SaveNow();
    }

    private async void BtnCheckUpdate_Click(object sender, RoutedEventArgs e)
    {
        BtnCheckUpdate.IsEnabled = false;
        try
        {
            var info = await UpdateManager.CheckForUpdate();
            if (info == null)
            {
                System.Windows.MessageBox.Show(this,
                    LanguageManager.Instance.CurrentLang == "de"
                        ? "Es ist bereits die aktuellste Version installiert."
                        : "You already have the latest version installed.",
                    "Update", MessageBoxButton.OK, MessageBoxImage.Information);
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

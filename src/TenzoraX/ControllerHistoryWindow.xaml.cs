using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;

namespace TenzoraX
{
    public partial class ControllerHistoryWindow : Window
    {
        private class HistoryItem
        {
            public string Name { get; set; } = "";
            public string ConnectionDisplay { get; set; } = "";
            public string DetailLine { get; set; } = "";
            public string VidPid { get; set; } = "";
            public string StableId { get; set; } = "";
        }

        public ControllerHistoryWindow()
        {
            InitializeComponent();
            ThemeHelper.StyleWindow(this);
            DataContext = LanguageManager.Instance;
            LoadHistory();
        }

        private void LoadHistory()
        {
            var entries = ControllerHistoryManager.Instance.GetEntries();
            var items = new List<HistoryItem>();
            foreach (var e in entries)
            {
                string lastSeen = "";
                if (DateTime.TryParse(e.LastSeen, null, DateTimeStyles.RoundtripKind, out var dt))
                    lastSeen = dt.ToLocalTime().ToString("dd.MM.yyyy HH:mm");

                string used = LanguageManager.Instance.Format("History_Used", e.TimesConnected);
                string lastUsed = LanguageManager.Instance.Format("History_LastUsed", lastSeen);

                items.Add(new HistoryItem
                {
                    Name = e.Name,
                    ConnectionDisplay = "(" + e.ConnectionType + ")",
                    DetailLine = lastUsed + " · " + used,
                    VidPid = e.VidPid,
                    StableId = e.StableId
                });
            }

            LstHistory.ItemsSource = items;
            TxtEmpty.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            Title = LanguageManager.Instance["History_Title"];
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void BtnDeleteEntry_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button btn && btn.Tag is string stableId)
            {
                var result = System.Windows.MessageBox.Show(
                    LanguageManager.Instance["History_DeleteConfirm"] ?? "Eintrag wirklich löschen?",
                    LanguageManager.Instance["History_Title"] ?? "Controller-Verlauf",
                    MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (result != MessageBoxResult.Yes) return;

                ControllerHistoryManager.Instance.RemoveEntry(stableId);
                LoadHistory();
            }
        }

        private void BtnClearAll_Click(object sender, RoutedEventArgs e)
        {
            var result = System.Windows.MessageBox.Show(
                LanguageManager.Instance["History_ClearAllConfirm"] ?? "Wirklich den gesamten Verlauf löschen?",
                LanguageManager.Instance["History_Title"] ?? "Controller-Verlauf",
                MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes) return;

            ControllerHistoryManager.Instance.ClearAll();
            LoadHistory();
        }
    }
}
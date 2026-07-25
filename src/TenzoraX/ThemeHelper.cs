using System.Windows;
using System.Windows.Media;

namespace TenzoraX
{
    internal static class ThemeHelper
    {
        public static System.Windows.Media.Color Bg => System.Windows.Media.Color.FromRgb(15, 15, 18);
        public static System.Windows.Media.Color PanelBg => System.Windows.Media.Color.FromRgb(22, 22, 30);
        public static System.Windows.Media.Color CardBg => System.Windows.Media.Color.FromRgb(13, 13, 18);
        public static System.Windows.Media.Color Text => System.Windows.Media.Color.FromRgb(226, 232, 240);
        public static System.Windows.Media.Color MutedText => System.Windows.Media.Color.FromRgb(148, 163, 184);
        public static System.Windows.Media.Color Accent => System.Windows.Media.Color.FromRgb(0, 210, 255);
        public static System.Windows.Media.Color Border => System.Windows.Media.Color.FromRgb(45, 45, 61);
        public static System.Windows.Media.Color HoverBg => System.Windows.Media.Color.FromRgb(42, 42, 56);
        public static System.Windows.Media.Color InputBg => System.Windows.Media.Color.FromRgb(30, 30, 38);

        public static SolidColorBrush BgBrush => new(Bg);
        public static SolidColorBrush PanelBgBrush => new(PanelBg);
        public static SolidColorBrush CardBgBrush => new(CardBg);
        public static SolidColorBrush TextBrush => new(Text);
        public static SolidColorBrush MutedTextBrush => new(MutedText);
        public static SolidColorBrush AccentBrush => new(Accent);
        public static SolidColorBrush BorderBrush => new(Border);
        public static SolidColorBrush HoverBgBrush => new(HoverBg);
        public static SolidColorBrush InputBgBrush => new(InputBg);

        public static void StyleWindow(Window window)
        {
            window.Background = BgBrush;
            window.Foreground = TextBrush;
            if (window.FontFamily == null || window.FontFamily.Source == "Portable User Interface")
                window.FontFamily = new System.Windows.Media.FontFamily("Segoe UI");
        }

        public static MessageBoxResult ShowMessage(Window owner, string message, string title, MessageBoxButton buttons = MessageBoxButton.OK)
        {
            var win = new Window
            {
                Title = title,
                Width = 380,
                Height = 180,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ResizeMode = ResizeMode.NoResize,
                WindowStyle = WindowStyle.SingleBorderWindow,
                ShowInTaskbar = false,
                Owner = owner
            };
            StyleWindow(win);

            var grid = new System.Windows.Controls.Grid { Margin = new Thickness(20) };
            grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = GridLength.Auto });

            var text = new System.Windows.Controls.TextBlock
            {
                Text = message,
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center
            };
            grid.Children.Add(text);

            var btnPanel = new System.Windows.Controls.StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Right
            };
            System.Windows.Controls.Grid.SetRow(btnPanel, 1);

            MessageBoxResult result = MessageBoxResult.OK;
            var okBtn = new System.Windows.Controls.Button { Content = "OK", Width = 80, Height = 28 };
            okBtn.Click += (s, e) => { result = MessageBoxResult.OK; win.Close(); };

            var yesBtn = new System.Windows.Controls.Button { Content = "Ja", Width = 80, Height = 28, Margin = new Thickness(0, 0, 8, 0) };
            yesBtn.Click += (s, e) => { result = MessageBoxResult.Yes; win.Close(); };

            var noBtn = new System.Windows.Controls.Button { Content = "Nein", Width = 80, Height = 28, Margin = new Thickness(0, 0, 8, 0) };
            noBtn.Click += (s, e) => { result = MessageBoxResult.No; win.Close(); };

            if (buttons == MessageBoxButton.OK)
            {
                btnPanel.Children.Add(okBtn);
            }
            else if (buttons == MessageBoxButton.YesNo)
            {
                btnPanel.Children.Add(yesBtn);
                btnPanel.Children.Add(noBtn);
            }

            grid.Children.Add(btnPanel);
            win.Content = grid;
            win.ShowDialog();
            return result;
        }
    }
}

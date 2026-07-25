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
    }
}

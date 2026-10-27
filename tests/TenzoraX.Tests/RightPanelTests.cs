using System;
using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace TenzoraX.Tests
{
    /// <summary>
    /// Hält den rechten Bereich aufgeräumt (Wunsch des Nutzers, 27.10.2026): Die Reiterleiste
    /// und der Reiter „Xbox 360 &amp; HidHide“ sind entfernt, die Liste der gespeicherten
    /// Kombinationen bekommt den ganzen Platz, zeigt keinen Regler mehr und hat einen Titel.
    /// Die verborgenen Bedienelemente der Xbox-Spiegelung werden mitgeprüft: Der Code spricht
    /// sie weiter an (Statuspflege), ihr Fehlen würde den Build brechen – der Test hält den
    /// Grund fest, damit sie nicht versehentlich „aufgeräumt“ werden.
    /// </summary>
    public class RightPanelTests
    {
        private static string RepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "TenzoraX.sln")))
                dir = dir.Parent;
            Assert.NotNull(dir);
            return dir!.FullName;
        }

        private static string Xaml()
            => File.ReadAllText(Path.Combine(RepoRoot(), "src", "TenzoraX", "MainWindow.xaml"));

        private static string ListBoxBlock()
        {
            var m = Regex.Match(Xaml(), "x:Name=\"ListMappings\"[\\s\\S]{0,400}?SelectionChanged=\"ListMappings_SelectionChanged\"");
            Assert.True(m.Success, "ListBox ListMappings nicht gefunden");
            return m.Value;
        }

        [Fact]
        public void RechterBereich_HatKeineReiterleisteMehr()
        {
            string xaml = Xaml();
            Assert.DoesNotContain("TabsRightPanel", xaml);
            Assert.DoesNotContain("TabController", xaml);
            Assert.DoesNotContain("Header=\"{Binding Tabs_Controller}\"", xaml);
        }

        [Fact]
        public void Liste_HatKeinenSichtbarenRegler()
        {
            // Hidden heißt: weiter scrollbar (Mausrad), aber ohne Balken.
            Assert.Contains("VerticalScrollBarVisibility=\"Hidden\"", ListBoxBlock());
            Assert.DoesNotContain("VerticalScrollBarVisibility=\"Auto\"", ListBoxBlock());
        }

        [Fact]
        public void Liste_StrecksDieKartenUndTraegtEinenTitel()
        {
            Assert.Contains("HorizontalContentAlignment=\"Stretch\"", ListBoxBlock());
            Assert.Contains("Text=\"{Binding Mapping_ListTitle}\"", Xaml());
        }

        [Fact]
        public void VerborgeneXboxBedienelemente_BleibenImFensterbaum()
        {
            string xaml = Xaml();
            Assert.Contains("x:Name=\"PanelVigemHidhide\"", xaml);
            Assert.Contains("Visibility=\"Collapsed\"", xaml);
            foreach (var name in new[]
                     {
                         "ChkVigemXboxMain", "TxtVigemStatusMain", "VigemStatusDotMain",
                         "TxtHidhideStatusMain", "HidhideStatusDotMain",
                         "BtnCheckDrivers", "BtnInstallHidhideMain", "BtnOpenHidhideClient",
                         "BtnOpenGameControllersMain"
                     })
            {
                Assert.Contains($"x:Name=\"{name}\"", xaml);
            }
        }
    }
}

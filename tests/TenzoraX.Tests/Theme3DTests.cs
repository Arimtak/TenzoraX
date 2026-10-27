using TenzoraX;
using Xunit;

namespace TenzoraX.Tests
{
    /// <summary>
    /// Die App startet im Standard-Stil – Shine (Wunsch vom 27.10.2026, vorher war 3D der
    /// Standard). Ein anderer Stil gilt nur, wenn er in den Einstellungen **bewusst gewählt**
    /// wurde, erkennbar an <c>"UiThemeUserChoice": true</c>. Ein allein gespeichertes "3d"
    /// stammt aus der Zeit, als 3D der Standard war, und darf den neuen Standard nicht
    /// blockieren. Getestet wird die reine Leselogik, ohne WPF-Anwendung
    /// (Application.Current ist in Tests null).
    /// </summary>
    public class Theme3DTests
    {
        [Fact]
        public void Standard_ist_Shine()
        {
            Assert.Equal(Theme3D.Shine, Theme3D.Default);
            Assert.Equal(Theme3D.Shine, Theme3D.Current);
        }

        [Fact]
        public void OhneConfig_StartetImStandard()
        {
            Assert.Equal(Theme3D.Default, Theme3D.ThemeFromConfigJson(null));
            Assert.Equal(Theme3D.Default, Theme3D.ThemeFromConfigJson(""));
            Assert.Equal(Theme3D.Default, Theme3D.ThemeFromConfigJson("   "));
        }

        [Fact]
        public void ConfigOhneTheme_StartetImStandard()
        {
            Assert.Equal(Theme3D.Default,
                Theme3D.ThemeFromConfigJson("{ \"Language\": \"de\", \"SoundEnabled\": true }"));
        }

        [Theory]
        [InlineData("flat")]
        [InlineData("3d")]
        [InlineData("shine")]
        public void BewusstGewaehlterStil_Gilt(string theme)
        {
            string json = "{ \"UiTheme\": \"" + theme + "\", \"UiThemeUserChoice\": true }";
            Assert.Equal(theme, Theme3D.ThemeFromConfigJson(json));
        }

        [Fact]
        public void OhneKennzeichen_GiltDerStandard()
        {
            // Der echte Fall: Die bestehende config.json trägt noch das damalige Standard-"3d"
            // (mitgeschrieben von einer älteren Fassung) – ohne Kennzeichen zählt der Standard.
            Assert.Equal(Theme3D.Default, Theme3D.ThemeFromConfigJson("{ \"UiTheme\": \"3d\" }"));
            Assert.Equal(Theme3D.Default, Theme3D.ThemeFromConfigJson("{ \"UiTheme\": \"shine\" }"));
            Assert.Equal(Theme3D.Default,
                Theme3D.ThemeFromConfigJson("{ \"UiTheme\": \"3d\", \"UiThemeUserChoice\": false }"));
        }

        [Fact]
        public void GewaehlterStil_WirdNormalisiert()
        {
            Assert.Equal(Theme3D.Shine,
                Theme3D.ThemeFromConfigJson("{ \"UiTheme\": \"  SHINE \", \"UiThemeUserChoice\": true }"));
            // Unbekannter Wert fällt auf den Standard zurück, nicht auf einen Zufallsstil.
            Assert.Equal(Theme3D.Default,
                Theme3D.ThemeFromConfigJson("{ \"UiTheme\": \"unbekannt\", \"UiThemeUserChoice\": true }"));
        }

        [Fact]
        public void KaputteConfig_StartetImStandard()
        {
            Assert.Equal(Theme3D.Default, Theme3D.ThemeFromConfigJson("{ \"UiTheme\": "));
            Assert.Equal(Theme3D.Default, Theme3D.ThemeFromConfigJson("kein json"));
            Assert.Equal(Theme3D.Default, Theme3D.ThemeFromConfigJson("{ \"UiTheme\": 42 }"));
            Assert.Equal(Theme3D.Default, Theme3D.ThemeFromConfigJson("{ \"UiThemeUserChoice\": \"ja\" }"));
        }

        [Fact]
        public void Einstellungen_OhneWahlTragenDenStandard()
        {
            // Der Startwert des Modells muss zum Standard passen, sonst schreibt die erste
            // Speicherung wieder einen anderen Stil in die config.json.
            var settings = new AppSettings();
            Assert.Equal(Theme3D.Default, settings.UiTheme);
            Assert.False(settings.UiThemeUserChoice);
        }
    }
}

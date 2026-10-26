using TenzoraX;
using Xunit;

namespace TenzoraX.Tests
{
    /// <summary>
    /// Die App soll immer in 3D starten – außer es wurde bewusst ein anderer Stil
    /// gewählt und gespeichert; dann gilt dieser. Getestet wird die reine Leselogik,
    /// ohne WPF-Anwendung (Application.Current ist in Tests null).
    /// </summary>
    public class Theme3DTests
    {
        [Fact]
        public void OhneConfig_StartetIn3D()
        {
            Assert.Equal(Theme3D.ThreeD, Theme3D.ThemeFromConfigJson(null));
            Assert.Equal(Theme3D.ThreeD, Theme3D.ThemeFromConfigJson(""));
            Assert.Equal(Theme3D.ThreeD, Theme3D.ThemeFromConfigJson("   "));
        }

        [Fact]
        public void ConfigOhneUiTheme_StartetIn3D()
        {
            Assert.Equal(Theme3D.ThreeD, Theme3D.ThemeFromConfigJson("{ \"Language\": \"de\", \"SoundEnabled\": true }"));
        }

        [Theory]
        [InlineData("flat")]
        [InlineData("3d")]
        [InlineData("shine")]
        public void GespeicherterStil_Gilt(string theme)
        {
            string json = "{ \"UiTheme\": \"" + theme + "\" }";
            Assert.Equal(theme, Theme3D.ThemeFromConfigJson(json));
        }

        [Fact]
        public void GespeicherterStil_WirdNormalisiert()
        {
            Assert.Equal(Theme3D.Shine, Theme3D.ThemeFromConfigJson("{ \"UiTheme\": \"  SHINE \" }"));
            Assert.Equal(Theme3D.ThreeD, Theme3D.ThemeFromConfigJson("{ \"UiTheme\": \"unbekannt\" }"));
        }

        [Fact]
        public void KaputteConfig_StartetIn3D()
        {
            Assert.Equal(Theme3D.ThreeD, Theme3D.ThemeFromConfigJson("{ \"UiTheme\": "));
            Assert.Equal(Theme3D.ThreeD, Theme3D.ThemeFromConfigJson("kein json"));
            Assert.Equal(Theme3D.ThreeD, Theme3D.ThemeFromConfigJson("{ \"UiTheme\": 42 }"));
        }
    }
}

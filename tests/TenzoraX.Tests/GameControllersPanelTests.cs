using TenzoraX;
using TenzoraX.Detection;
using Xunit;

namespace TenzoraX.Tests
{
    public class GameControllersPanelTests
    {
        [Theory]
        [InlineData("#32770", "Gamecontroller", true)]
        [InlineData("#32770", "Game Controllers", true)]
        [InlineData("#32770", "  Gamecontroller  ", true)]
        [InlineData("#32770", "gamecontroller", true)]
        [InlineData("#32770", "Sound", false)]
        [InlineData("#32770", "", false)]
        [InlineData("#32769", "Gamecontroller", false)]
        public void Erkennt_nur_das_echte_Panel(string className, string title, bool expected)
        {
            Assert.Equal(expected, GameControllersPanel.IsPanelWindow(className, title));
        }

        [Fact]
        public void Wartet_wenn_die_Geräteliste_gerade_erst_gewechselt_hat()
        {
            // Vor 300 ms ein Reconnect, noch keine Wartezeit verstrichen → weiter warten.
            Assert.True(GameControllersPanel.ShouldWaitForQuiet(sinceChangeMs: 300, waitedMs: 0));
        }

        [Fact]
        public void Oeffnet_wenn_die_Liste_lange_genug_ruhig_ist()
        {
            // Letzte Änderung vor 2 s, Ruhefenster 1,5 s → öffnen.
            Assert.False(GameControllersPanel.ShouldWaitForQuiet(sinceChangeMs: 2000, waitedMs: 0));
        }

        [Fact]
        public void Gibt_nach_der_hoechstens_Wartezeit_auf()
        {
            // Änderung ist noch frisch, aber die Obergrenze ist erreicht → nicht weiter warten.
            Assert.False(GameControllersPanel.ShouldWaitForQuiet(sinceChangeMs: 100, waitedMs: 6000));
        }

        [Fact]
        public void Wartet_kurz_vor_dem_Ruhefenster_nicht_mehr_lange()
        {
            // Genau an der Grenze des Ruhefensters gilt die Liste als ruhig.
            Assert.False(GameControllersPanel.ShouldWaitForQuiet(sinceChangeMs: 1500, waitedMs: 0));
        }

        [Fact]
        public void Beendet_ein_unsichtbares_Panel_ohne_andere_Fenster()
        {
            Assert.True(GameControllersPanel.ShouldTerminateHungPanel(
                windowVisible: false, responsive: true, hasOtherVisibleWindows: false));
        }

        [Fact]
        public void Beendet_ein_sichtbares_aber_nicht_antwortendes_Panel()
        {
            Assert.True(GameControllersPanel.ShouldTerminateHungPanel(
                windowVisible: true, responsive: false, hasOtherVisibleWindows: false));
        }

        [Fact]
        public void Laesst_ein_sichtbares_antwortendes_Panel_stehen()
        {
            Assert.False(GameControllersPanel.ShouldTerminateHungPanel(
                windowVisible: true, responsive: true, hasOtherVisibleWindows: false));
        }

        [Fact]
        public void Beendet_keinen_Prozess_mit_anderen_sichtbaren_Fenstern()
        {
            Assert.False(GameControllersPanel.ShouldTerminateHungPanel(
                windowVisible: false, responsive: false, hasOtherVisibleWindows: true));
        }

        [Fact]
        public void Waechter_ersetzt_ein_verstecktes_RunDLL_Panel_nach_zehn_Sekunden()
        {
            Assert.True(GameControllersPanel.ShouldRecover(
                hasPanel: true, visible: false, responsive: false, unresponsiveTicks: 5, ageMs: 10000, recoveriesInWindow: 0));
        }

        [Fact]
        public void Waechter_beendet_ein_sichtbares_Panel_nie()
        {
            // joy.cpl antwortet beim Listenaufbau minutenlang nicht; das Fenster gehört
            // dem Nutzer und wird nicht abgeschossen (sonst Flackern wie ein Absturz).
            Assert.False(GameControllersPanel.ShouldRecover(
                hasPanel: true, visible: true, responsive: false, unresponsiveTicks: 12, ageMs: 30000, recoveriesInWindow: 0));
            Assert.False(GameControllersPanel.ShouldRecover(
                hasPanel: true, visible: true, responsive: false, unresponsiveTicks: 60, ageMs: 120000, recoveriesInWindow: 0));
        }

        [Fact]
        public void Waechter_laesst_ein_antwortendes_Panel_stehen()
        {
            Assert.False(GameControllersPanel.ShouldRecover(
                hasPanel: true, visible: true, responsive: true, unresponsiveTicks: 12, ageMs: 40000, recoveriesInWindow: 0));
        }

        [Fact]
        public void Waechter_wartet_beim_versteckten_Panel_die_Schonzeit_ab()
        {
            Assert.False(GameControllersPanel.ShouldRecover(
                hasPanel: true, visible: false, responsive: false, unresponsiveTicks: 5, ageMs: 9999, recoveriesInWindow: 0));
            Assert.False(GameControllersPanel.ShouldRecover(
                hasPanel: true, visible: false, responsive: false, unresponsiveTicks: 4, ageMs: 12000, recoveriesInWindow: 0));
        }

        [Fact]
        public void Waechter_hoert_nach_drei_Versuchen_auf()
        {
            Assert.False(GameControllersPanel.ShouldRecover(
                hasPanel: true, visible: false, responsive: false, unresponsiveTicks: 6, ageMs: 12000, recoveriesInWindow: 3));
        }

        [Fact]
        public void Waechter_ohne_Panel_tut_nichts()
        {
            Assert.False(GameControllersPanel.ShouldRecover(
                hasPanel: false, visible: true, responsive: false, unresponsiveTicks: 12, ageMs: 40000, recoveriesInWindow: 0));
        }

        [Fact]
        public void Panel_startet_nicht_waehrend_eines_frischen_DirectInput_Staus()
        {
            // Letzte Aufzählung brauchte 50 s und endete vor 5 s: joy.cpl würde hängen.
            Assert.True(DirectInputControllerProvider.ShouldRefuseForStall(lastCallMs: 50400, msSinceLastCall: 5000));
        }

        [Fact]
        public void Panel_startet_nach_einer_schnellen_Aufzaehlung()
        {
            Assert.False(DirectInputControllerProvider.ShouldRefuseForStall(lastCallMs: 242, msSinceLastCall: 300));
        }

        [Fact]
        public void Panel_startet_wieder_wenn_der_Stau_lange_zurueckliegt()
        {
            Assert.False(DirectInputControllerProvider.ShouldRefuseForStall(lastCallMs: 50400, msSinceLastCall: 181000));
        }

        [Fact]
        public void Panel_startet_beim_ersten_Versuch_ohne_Messung()
        {
            Assert.False(DirectInputControllerProvider.ShouldRefuseForStall(lastCallMs: 0, msSinceLastCall: double.MaxValue));
        }
    }
}

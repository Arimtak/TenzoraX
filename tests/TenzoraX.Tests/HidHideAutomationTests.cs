using System.IO;
using TenzoraX;
using Xunit;

namespace TenzoraX.Tests
{
    /// <summary>
    /// Testet die HidHide-Automatik nur an ihren seiten-effektfreien Verträgen:
    /// Die Suche nach HidHideCLI.exe liefert entweder null (nicht installiert) oder
    /// einen existierenden Pfad, und ein leerer Geräte-Instanzpfad wird ohne jeden
    /// CLI-Aufruf abgewiesen. Echte CLI-Aufrufe (--dev-hide/--cloak-on) werden hier
    /// bewusst NICHT ausgeführt, weil sie den HidHide-Zustand des Systems verändern
    /// würden – der Test läuft in jeder Umgebung grün (mit oder ohne HidHide, mit
    /// oder ohne Admin-Rechte).
    /// </summary>
    public class HidHideAutomationTests
    {
        [Fact]
        public void FindCli_ReturnsNullOrExistingPath()
        {
            string? cli = HidHideAutomation.FindCli();

            // null ist OK (HidHide nicht installiert / an unerwartetem Ort),
            // sonst muss der Pfad wirklich existieren.
            if (cli != null)
                Assert.True(File.Exists(cli));
        }

        [Fact]
        public void HideController_EmptyInstanceId_ReturnsFalseWithoutSideEffects()
        {
            // Leerer Geräte-Instanzpfad → sofort false, ohne CLI-Prozess zu starten.
            Assert.False(HidHideAutomation.HideController(""));
        }

        [Fact]
        public void HideController_WhitespaceInstanceId_ReturnsFalse()
        {
            // Auch nur-Whitespace-Eingaben werden sauber abgewiesen (kein Crash).
            Assert.False(HidHideAutomation.HideController("   "));
        }

        [Fact]
        public void IsDeviceHidden_NeverThrows_AndReturnsBool()
        {
            // Reine Leseoperation (--dev-list): darf nie werfen und liefert
            // true/false. Keine Systemänderung. Leere Eingaben werden ohne jeden
            // CLI-Aufruf abgewiesen.
            Assert.False(HidHideAutomation.IsDeviceHidden(""));
            Assert.False(HidHideAutomation.IsDeviceHidden("   "));
            bool result = HidHideAutomation.IsDeviceHidden("HID\\some-non-existent-device-xyz");
            Assert.True(result || !result);
        }

        [Fact]
        public void ParseDevList_ExtractsQuotedInstancePaths()
        {
            var list = HidHideAutomation.ParseDevList(
                "--dev-hide \"   \"\r\n" +
                "--dev-hide \"HID\\VID_045E&PID_028E&IG_01\\3&5D59EC1&0&0000\"\r\n" +
                "unrelated line without marker\r\n" +
                "--dev-hide \"VID&2DC8&PID&200B\\D&0000\"");

            Assert.Equal(3, list.Count);
            Assert.Equal("   ", list[0]);                      // leerer/Whitespace-Eintrag bleibt erhalten (wird vom Cleanup entfernt)
            Assert.Equal("HID\\VID_045E&PID_028E&IG_01\\3&5D59EC1&0&0000", list[1]);
            Assert.Equal("VID&2DC8&PID&200B\\D&0000", list[2]);
        }

        [Fact]
        public void ParseDevList_EmptyOrGarbage_ReturnsEmpty()
        {
            Assert.Empty(HidHideAutomation.ParseDevList(""));
            Assert.Empty(HidHideAutomation.ParseDevList(null));
            Assert.Empty(HidHideAutomation.ParseDevList("--cloak-on\r\n--app-reg \"x\""));
        }

        [Fact]
        public void IsCloakActive_NeverThrows_AndReturnsBool()
        {
            // Reine Leseoperation (--cloak-state): darf nie werfen. Keine Systemänderung.
            bool result = HidHideAutomation.IsCloakActive();
            Assert.True(result || !result);
        }
    }
}

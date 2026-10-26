using Xunit;

namespace TenzoraX.Tests
{
    /// <summary>
    /// Die Auswahlliste zeigte ein Pad, dessen Windows-Schnittstelle deaktiviert war, wie
    /// einen normalen Controller – es fehlte aber im Windows-Panel und bei Steam (gemessen
    /// 26.10.2026). Solche Einträge müssen sich in der Liste zu erkennen geben.
    /// </summary>
    public class ControllerListEntryTests
    {
        private static DetectedController Ctrl(
            string name = "Ultimate 2",
            string connectionType = "USB",
            bool accessError = false,
            bool disabled = false)
        {
            return new DetectedController
            {
                Name = name,
                ConnectionType = connectionType,
                AccessError = accessError,
                WindowsInterfaceDisabled = disabled,
                Connected = true
            };
        }

        [Fact]
        public void NormalController_ShowsNameAndConnection()
        {
            Assert.Equal("Ultimate 2 (USB)", ControllerManager.FormatControllerListEntry(Ctrl()));
        }

        [Fact]
        public void AccessError_GetsWarningMark()
        {
            Assert.Equal("Ultimate 2 (USB) ⚠", ControllerManager.FormatControllerListEntry(Ctrl(accessError: true)));
        }

        [Fact]
        public void DisabledWindowsInterface_IsNamedAsDisabled()
        {
            string entry = ControllerManager.FormatControllerListEntry(Ctrl(accessError: true, disabled: true));

            Assert.Contains("deaktiviert (Windows)", entry);
            Assert.DoesNotContain("⚠", entry);
        }

        [Fact]
        public void BluetoothController_KeepsItsTransport()
        {
            Assert.Equal("Ultimate 2 (Bluetooth)", ControllerManager.FormatControllerListEntry(Ctrl(connectionType: "Bluetooth")));
        }
    }
}

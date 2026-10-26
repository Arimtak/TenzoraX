using TenzoraX.Detection;
using Xunit;

namespace TenzoraX.Tests
{
    /// <summary>
    /// Windows meldet für XInput-Pads nur den Treibernamen ("XINPUT-kompatibles
    /// HID-Gerät"). In der Oberfläche soll stattdessen ein verständlicher Name stehen –
    /// echte Hersteller-Produktnamen dürfen dabei nicht überschrieben werden.
    /// </summary>
    public class ControllerDisplayNameTests
    {
        [Fact]
        public void XboxOne_WithDriverPlaceholder_BecomesXboxController()
        {
            string name = HidControllerProvider.GetControllerDisplayName(
                "XINPUT-kompatibles HID-Gerät", "XINPUT-kompatibles HID-Gerät", 0x045E, 0x02FF);
            Assert.Equal("Xbox-Controller", name);
        }

        [Fact]
        public void Xbox360_WithDriverPlaceholder_BecomesXbox360Controller()
        {
            string name = HidControllerProvider.GetControllerDisplayName(
                "XINPUT compatible HID device", "XINPUT compatible HID device", 0x045E, 0x028E);
            Assert.Equal("Xbox 360 Controller", name);
        }

        [Fact]
        public void Xbox_WithoutAnyName_BecomesXboxController()
        {
            string name = HidControllerProvider.GetControllerDisplayName("", "", 0x045E, 0x0B13);
            Assert.Equal("Xbox-Controller", name);
        }

        [Fact]
        public void RealProductName_IsNeverOverwritten()
        {
            string name = HidControllerProvider.GetControllerDisplayName(
                "Xbox Elite Wireless Controller Series 2", "Xbox Elite Wireless Controller Series 2", 0x045E, 0x0B22);
            Assert.Equal("Xbox Elite Wireless Controller Series 2", name);
        }

        [Fact]
        public void DualShock4_WithWirelessControllerPlaceholder_BecomesDualShock4()
        {
            string name = HidControllerProvider.GetControllerDisplayName(
                "Wireless Controller", "Wireless Controller", 0x054C, 0x09CC);
            Assert.Equal("DualShock 4", name);
        }

        [Fact]
        public void SwitchPro_WithDriverPlaceholder_BecomesSwitchProController()
        {
            string name = HidControllerProvider.GetControllerDisplayName(
                "HID-compliant game controller", "HID-compliant game controller", 0x057E, 0x2009);
            Assert.Equal("Switch Pro Controller", name);
        }

        [Fact]
        public void UnknownSonyPad_GetsNeutralPlayStationName()
        {
            string name = HidControllerProvider.GetControllerDisplayName("", "", 0x054C, 0x0BAA);
            Assert.Equal("PlayStation-Controller", name);
        }

        [Fact]
        public void OtherVendors_KeepTheirProductName()
        {
            string name = HidControllerProvider.GetControllerDisplayName(
                "8BitDo Ultimate 2C", "Wireless Gamepad", 0x2DC8, 0x200B);
            Assert.Equal("8BitDo Ultimate 2C", name);
        }

        [Fact]
        public void OtherVendor_WithoutName_FallsBackToVendorGamepad()
        {
            string name = HidControllerProvider.GetControllerDisplayName("", "", 0x2DC8, 0x200B);
            Assert.Equal("8BitDo Gamepad", name);
        }
    }
}

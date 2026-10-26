using TenzoraX.Detection;
using Xunit;

namespace TenzoraX.Tests
{
    /// <summary>
    /// Die Hardware-ID eines Geräts ist ein REG_MULTI_SZ. Wurde nur der erste Eintrag
    /// gelesen, blieben die Gamepad-Marker unsichtbar und echte Pads (z.B.
    /// "XINPUT-kompatibles HID-Gerät") wurden verworfen – sie tauchten dann nur noch
    /// über DirectInput ohne Eingabekanal auf.
    /// </summary>
    public class HidHardwareIdTests
    {
        [Fact]
        public void XboxPad_MarkerInZweiterHardwareId_WirdErkannt()
        {
            const string ids = "HID\\VID_045E&PID_02FF&IG_00|HID\\VID_045E&UP:0001_U:0005"
                               + "|HID_DEVICE_SYSTEM_GAME|HID_DEVICE_UP:0001_U:0005";
            Assert.True(HidControllerProvider.MentionsGamepadUsage(ids, includeJoystickUsage: true));
            Assert.True(HidControllerProvider.MentionsGamepadUsage(ids, includeJoystickUsage: false));
        }

        [Fact]
        public void JoystickUsage_ZaehltNurMitJoystickOption()
        {
            const string ids = "HID\\VID_2DC8&PID_200B|HID\\VID_2DC8&UP:0001_U:0004";
            Assert.True(HidControllerProvider.MentionsGamepadUsage(ids, includeJoystickUsage: true));
            Assert.False(HidControllerProvider.MentionsGamepadUsage(ids, includeJoystickUsage: false));
        }

        [Theory]
        [InlineData("")]
        [InlineData("HID\\VID_1A2C&PID_7B81&MI_01&COL02")]
        [InlineData("HID\\VID_25A7&PID_FA61&REV_0691&MI_01&Col04")]
        public void TastaturUndMaus_WerdenNichtErkannt(string ids)
        {
            Assert.False(HidControllerProvider.MentionsGamepadUsage(ids, includeJoystickUsage: true));
        }
    }
}

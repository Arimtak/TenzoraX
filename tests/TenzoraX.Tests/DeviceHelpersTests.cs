using TenzoraX.Detection;
using Xunit;

namespace TenzoraX.Tests
{
    public class DeviceHelpersTests
    {
        [Theory]
        [InlineData("USB\\VID_045E&PID_02FD\\6&2A3B4C5D&0&0000", 0x045E, 0x02FD)]
        [InlineData("HID\\VID_2DC8&PID_2013&COL01\\8&1A2B3C4D", 0x2DC8, 0x2013)]
        [InlineData("vid_054c&pid_05c4", 0x054C, 0x05C4)]          // Kleinbuchstaben
        [InlineData("VID_045E PID_02FD", 0x045E, 0x02FD)]           // ohne &
        // Bluetooth-GATT-Format (Namespace-Ziffer + VID/PID)
        [InlineData("HID\\{00001812-0000-1000-8000-00805F9B34FB}_DEV_VID&022DC8_PID&200B_REV&0001_E417D89E6208\\D&EFCDE22&0&0000", 0x2DC8, 0x200B)]
        public void TryParseVidPid_ValidStrings(string text, ushort expectedVid, ushort expectedPid)
        {
            Assert.True(DeviceHelpers.TryParseVidPid(text, out ushort vid, out ushort pid));
            Assert.Equal(expectedVid, vid);
            Assert.Equal(expectedPid, pid);
        }

        [Theory]
        [InlineData("")]
        [InlineData("HID\\DEVICE\\SOMETHING")]                       // kein VID/PID
        [InlineData("VID_XXXX&PID_YYYY")]                            // keine Hexzahl
        [InlineData("VID_0000&PID_0000")]                            // beides null ⇒ ungültig
        public void TryParseVidPid_InvalidStrings(string text)
        {
            Assert.False(DeviceHelpers.TryParseVidPid(text, out _, out _));
        }

        [Fact]
        public void InstanceIdFromDevicePath_HandlesBluetoothGuidInPath()
        {
            // Bluetooth-Geräte tragen die GATT-Dienst-GUID IN der Instanz-ID;
            // nur das LETZTE "#{…}" ist die Interface-GUID.
            string path = "\\\\?\\HID#{00001812-0000-1000-8000-00805f9b34fb}_Dev_VID&022dc8_PID&200b_REV&0001_e417d89e6208#d&efcde22&0&0000#{4d1e55b2-f16f-11cf-88cb-001111000030}";
            string expected = "HID\\{00001812-0000-1000-8000-00805f9b34fb}_Dev_VID&022dc8_PID&200b_REV&0001_e417d89e6208\\d&efcde22&0&0000";
            Assert.Equal(expected, RawInputControllerProvider.InstanceIdFromDevicePath(path));
        }

        [Theory]
        [InlineData(0x045Eu, 0x02FDu, "VID_045E&PID_02FD")]
        [InlineData(0u, 1u, "VID_0000&PID_0001")]
        public void FormatVidPid_FormatsUppercase(ushort vid, ushort pid, string expected)
        {
            Assert.Equal(expected, DeviceHelpers.FormatVidPid(vid, pid));
        }

        [Theory]
        [InlineData("HID-compliant game controller", true)]
        [InlineData("Wireless Gamepad F710", true)]
        [InlineData("Spielsteuerung", true)]
        [InlineData("Logitech G29 Driving Force", false)]
        [InlineData("", false)]
        public void LooksLikeGenericGamepad_DetectsGenericNames(string text, bool expected)
        {
            Assert.Equal(expected, DeviceHelpers.LooksLikeGenericGamepad(text));
        }

        [Theory]
        [InlineData(0x2DC8u, "8BitDo")]
        [InlineData(0x045Eu, "Microsoft")]
        [InlineData(0x1234u, "")]                                    // unbekannt
        public void GetKnownVendorName_MapsKnownVendors(ushort vid, string expected)
        {
            Assert.Equal(expected, DeviceHelpers.GetKnownVendorName(vid));
        }
    }
}

using System.Collections.Generic;
using TenzoraX.Detection;
using Xunit;

namespace TenzoraX.Tests
{
    /// <summary>
    /// Raw Input meldet zu einem Gerät nur VID/PID. Die Zusatz-Erkennung soll den
    /// echten Produktnamen der physischen Instanz übernehmen, statt „VID_…&amp;PID_…“
    /// in die Controller-Liste zu schreiben.
    /// </summary>
    public class AdditionalDeviceNameTests
    {
        private static DetectedDeviceInfo Finding(
            ControllerApi api = ControllerApi.RawInput,
            string name = "",
            string vidPid = "VID_2DC8&PID_3024",
            string instanceId = @"HID\VID_2DC8&PID_3024\9&8581B18&0&0000",
            ushort vid = 0x2DC8,
            ushort pid = 0x3024)
        {
            return new DetectedDeviceInfo
            {
                Api = api,
                Name = name,
                VidPid = vidPid,
                DeviceInstanceId = instanceId,
                Vid = vid,
                Pid = pid
            };
        }

        [Fact]
        public void RawInputWithoutName_TakesProductNameOfPhysicalInstance()
        {
            var finding = Finding();
            string name = ControllerManager.ResolveAdditionalDisplayName(
                finding, new List<DetectedDeviceInfo> { finding }, new List<(string, string)>(),
                (_, _, _) => "8BitDo Ultimate Wireless Controller");
            Assert.Equal("8BitDo Ultimate Wireless Controller", name);
        }

        [Fact]
        public void WindowsPlaceholderName_IsNotUsed()
        {
            var finding = Finding(vid: 0x045E, pid: 0x02FF, vidPid: "VID_045E&PID_02FF");
            string name = ControllerManager.ResolveAdditionalDisplayName(
                finding, new List<DetectedDeviceInfo> { finding }, new List<(string, string)>(),
                (_, _, _) => "XINPUT-kompatibles HID-Gerät");
            // Platzhalter verworfen, danach greift der bekannte Controllername bzw. die VID/PID.
            Assert.NotEqual("XINPUT-kompatibles HID-Gerät", name);
        }

        [Fact]
        public void GenericOwnName_LosesAgainstRealWindowsName()
        {
            var finding = Finding(name: "USB Gamepad");
            string name = ControllerManager.ResolveAdditionalDisplayName(
                finding, new List<DetectedDeviceInfo> { finding }, new List<(string, string)>(),
                (_, _, _) => "8BitDo Ultimate Wireless Controller");
            Assert.Equal("8BitDo Ultimate Wireless Controller", name);
        }

        [Fact]
        public void RealOwnName_IsKeptWithoutAskingWindows()
        {
            var finding = Finding(name: "Xbox Elite Wireless Controller Series 2", vid: 0x045E, pid: 0x0B22);
            bool asked = false;
            string name = ControllerManager.ResolveAdditionalDisplayName(
                finding, new List<DetectedDeviceInfo> { finding }, new List<(string, string)>(),
                (_, _, _) => { asked = true; return "sollte nicht gefragt werden"; });
            Assert.Equal("Xbox Elite Wireless Controller Series 2", name);
            Assert.False(asked);
        }

        [Fact]
        public void HidProductName_WinsOverWindowsDeviceTree()
        {
            // Beobachtet am 26.10.2026: für ein Bluetooth-8BitDo nennt Windows im
            // Gerätebaum nur „Bluetooth-HID-Gerät“; der echte Produktname steht allein
            // im HID-Descriptor. Der darf nicht von der Baum-Auflösung verdrängt werden.
            var finding = Finding(vid: 0x2DC8, pid: 0x202F, vidPid: "VID_2DC8&PID_202F");
            bool askedWindows = false;
            string name = ControllerManager.ResolveAdditionalDisplayName(
                finding, new List<DetectedDeviceInfo> { finding }, new List<(string, string)>(),
                (_, _, _) => { askedWindows = true; return "8BitDo Gamepad"; },
                hidProductName: "8BitDo Ultimate 2C Wireless Controller");
            Assert.Equal("8BitDo Ultimate 2C Wireless Controller", name);
            Assert.False(askedWindows);
        }

        [Fact]
        public void GenericHidProductName_FallsThroughToWindowsName()
        {
            var finding = Finding();
            string name = ControllerManager.ResolveAdditionalDisplayName(
                finding, new List<DetectedDeviceInfo> { finding }, new List<(string, string)>(),
                (_, _, _) => "8BitDo Ultimate Wireless Controller",
                hidProductName: "USB Gamepad");
            Assert.Equal("8BitDo Ultimate Wireless Controller", name);
        }

        [Fact]
        public void SameVidPidFromDirectInput_NamesTheRawInputEntry()
        {
            var raw = Finding();
            var directInput = Finding(api: ControllerApi.DirectInput, name: "8BitDo Ultimate Controller");
            string name = ControllerManager.ResolveAdditionalDisplayName(
                raw, new List<DetectedDeviceInfo> { raw, directInput }, new List<(string, string)>(),
                (_, _, _) => "");
            Assert.Equal("8BitDo Ultimate Controller", name);
        }

        [Fact]
        public void KnownPipelineDevice_NamesSameInstanceIgnoringCase()
        {
            var raw = Finding(instanceId: @"HID\VID_2DC8&PID_3024\9&8581b18&0&0000");
            var pipeline = new List<(string Name, string InstanceId)>
            {
                ("8BitDo Ultimate Controller", @"hid\vid_2dc8&pid_3024\9&8581B18&0&0000")
            };
            string name = ControllerManager.ResolveAdditionalDisplayName(
                raw, new List<DetectedDeviceInfo> { raw }, pipeline, (_, _, _) => "");
            Assert.Equal("8BitDo Ultimate Controller", name);
        }

        [Fact]
        public void NothingFound_FallsBackToVidPid()
        {
            var finding = Finding(vid: 0x1234, pid: 0x5678, vidPid: "VID_1234&PID_5678");
            string name = ControllerManager.ResolveAdditionalDisplayName(
                finding, new List<DetectedDeviceInfo> { finding }, new List<(string, string)>(),
                (_, _, _) => "");
            Assert.Equal("VID_1234&PID_5678", name);
        }

        [Fact]
        public void VendorFallback_IsPreferredOverVidPid()
        {
            // 8BitDo (0x2DC8) ist bekannt, der Name also „8BitDo Gamepad“ statt VID/PID.
            var finding = Finding();
            string name = ControllerManager.ResolveAdditionalDisplayName(
                finding, new List<DetectedDeviceInfo> { finding }, new List<(string, string)>(),
                (instanceId, vid, pid) => DeviceNameResolver.PickProductName("", "", null, vid, pid));
            Assert.Equal("8BitDo Gamepad", name);
        }

        [Theory]
        [InlineData("8BitDo Ultimate Wireless Controller", true)]
        [InlineData("Xbox Wireless Controller", true)]
        [InlineData("VID_2DC8&PID_3024", false)]
        [InlineData("USB Gamepad", false)]
        [InlineData("HID-konformes Benutzersteuergerät", false)]
        [InlineData("XINPUT-kompatibles HID-Gerät", false)]
        [InlineData("Wireless Controller", false)]
        [InlineData("Generischer USB-Hub", false)]
        [InlineData("USB-Verbundgerät", false)]
        [InlineData("Geräteerkennungsdienst", false)]
        [InlineData("", false)]
        public void IsRealProductName_ClassifiesWindowsNames(string text, bool expected)
        {
            Assert.Equal(expected, DeviceNameResolver.IsRealProductName(text));
        }

        [Fact]
        public void PickProductName_UsesParentName_WhenOwnNameIsDriverPlaceholder()
        {
            var ancestors = new List<(string Friendly, string Desc)>
            {
                ("8BitDo Ultimate Wireless Controller", "USB-Eingabegerät")
            };
            string name = DeviceNameResolver.PickProductName(
                "XINPUT-kompatibles HID-Gerät", "XINPUT-kompatibles HID-Gerät", ancestors, 0x2DC8, 0x3024);
            Assert.Equal("8BitDo Ultimate Wireless Controller", name);
        }

        [Fact]
        public void PickProductName_PrefersOwnRealName()
        {
            var ancestors = new List<(string Friendly, string Desc)>
            {
                ("Irgendein Elternknoten", "")
            };
            string name = DeviceNameResolver.PickProductName(
                "8BitDo Ultimate Wireless Controller", "USB-Eingabegerät", ancestors, 0x2DC8, 0x3024);
            Assert.Equal("8BitDo Ultimate Wireless Controller", name);
        }

        [Fact]
        public void PickProductName_IgnoresInfrastructureAncestors()
        {
            // Am 26.10.2026 mit echten Namen gemessen: ohne diesen Filter hieß ein
            // 8BitDo-Pad nach dem Aufstieg im Gerätebaum „Generischer USB-Hub“.
            var ancestors = new List<(string Friendly, string Desc)>
            {
                ("USB-Eingabegerät", "USB-Eingabegerät"),
                ("Generischer USB-Hub", "Generischer USB-Hub")
            };
            string name = DeviceNameResolver.PickProductName(
                "HID-konformes, vom Hersteller definiertes Gerät", "", ancestors, 0x2DC8, 0x6013);
            Assert.Equal("8BitDo Gamepad", name);
        }

        [Fact]
        public void PickProductName_FallsBackToKnownControllerName()
        {
            string name = DeviceNameResolver.PickProductName(
                "XINPUT-kompatibles HID-Gerät", "XINPUT-kompatibles HID-Gerät", null, 0x045E, 0x0B13);
            Assert.Equal("Xbox-Controller", name);
        }
    }
}

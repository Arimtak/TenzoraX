using System.Collections.Generic;
using TenzoraX.Detection;
using Xunit;

namespace TenzoraX.Tests
{
    public class DetectionMergeTests
    {
        private static DetectedDeviceInfo Dev(
            ControllerApi api = ControllerApi.Hid,
            string name = "",
            string vidPid = "",
            string instanceId = "",
            string transport = "Unknown")
        {
            return new DetectedDeviceInfo
            {
                Api = api,
                Name = name,
                VidPid = vidPid,
                DeviceInstanceId = instanceId,
                Transport = transport
            };
        }

        [Fact]
        public void StableKey_PrefersInstanceId()
        {
            var d = Dev(vidPid: "VID_045E&PID_02FD", instanceId: "HID\\VID_045E&PID_02FD\\7&AA");
            Assert.Equal("IID:HID\\VID_045E&PID_02FD\\7&AA", DetectionMerge.StableKey(d));
        }

        [Fact]
        public void StableKey_FallsBackToVidPid()
        {
            var d = Dev(vidPid: "VID_054C&PID_05C4");
            Assert.Equal("VP:VID_054C&PID_05C4", DetectionMerge.StableKey(d));
        }

        [Fact]
        public void SamePhysicalDevice_SameInstanceId_IgnoresCase()
        {
            var a = Dev(instanceId: "hid\\vid_2dc8&pid_2013\\a&1");
            var b = Dev(instanceId: "HID\\VID_2DC8&PID_2013\\A&1");
            Assert.True(DetectionMerge.SamePhysicalDevice(a, b));
        }

        [Fact]
        public void SamePhysicalDevice_TwoUnitsSameModel_AreNotMerged()
        {
            // Zwei physisch vorhandene Exemplare desselben Modells (gleiche VID/PID,
            // unterschiedliche Instance-IDs) dürfen NICHT zusammengeführt werden.
            var a = Dev(vidPid: "VID_045E&PID_02FD", instanceId: "HID\\...\\COL01");
            var b = Dev(vidPid: "VID_045E&PID_02FD", instanceId: "HID\\...\\COL02");
            Assert.False(DetectionMerge.SamePhysicalDevice(a, b));
        }

        [Fact]
        public void SamePhysicalDevice_VidPidWithoutInstance_Matches()
        {
            var hid = Dev(vidPid: "VID_045E&PID_02FD", instanceId: "HID\\XINPUT_DEV");
            var di = Dev(api: ControllerApi.DirectInput, vidPid: "VID_045E&PID_02FD");   // DI hat keine IID
            Assert.True(DetectionMerge.SamePhysicalDevice(hid, di));
        }

        [Fact]
        public void SamePhysicalDevice_DifferentVidPid_NeverMatches()
        {
            var a = Dev(instanceId: "HID\\AAA");
            var b = Dev(instanceId: "HID\\BBB", vidPid: "VID_1111&PID_2222");
            Assert.False(DetectionMerge.SamePhysicalDevice(a, b));
        }

        [Fact]
        public void MergeInto_AddsNewAndEnrichesExisting()
        {
            var target = new List<DetectedDeviceInfo>();
            var hid = Dev(ControllerApi.Hid, vidPid: "VID_2DC8&PID_2013", instanceId: "HID\\BT_PAD",
                          transport: "Bluetooth");
            DetectionMerge.MergeInto(target, new[] { hid });
            Assert.Single(target);
            Assert.Contains(ControllerApi.Hid, target[0].SeenApis);

            var ri = Dev(ControllerApi.RawInput, name: "8BitDo Ultimate", vidPid: "VID_2DC8&PID_2013");
            DetectionMerge.MergeInto(target, new[] { ri });

            Assert.Single(target);                                   // nicht doppelt gelistet
            Assert.Equal("8BitDo Ultimate", target[0].Name);         // Name angereichert
            Assert.Equal("Bluetooth", target[0].Transport);          // Transport bleibt erhalten
            Assert.Equal(2, target[0].SeenApis.Count);               // HID + Raw Input gesehen
        }

        [Theory]
        [InlineData("BTHENUM\\DEV_ABC", "Bluetooth")]
        [InlineData("BTHLE\\DEV_ABC", "Bluetooth")]
        [InlineData("USB\\VID_045E&PID_02FD\\X", "USB")]
        [InlineData("", "")]
        [InlineData("ACPI\\PNP0501", "")]
        public void TransportFromParentInstance_ClassifiesPrefixes(string instanceId, string expected)
        {
            Assert.Equal(expected, DetectionMerge.TransportFromParentInstance(instanceId));
        }
    }
}

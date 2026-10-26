using System.Collections.Generic;
using TenzoraX.Detection;
using Xunit;

namespace TenzoraX.Tests
{
    /// <summary>
    /// Der Banner-Knopf aktiviert eine deaktivierte Windows-Schnittstelle per
    /// Enable-PnpDevice. Falsch gequotet würde der Befehl an der Instanz-ID mit
    /// „&amp;" scheitern – deshalb sind die Argumente hier festgenagelt.
    /// </summary>
    public class DeviceInterfaceRecoveryTests
    {
        [Fact]
        public void Arguments_NameTheInterfaceAndSkipConfirmation()
        {
            string args = DeviceInterfaceRecovery.BuildEnableArguments(@"HID\VID_045E&PID_02FF&IG_00\9&8581B18&0&0000");

            Assert.Contains("Enable-PnpDevice", args);
            Assert.Contains("-InstanceId 'HID\\VID_045E&PID_02FF&IG_00\\9&8581B18&0&0000'", args);
            Assert.Contains("-Confirm:$false", args);
        }

        [Fact]
        public void Arguments_QuoteEmbeddedSingleQuotes()
        {
            string args = DeviceInterfaceRecovery.BuildEnableArguments("HID\\TEST'X");

            Assert.Contains("'HID\\TEST''X'", args);
        }

        [Fact]
        public void WithoutInstance_ReportsMissingInterface()
        {
            var (success, message) = DeviceInterfaceRecovery.Enable("");

            Assert.False(success);
            Assert.Contains("Keine deaktivierte Schnittstelle", message);
        }

        [Fact]
        public void RestartTarget_Prefers_MI_01_And_Skips_Hid_Collections()
        {
            var nodes = new List<PhysicalDeviceResolver.DeviceStateInfo>
            {
                new() { InstanceId = @"HID\VID_1A2C&PID_7B81&MI_01&COL03\B&33D9AD0&0&0002",
                        FriendlyName = "HID-konformes, vom Hersteller definiertes Gerät" },
                new() { InstanceId = @"USB\VID_1A2C&PID_7B81&MI_00\A&1D13E031&0&0000",
                        FriendlyName = "USB-Eingabegerät" },
                new() { InstanceId = @"USB\VID_1A2C&PID_7B81&MI_01\A&1D13E031&0&0001",
                        FriendlyName = "USB-Eingabegerät" }
            };

            var targets = DeviceInterfaceRecovery.SelectRestartTargets(
                nodes, new[] { "VID_1A2C&PID_7B81" });

            Assert.Single(targets);
            Assert.Equal(@"USB\VID_1A2C&PID_7B81&MI_01\A&1D13E031&0&0001", targets[0]);
        }

        [Fact]
        public void RestartTarget_Leaves_Gamepad_Interfaces_Alone()
        {
            var nodes = new List<PhysicalDeviceResolver.DeviceStateInfo>
            {
                new() { InstanceId = @"USB\VID_045E&PID_02FF&MI_00\9&8581B18&0&0000",
                        FriendlyName = "XINPUT-kompatibles HID-Gerät" }
            };

            var targets = DeviceInterfaceRecovery.SelectRestartTargets(
                nodes, new[] { "VID_045E&PID_02FF" });

            Assert.Empty(targets);
        }

        [Fact]
        public void RestartTargets_One_Per_Blocking_Device()
        {
            var nodes = new List<PhysicalDeviceResolver.DeviceStateInfo>
            {
                new() { InstanceId = @"USB\VID_1A2C&PID_7B81&MI_01\A&1D13E031&0&0001", FriendlyName = "USB-Eingabegerät" },
                new() { InstanceId = @"USB\VID_25A7&PID_FA61&MI_01\9&2FC239B5&0&0001", FriendlyName = "USB-Eingabegerät" }
            };

            var targets = DeviceInterfaceRecovery.SelectRestartTargets(
                nodes, new[] { "VID_1A2C&PID_7B81", "VID_25A7&PID_FA61" });

            Assert.Equal(2, targets.Count);
        }
    }
}

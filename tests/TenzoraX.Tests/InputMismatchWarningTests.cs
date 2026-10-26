using System;
using System.Collections.Generic;
using TenzoraX.Detection;
using Xunit;

namespace TenzoraX.Tests
{
    /// <summary>
    /// Am 26.10.2026 stand ein 8BitDo-Pad in TenzoraX in der Liste, fehlte aber im
    /// Windows-Panel „Game Controller“ und bei Steam: Seine xinputhid-Schnittstelle war
    /// in Windows deaktiviert (Problemcode 22). XInput lieferte nichts, DirectInput
    /// führte nichts. Genau dieser Widerspruch soll sichtbar werden.
    /// </summary>
    public class InputMismatchWarningTests
    {
        [Fact]
        public void WithoutControllers_NoWarning()
        {
            Assert.Equal("", InputEndpoints.MismatchWarning(0, 0, 0, "XINPUT-kompatibles HID-Gerät"));
        }

        [Fact]
        public void WithOccupiedXInputSlot_NoWarning()
        {
            Assert.Equal("", InputEndpoints.MismatchWarning(1, 1, 0));
        }

        [Fact]
        public void WithDirectInputDevice_NoWarning()
        {
            Assert.Equal("", InputEndpoints.MismatchWarning(1, 0, 1));
        }

        [Fact]
        public void ListedButInvisibleToWindows_WarnsAndNamesTheDisabledInterface()
        {
            string warning = InputEndpoints.MismatchWarning(1, 0, 0, "XINPUT-kompatibles HID-Gerät");

            Assert.Contains("TenzoraX listet 1 Controller", warning);
            Assert.Contains("weder einen XInput-Slot noch ein DirectInput-Gerät", warning);
            Assert.Contains("XINPUT-kompatibles HID-Gerät", warning);
            Assert.Contains("deaktiviert", warning);
            Assert.Contains("Geräte-Manager", warning);
        }

        [Theory]
        [InlineData(0, 0, 0, false)]   // nichts gelistet → kein Widerspruch
        [InlineData(1, 1, 0, false)]   // XInput hat das Gerät
        [InlineData(1, 0, 1, false)]   // DirectInput hat es
        [InlineData(1, 0, 0, true)]    // App ja, Windows nein
        [InlineData(2, 0, 0, true)]
        public void IsMismatch_CoversTheFourCombinations(int listed, int xinput, int directInput, bool expected)
        {
            Assert.Equal(expected, InputEndpoints.IsMismatch(listed, xinput, directInput));
            Assert.Equal(expected, InputEndpoints.MismatchWarning(listed, xinput, directInput) != "");
        }

        [Fact]
        public void RecoveryByVidPid_PicksTheGamepadInterfaceOfTheSameDevice()
        {
            // Der Knopf soll auch ohne markierten Listeneintrag angeboten werden: gesucht
            // wird die abgeschaltete Schnittstelle desselben Geräts (VID/PID) – und unter
            // mehreren Treffern die Gamepad-Schnittstelle, nicht irgendeine HID-Sammlung.
            var nodes = new List<PhysicalDeviceResolver.DeviceStateInfo>
            {
                Disabled(@"HID\VID_2DC8&PID_310B&MI_02\B&2A1BAB8B&0&0000", "HID-konformes, vom Hersteller definiertes Gerät"),
                Disabled(@"HID\VID_2DC8&PID_310B&MI_00\B&15498063&0&0000", "XINPUT-kompatibles HID-Gerät"),
                Started(@"USB\VID_2DC8&PID_310B\82D57104CC", "USB-Verbundgerät")
            };

            var hit = InputEndpoints.SelectRecoveryInterface(nodes, new[] { "VID_2DC8&PID_310B" });

            Assert.NotNull(hit);
            Assert.Equal("XINPUT-kompatibles HID-Gerät", hit!.FriendlyName);
            Assert.Contains("MI_00", hit.InstanceId);
        }

        [Fact]
        public void RecoveryWithoutMatchingVidPid_FallsBackToTheGamepadInterface()
        {
            // Gemessenes Muster: Die XInput-Schnittstelle desselben Pads läuft unter
            // Microsofts VID (Xbox-Modus), das HID-Gamepad unter der Hersteller-VID. Ein
            // VID/PID-Vergleich findet die abgeschaltete Schnittstelle dann nicht.
            var nodes = new List<PhysicalDeviceResolver.DeviceStateInfo>
            {
                Disabled(@"HID\VID_045E&PID_02FF&IG_00\9&8581B18&0&0000", "XINPUT-kompatibles HID-Gerät"),
                Disabled(@"USB\VID_0BDA&PID_5411\A&1", "USB-Videokamera"),
                Started(@"USB\VID_2DC8&PID_310B\82D57104CC", "USB-Verbundgerät")
            };

            var hit = InputEndpoints.SelectRecoveryInterface(nodes, new[] { "VID_2DC8&PID_310B" });

            Assert.NotNull(hit);
            Assert.Equal("XINPUT-kompatibles HID-Gerät", hit!.FriendlyName);
        }

        [Fact]
        public void RecoveryWithoutGamepadInterface_OffersNothing()
        {
            // Lieber kein Knopf als einer, der ein fremdes Gerät einschaltet.
            var nodes = new List<PhysicalDeviceResolver.DeviceStateInfo>
            {
                Disabled(@"USB\VID_0BDA&PID_5411\A&1", "USB-Videokamera")
            };

            Assert.Null(InputEndpoints.SelectRecoveryInterface(nodes, new[] { "VID_2DC8&PID_310B" }));
            Assert.Null(InputEndpoints.SelectRecoveryInterface(nodes, Array.Empty<string>()));
            Assert.Null(InputEndpoints.SelectRecoveryInterface(null, new[] { "VID_2DC8&PID_310B" }));
        }

        [Fact]
        public void GamepadInterfaceNames_AreRecognised()
        {
            Assert.True(InputEndpoints.IsGamepadInterfaceName("XINPUT-kompatibles HID-Gerät"));
            Assert.True(InputEndpoints.IsGamepadInterfaceName("Xbox 360-Controller für Windows"));
            Assert.True(InputEndpoints.IsGamepadInterfaceName("HID-compliant game controller"));
            Assert.False(InputEndpoints.IsGamepadInterfaceName("USB-Videokamera"));
            Assert.False(InputEndpoints.IsGamepadInterfaceName(""));
            Assert.False(InputEndpoints.IsGamepadInterfaceName(null));
        }

        private static PhysicalDeviceResolver.DeviceStateInfo Disabled(string instanceId, string name) =>
            new()
            {
                InstanceId = instanceId,
                FriendlyName = name,
                State = PhysicalDeviceResolver.DeviceState.Disabled,
                ProblemNumber = 22
            };

        private static PhysicalDeviceResolver.DeviceStateInfo Started(string instanceId, string name) =>
            new() { InstanceId = instanceId, FriendlyName = name, State = PhysicalDeviceResolver.DeviceState.Started };

        [Fact]
        public void WithoutKnownInterface_PointsToTheDiagnostics()
        {
            string warning = InputEndpoints.MismatchWarning(2, 0, 0);

            Assert.Contains("TenzoraX listet 2 Controller", warning);
            Assert.Contains("Diagnosebericht", warning);
        }
    }
}

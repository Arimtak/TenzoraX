using System;
using System.Collections.Generic;
using System.Linq;
using TenzoraX.Detection;
using Xunit;

namespace TenzoraX.Tests
{
    /// <summary>
    /// Prüft die Gruppierung der Geräteschnittstellen eines physischen Controllers.
    /// Die Knotenlisten sind echte Windows-Gerätebäume: das 8BitDo Ultimate 2
    /// Wireless meldet sich per USB als zwei Composite-Zweige (XInput + HID).
    /// Reine Rechenlogik – kein Gerät, kein HidHide, keine Systemänderung nötig.
    /// </summary>
    public class PhysicalDeviceResolverTests
    {
        private const string PadSerial = "USB\\VID_2DC8&PID_310B\\82D57104CC";
        private const string XInputInterface = "USB\\VID_2DC8&PID_310B&MI_00\\A&25B3D6E2&5&0000";
        private const string HidKeyboardInterface = "USB\\VID_2DC8&PID_310B&MI_01\\a&25b3d6e2&5&0001";
        private const string HidKeyboardCollection = "HID\\VID_2DC8&PID_310B&MI_01&COL01\\B&12581F46&0&0000";
        private const string SecondBranchComposite = "USB\\VID_2DC8&PID_310B&IG_00\\b&362f5d08&0&00";
        private const string HidGamepadInterface = "HID\\VID_2DC8&PID_310B&IG_00\\C&1D162E25&0&0000";
        private const string Hub = "USB\\VID_174C&PID_2074\\8&12075eed&0&10";
        private const string HubParent = "USB\\ROOT_HUB30\\4&2c9f1b7a&0&0";

        /// <summary>Gerätebaum des 8BitDo Ultimate 2 am USB-Hub (Namen wie von Windows geliefert).</summary>
        private static List<DeviceNode> EightBitDoNodes() => new()
        {
            new DeviceNode(HubParent, "PCI\\VEN_1022&DEV_15B8&SUBSYS_7D751462&REV_00\\4&370529A9&0&0043"),
            new DeviceNode(Hub, HubParent),
            new DeviceNode(PadSerial, Hub),
            new DeviceNode(XInputInterface, PadSerial),
            new DeviceNode(HidKeyboardInterface, PadSerial),
            new DeviceNode(HidKeyboardCollection, HidKeyboardInterface),
            new DeviceNode(SecondBranchComposite, Hub),
            new DeviceNode(HidGamepadInterface, SecondBranchComposite),

            // Fremdes Gerät am selben Hub – darf NIE mit hineingezogen werden.
            new DeviceNode("USB\\VID_046D&PID_C534\\5&1939E8C&0&2", Hub),
            new DeviceNode("HID\\VID_046D&PID_C534&MI_00&COL01\\6&2A1BAB8B&0&0000", "USB\\VID_046D&PID_C534&MI_00\\5&1939E8C&0&2"),
        };

        private static List<string> ResolvePad(string target) => PhysicalDeviceResolver.Resolve(target, EightBitDoNodes());

        [Fact]
        public void Resolve_FromHidGamepadInterface_IncludesBothBranches()
        {
            var group = ResolvePad(HidGamepadInterface);

            // Der HID-Gamepad-Zweig und der XInput-Zweig desselben Pads gehören zusammen.
            Assert.Contains(HidGamepadInterface, group);
            Assert.Contains(SecondBranchComposite, group);
            Assert.Contains(XInputInterface, group);
            Assert.Contains(PadSerial, group);
            Assert.Contains(HidKeyboardInterface, group);
            Assert.Contains(HidKeyboardCollection, group);
        }

        [Fact]
        public void Resolve_FromHidGamepadInterface_DoesNotIncludeForeignDevices()
        {
            var group = ResolvePad(HidGamepadInterface);

            Assert.DoesNotContain("USB\\VID_046D&PID_C534\\5&1939E8C&0&2", group);
            Assert.DoesNotContain("HID\\VID_046D&PID_C534&MI_00&COL01\\6&2A1BAB8B&0&0000", group);
            Assert.DoesNotContain(Hub, group);          // der Hub gehört nicht zum Pad
            Assert.DoesNotContain(HubParent, group);
        }

        [Fact]
        public void Resolve_FromXInputInterface_FindsSameGroup()
        {
            // Egal von welcher Schnittstelle aus aufgelöst wird – es kommt dieselbe
            // Gruppe heraus, damit das Verstecken nie halb ausgeführt wird.
            var fromHid = ResolvePad(HidGamepadInterface);
            var fromXInput = ResolvePad(XInputInterface);

            Assert.Equal(Sorted(fromHid), Sorted(fromXInput));
        }

        [Fact]
        public void Resolve_FromCompositeDevice_FindsSameGroup()
        {
            var group = ResolvePad(PadSerial);

            Assert.Contains(HidGamepadInterface, group);
            Assert.Contains(XInputInterface, group);
            Assert.Contains(HidKeyboardCollection, group);
        }

        [Fact]
        public void Resolve_UnknownDevice_ReturnsOnlyItself()
        {
            // Gerät nicht im Baum bekannt (z.B. frisch eingesteckt): nichts dazuerfinden.
            var group = PhysicalDeviceResolver.Resolve("HID\\VID_1234&PID_5678\\1&2&3", EightBitDoNodes());

            Assert.Single(group);
            Assert.Equal("HID\\VID_1234&PID_5678\\1&2&3", group[0]);
        }

        [Fact]
        public void Resolve_EmptyTargetOrNodes_IsSafe()
        {
            Assert.Empty(PhysicalDeviceResolver.Resolve("", EightBitDoNodes()));
            Assert.Empty(PhysicalDeviceResolver.Resolve(null, EightBitDoNodes()));
            Assert.Empty(PhysicalDeviceResolver.Resolve("   ", EightBitDoNodes()));

            var withoutTree = PhysicalDeviceResolver.Resolve(HidGamepadInterface, new List<DeviceNode>());
            Assert.Single(withoutTree);
            Assert.Equal(HidGamepadInterface, withoutTree[0]);

            Assert.Single(PhysicalDeviceResolver.Resolve(HidGamepadInterface, null!));
        }

        [Fact]
        public void Resolve_TwoIdenticalPads_StaysOnOwnBranch()
        {
            // Zweites Pad desselben Modells am selben Hub: die USB-Seriennummern
            // unterscheiden sich, also darf nur der eigene Zweig versteckt werden.
            var nodes = EightBitDoNodes();
            nodes.Add(new DeviceNode("USB\\VID_2DC8&PID_310B\\A1B2C3D4E5", Hub));
            nodes.Add(new DeviceNode("USB\\VID_2DC8&PID_310B&MI_00\\D&1111111&5&0000", "USB\\VID_2DC8&PID_310B\\A1B2C3D4E5"));
            nodes.Add(new DeviceNode("HID\\VID_2DC8&PID_310B&MI_01&COL01\\E&2222222&0&0000",
                "USB\\VID_2DC8&PID_310B&MI_01\\D&1111111&5&0001"));
            nodes.Add(new DeviceNode("USB\\VID_2DC8&PID_310B&MI_01\\D&1111111&5&0001", "USB\\VID_2DC8&PID_310B\\A1B2C3D4E5"));

            var group = PhysicalDeviceResolver.Resolve(HidGamepadInterface, nodes);

            Assert.Contains(HidGamepadInterface, group);
            Assert.Contains(SecondBranchComposite, group);
            Assert.DoesNotContain("USB\\VID_2DC8&PID_310B\\A1B2C3D4E5", group);
            Assert.DoesNotContain("USB\\VID_2DC8&PID_310B&MI_00\\D&1111111&5&0000", group);
            Assert.DoesNotContain("HID\\VID_2DC8&PID_310B&MI_01&COL01\\E&2222222&0&0000", group);
        }

        [Fact]
        public void Resolve_ReturnsNoDuplicates()
        {
            var group = ResolvePad(HidGamepadInterface);

            Assert.Equal(group.Count, group.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        }

        [Fact]
        public void LastSegmentAndSerialDetection_MatchWindowsConventions()
        {
            Assert.Equal("82D57104CC", PhysicalDeviceResolver.LastSegment(PadSerial));
            Assert.True(PhysicalDeviceResolver.IsSerialLike("82D57104CC"));
            Assert.False(PhysicalDeviceResolver.IsSerialLike("b&362f5d08&0&00"));

            // Seriennummer vs. Seriennummer: nur gleich bedeutet dasselbe Gerät.
            Assert.True(PhysicalDeviceResolver.IsSamePhysicalUnit("82D57104CC", "82D57104CC"));
            Assert.False(PhysicalDeviceResolver.IsSamePhysicalUnit("82D57104CC", "A1B2C3D4E5"));
            // Port-Position auf einer Seite: Aussage bleibt offen (true).
            Assert.True(PhysicalDeviceResolver.IsSamePhysicalUnit("b&362f5d08&0&00", "82D57104CC"));
        }

        // ---------------- Gerätezustand (auch abgeschaltete Knoten) ----------------

        private const uint DN_STARTED = 0x00000008;
        private const uint DN_HAS_PROBLEM = 0x00000400;

        [Fact]
        public void LaufendesGeraet_GiltAlsStarted()
        {
            Assert.Equal(PhysicalDeviceResolver.DeviceState.Started,
                PhysicalDeviceResolver.ClassifyState(DN_STARTED, 0));
        }

        [Fact]
        public void DeaktiviertesGeraet_WirdErkannt()
        {
            // Problemcode 22 = im Geräte-Manager deaktiviert (die zwei Leersammlungen des Pads)
            Assert.Equal(PhysicalDeviceResolver.DeviceState.Disabled,
                PhysicalDeviceResolver.ClassifyState(DN_STARTED | DN_HAS_PROBLEM, 22));
            Assert.Equal(PhysicalDeviceResolver.DeviceState.Disabled,
                PhysicalDeviceResolver.ClassifyState(0, 22));
        }

        [Fact]
        public void NichtAngeschlosseneGeraete_GeltenAlsNotPresent()
        {
            Assert.Equal(PhysicalDeviceResolver.DeviceState.NotPresent,
                PhysicalDeviceResolver.ClassifyState(DN_HAS_PROBLEM, 24));   // Gerät nicht vorhanden
            Assert.Equal(PhysicalDeviceResolver.DeviceState.NotPresent,
                PhysicalDeviceResolver.ClassifyState(DN_HAS_PROBLEM, 45));   // Phantom (anderer USB-Port)
        }

        [Fact]
        public void UnklaresGeraet_BleibtUnknown()
        {
            Assert.Equal(PhysicalDeviceResolver.DeviceState.Unknown,
                PhysicalDeviceResolver.ClassifyState(DN_HAS_PROBLEM, 10));
            Assert.Equal(PhysicalDeviceResolver.DeviceState.Unknown,
                PhysicalDeviceResolver.ClassifyState(0, 0));
        }

        private static List<string> Sorted(IEnumerable<string> values)
            => values.OrderBy(v => v, StringComparer.OrdinalIgnoreCase).ToList();
    }
}

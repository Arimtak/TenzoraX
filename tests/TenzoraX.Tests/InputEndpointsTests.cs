using TenzoraX.Detection;
using Xunit;

namespace TenzoraX.Tests
{
    /// <summary>
    /// Prüft die Rechenregeln der Endpunkt-Ansicht. Zahlen und Erwartungen stammen aus
    /// den Messungen am 13.09.2026 an einem 8BitDo Ultimate 2 Wireless (VID_2DC8/PID_310B):
    /// Das Pad meldet gleichzeitig XInput (Slot 0) und ein HID-Gamepad (Usage 0x0001/0x0005);
    /// zwei weitere Sammlungen (0x000C Multimedia, 0xFF7A Hersteller) liefern keine Eingaben,
    /// werden von DirectInput und dem Windows-Panel aber als Controller geführt.
    ///
    /// Gemessene Zuordnung: Panel-Zeilen = DirectInput-Geräte + belegte XInput-Slots
    /// (1 + 1 = 2 Zeilen; mit den beiden Leersammlungen 3 + 1 = 4 Zeilen), Steam zählt
    /// dagegen nur echte Gamepads (1 + 1 = 2; nach Abschalten des HID-Endpunkts 1 + 0 = 1).
    /// </summary>
    public class InputEndpointsTests
    {
        // ---------------- Echte Gamepads erkennen ----------------

        [Fact]
        public void HidGamepad_GiltAlsEchtesGamepad()
        {
            // Das HID-Gesicht des Pads: UsagePage 0x0001 (Generic Desktop), Usage 0x0005 (Gamepad)
            Assert.True(InputEndpoints.IsRealGamepad(0x0001, 0x0005, 5, 10, 1));
        }

        [Fact]
        public void JoystickUndMultiAxis_GeltenAlsEchtesGamepad()
        {
            Assert.True(InputEndpoints.IsRealGamepad(0x0001, 0x0004, 2, 2, 0));
            Assert.True(InputEndpoints.IsRealGamepad(0x0001, 0x0008, 6, 32, 1));
        }

        [Fact]
        public void MultimediaSammlung_IstKeinGamepad_TrotzGemeldeterTasten()
        {
            // Gemessen: diese Sammlung meldete 0 Achsen, aber 1085 "Tasten" (Unsinn) - sie darf nicht zählen.
            Assert.False(InputEndpoints.IsRealGamepad(0x000C, 0x0001, 0, 1085, 0));
        }

        [Fact]
        public void HerstellerSammlung_IstKeinGamepad()
        {
            Assert.False(InputEndpoints.IsRealGamepad(0xFF7A, 0x0001, 0, 0, 0));
        }

        [Fact]
        public void OhneUsage_EntscheidenDieEingaben()
        {
            Assert.True(InputEndpoints.IsRealGamepad(0, 0, 5, 10, 1));
            Assert.False(InputEndpoints.IsRealGamepad(0, 0, 0, 0, 0));
        }

        // ---------------- Auswertung: Panel und Launcher ----------------

        [Fact]
        public void PanelZeilen_EntsprechenDerMessung()
        {
            // nur HID-Gamepad sichtbar, Slot 0 belegt
            Assert.Equal(2, InputEndpoints.PanelRowEstimate(1, 1));
            // mit den zwei leeren Sammlungen waren es gemessen 4 Zeilen
            Assert.Equal(4, InputEndpoints.PanelRowEstimate(1, 3));
            // HID-Endpunkt abgeschaltet: nur noch die XInput-Zeile
            Assert.Equal(1, InputEndpoints.PanelRowEstimate(1, 0));
            // kein Pad
            Assert.Equal(0, InputEndpoints.PanelRowEstimate(0, 0));
        }

        [Fact]
        public void LauncherZaehlt_NurEchteGamepads()
        {
            // vor dem Abschalten: XInput-Slot 0 + HID-Gamepad = 2 Controller in Steam
            Assert.Equal(2, InputEndpoints.LauncherCountEstimate(1, 1));
            // nach dem Abschalten des HID-Endpunkts = 1 Controller
            Assert.Equal(1, InputEndpoints.LauncherCountEstimate(1, 0));
            // leere Sammlungen zählen nicht mit
            Assert.Equal(2, InputEndpoints.LauncherCountEstimate(1, 1));
        }

        [Fact]
        public void LauncherIgnoriert_LeerSammlungen()
        {
            // 1 DirectInput-Gerät, davon keines mit Eingaben -> trotzdem nur der XInput-Controller
            var snapshot = new InputEndpointSnapshot();
            snapshot.OccupiedXInputSlots.Add(0);
            snapshot.DirectInputEndpoints.Add(new InputEndpoint { Label = "X", HasInput = false });
            snapshot.DirectInputEndpoints.Add(new InputEndpoint { Label = "Y", HasInput = false });

            Assert.Equal(1, snapshot.LauncherCountEstimate);
            Assert.Equal(3, snapshot.PanelRowEstimate);
        }

        [Fact]
        public void Pfad_WirdBeiEindeutigerVidPidGefunden()
        {
            var kandidaten = new[]
            {
                new HidInterface { VidPid = "VID_2DC8&PID_310B", InstanceId = @"HID\VID_2DC8&PID_310B&IG_00\C&1D162E25&0&0000" },
                new HidInterface { VidPid = "VID_045E&PID_02EA", InstanceId = @"HID\VID_045E&PID_02EA\A&1" }
            };

            Assert.Equal(@"HID\VID_2DC8&PID_310B&IG_00\C&1D162E25&0&0000",
                InputEndpoints.MatchHidInstanceId("VID_2DC8&PID_310B", kandidaten));
        }

        [Fact]
        public void Pfad_BleibtLeerWennNichtEindeutig()
        {
            var kandidaten = new[]
            {
                new HidInterface { VidPid = "VID_2DC8&PID_310B", InstanceId = "A" },
                new HidInterface { VidPid = "VID_2DC8&PID_310B", InstanceId = "B" }
            };

            Assert.Equal("", InputEndpoints.MatchHidInstanceId("VID_2DC8&PID_310B", kandidaten));
        }

        [Fact]
        public void Pfad_OhneTrefferOderOhneAngaben_Leer()
        {
            var kandidaten = new[] { new HidInterface { VidPid = "VID_1234&PID_5678", InstanceId = "X" } };

            Assert.Equal("", InputEndpoints.MatchHidInstanceId("VID_2DC8&PID_310B", kandidaten));
            Assert.Equal("", InputEndpoints.MatchHidInstanceId("", kandidaten));
            Assert.Equal("", InputEndpoints.MatchHidInstanceId(null, kandidaten));
            Assert.Equal("", InputEndpoints.MatchHidInstanceId("VID_2DC8&PID_310B", null));
        }

        // ---------------- Abgeschaltete Schnittstellen ----------------

        [Fact]
        public void AbgeschalteteSchnittstellen_WerdenGefunden_UndFremdeNicht()
        {
            var nodes = new[]
            {
                new PhysicalDeviceResolver.DeviceStateInfo
                {
                    InstanceId = @"HID\VID_2DC8&PID_310B&MI_01&COL02\B&12581F46&0&0001",
                    FriendlyName = "HID-konformes Benutzersteuergerät",
                    State = PhysicalDeviceResolver.DeviceState.Disabled
                },
                new PhysicalDeviceResolver.DeviceStateInfo
                {
                    InstanceId = @"HID\VID_2DC8&PID_310B&MI_02\B&2A1BAB8B&0&0000",
                    FriendlyName = "HID-konformes, vom Hersteller definiertes Gerät",
                    State = PhysicalDeviceResolver.DeviceState.Disabled
                },
                // läuft → gehört nicht in die Liste
                new PhysicalDeviceResolver.DeviceStateInfo
                {
                    InstanceId = @"HID\VID_2DC8&PID_310B&IG_00\C&1D162E25&0&0000",
                    State = PhysicalDeviceResolver.DeviceState.Started
                },
                // anderer Hersteller → nicht unser Pad
                new PhysicalDeviceResolver.DeviceStateInfo
                {
                    InstanceId = @"HID\VID_045E&PID_02EA\A&1",
                    State = PhysicalDeviceResolver.DeviceState.Disabled
                }
            };

            var treffer = InputEndpoints.SelectDisabledInterfaces(nodes, new[] { "VID_2DC8&PID_310B" });

            Assert.Equal(2, treffer.Count);
            Assert.All(treffer, t => Assert.Equal(PhysicalDeviceResolver.DeviceState.Disabled, t.State));
            Assert.Contains(treffer, t => t.InstanceId.Contains("MI_01&COL02"));
            Assert.Contains(treffer, t => t.InstanceId.Contains("MI_02"));
        }

        [Fact]
        public void AbgeschalteteSchnittstellen_OhneAngaben_Leer()
        {
            var nodes = new[]
            {
                new PhysicalDeviceResolver.DeviceStateInfo
                {
                    InstanceId = @"HID\VID_2DC8&PID_310B&MI_02\B&1",
                    State = PhysicalDeviceResolver.DeviceState.Disabled
                }
            };

            Assert.Empty(InputEndpoints.SelectDisabledInterfaces(nodes, null));
            Assert.Empty(InputEndpoints.SelectDisabledInterfaces(nodes, Array.Empty<string>()));
            Assert.Empty(InputEndpoints.SelectDisabledInterfaces(null, new[] { "VID_2DC8&PID_310B" }));
        }

        // ---------------- Textfassung ----------------

        [Fact]
        public void Text_NenntBeideWegeUndDasDuplikat()
        {
            var snapshot = new InputEndpointSnapshot { HidHideInstalled = false, HidHideStateKnown = true };
            snapshot.OccupiedXInputSlots.Add(0);
            snapshot.DirectInputEndpoints.Add(new InputEndpoint
            {
                Label = "Controller (8BitDo Ultimate 2 Wireless Controller for PC)",
                Detail = "Eingaben: 5 Achsen, 10 Tasten, 1 POV   HID-Usage 0x0001/0x0005   VID_2DC8&PID_310B",
                InstanceId = @"HID\VID_2DC8&PID_310B&IG_00\C&1D162E25&0&0000",
                HasInput = true
            });

            string text = InputEndpoints.Format(snapshot);

            Assert.Contains("Slot 0:   BELEGT", text);
            Assert.Contains("Slot 3:   leer", text);
            Assert.Contains("Windows-Panel:  2 Zeile(n)", text);
            Assert.Contains("Steam/Launcher: 2 Controller", text);
            Assert.Contains("meldet sich zweimal", text);
            Assert.Contains("nicht installiert", text);
        }

        [Fact]
        public void Text_OhneDuplikat_MeldetKeineAuffaelligkeit()
        {
            var snapshot = new InputEndpointSnapshot { HidHideInstalled = false, HidHideStateKnown = true };
            snapshot.OccupiedXInputSlots.Add(0);

            string text = InputEndpoints.Format(snapshot);

            Assert.Contains("einzigen Eingabeweg", text);
            Assert.Contains("Steam/Launcher: 1 Controller", text);
            Assert.Contains("(kein DirectInput-Gerät)", text);
        }

        [Fact]
        public void Text_NenntAbgeschalteteSchnittstellen()
        {
            var snapshot = new InputEndpointSnapshot { HidHideInstalled = false, HidHideStateKnown = true };
            snapshot.OccupiedXInputSlots.Add(0);
            snapshot.DirectInputEndpoints.Add(new InputEndpoint
            {
                Label = "Controller (8BitDo Ultimate 2 Wireless Controller for PC)",
                HasInput = true,
                VidPid = "VID_2DC8&PID_310B"
            });
            snapshot.DisabledInterfaces.Add(new PhysicalDeviceResolver.DeviceStateInfo
            {
                InstanceId = @"HID\VID_2DC8&PID_310B&MI_01&COL02\B&12581F46&0&0001",
                FriendlyName = "HID-konformes Benutzersteuergerät",
                State = PhysicalDeviceResolver.DeviceState.Disabled
            });

            string text = InputEndpoints.Format(snapshot);

            Assert.Contains("deaktivierte Schnittstellen", text);
            Assert.Contains("HID-konformes Benutzersteuergerät", text);
            Assert.Contains("MI_01&COL02", text);
            Assert.Contains("Problemcode 22", text);
        }

        [Fact]
        public void Text_OhneAbschaltungen_MeldetKeine()
        {
            var snapshot = new InputEndpointSnapshot { HidHideInstalled = false, HidHideStateKnown = true };

            string text = InputEndpoints.Format(snapshot);

            Assert.Contains("deaktivierte Schnittstellen", text);
            Assert.Contains("(keine)", text);
        }

        [Fact]
        public void Text_MarkiertLeereSammlungen()
        {
            var snapshot = new InputEndpointSnapshot { HidHideInstalled = false, HidHideStateKnown = true };
            snapshot.DirectInputEndpoints.Add(new InputEndpoint { Label = "HID-konformes Benutzersteuergerät", HasInput = false });

            string text = InputEndpoints.Format(snapshot);

            Assert.Contains("keine Eingaben", text);
        }

        [Fact]
        public void Text_WarntBeiAktivemCloaking()
        {
            var snapshot = new InputEndpointSnapshot
            {
                HidHideInstalled = true,
                CloakActive = true,
                HiddenDeviceCount = 5,
                HidHideStateKnown = true
            };

            string text = InputEndpoints.Format(snapshot);

            Assert.Contains("Cloaking aktiv, 5 Gerät(e) versteckt", text);
            Assert.Contains("Ausnahmeliste", text);
        }
    }
}

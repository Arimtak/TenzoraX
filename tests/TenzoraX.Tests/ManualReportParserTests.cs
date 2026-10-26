using System.Linq;
using TenzoraX.Detection;
using Xunit;

namespace TenzoraX.Tests
{
    /// <summary>
    /// Tests für den manuellen Fallback-Parser (Layout empirisch aus dem 8BitDo
    /// Ultimate Mobile 64-Byte-Report ermittelt, siehe ManualReportParser-Doku).
    /// </summary>
    public class ManualReportParserTests
    {
        private static byte[] Report(byte b1 = 0, byte b2 = 0, byte hat = 0x0F,
            byte lx = 0x7F, byte ly = 0x7F, byte rx = 0x7F, byte ry = 0x7F,
            byte r2 = 0x00, byte l2 = 0x00)
        {
            var report = new byte[16];                              // Rest bleibt 0 (ReportID etc.)
            report[1] = b1;
            report[2] = b2;
            report[3] = hat;
            report[4] = lx;
            report[5] = ly;
            report[6] = rx;
            report[7] = ry;
            report[8] = r2;
            report[9] = l2;
            return report;
        }

        [Theory]
        [InlineData(0x7F, 0f)]
        [InlineData(0x00, -1f)]
        [InlineData(0xFF, 1f)]                                      // wird auf +1 geklemmt
        public void Normalize8_MapsCenterAndEdges(byte value, float expected)
        {
            Assert.Equal(expected, ManualReportParser.Normalize8(value), 3);
        }

        [Fact]
        public void Normalize8_IsSymmetricAroundCenter()
        {
            // 0x3F (unter Mitte) und 0xBF (über Mitte) liefern entgegengesetzt gleiche Werte
            float below = ManualReportParser.Normalize8(0x3F);
            float above = ManualReportParser.Normalize8(0xBF);
            Assert.True(below < 0f);
            Assert.True(above > 0f);
            Assert.Equal(above, -below, 3);
        }

        [Fact]
        public void Parse_NeutralReport_PressesNothing()
        {
            var (pressed, lx, ly, rx, ry) = ManualReportParser.Parse(Report());
            Assert.Empty(pressed);
            Assert.Equal(0f, lx, 3);
            Assert.Equal(0f, ly, 3);
            Assert.Equal(0f, rx, 3);
            Assert.Equal(0f, ry, 3);
        }

        [Fact]
        public void Parse_FaceButtons()
        {
            // bit0=A, bit1=B, bit3=X, bit4=Y, bit6=L1, bit7=R1
            var (pressed, _, _, _, _) = ManualReportParser.Parse(Report(b1: 0b1101_1011));
            Assert.Contains("A", pressed);
            Assert.Contains("B", pressed);
            Assert.Contains("X", pressed);
            Assert.Contains("Y", pressed);
            Assert.Contains("L1", pressed);
            Assert.Contains("R1", pressed);
            Assert.Equal(6, pressed.Count);
        }

        [Fact]
        public void Parse_SecondaryButtons()
        {
            // bit0=L2, bit1=R2, bit4=HOME, bit5=L3, bit6=R3
            var (pressed, _, _, _, _) = ManualReportParser.Parse(Report(b2: 0b0111_0011));
            Assert.Contains("L2", pressed);
            Assert.Contains("R2", pressed);
            Assert.Contains("HOME", pressed);
            Assert.Contains("L3", pressed);
            Assert.Contains("R3", pressed);
            Assert.DoesNotContain("SELECT", pressed);
            Assert.DoesNotContain("START", pressed);
        }

        [Theory]
        [InlineData(0, new[] { "DPAD_UP" })]
        [InlineData(1, new[] { "DPAD_UP", "DPAD_RIGHT" })]
        [InlineData(2, new[] { "DPAD_RIGHT" })]
        [InlineData(4, new[] { "DPAD_DOWN" })]
        [InlineData(6, new[] { "DPAD_LEFT" })]
        [InlineData(7, new[] { "DPAD_UP", "DPAD_LEFT" })]
        public void Parse_HatPositions(byte hat, string[] expected)
        {
            var (pressed, _, _, _, _) = ManualReportParser.Parse(Report(hat: hat));
            foreach (var e in expected)
                Assert.Contains(e, pressed);
            Assert.Equal(expected.Length, pressed.Count(e => e.StartsWith("DPAD_")));
        }

        [Fact]
        public void Parse_AnalogTriggers_CountAsPressedAboveThreshold()
        {
            // Analogwerte > 0x20 gelten als gedrückt; digitale Bits zusätzlich möglich
            var (pressed, _, _, _, _) = ManualReportParser.Parse(Report(l2: 0xFF, r2: 0x40));
            Assert.Contains("L2", pressed);
            Assert.Contains("R2", pressed);

            var below = ManualReportParser.Parse(Report(l2: 0x10, r2: 0x20));   // Schwelle nicht überschritten
            Assert.DoesNotContain("L2", below.Pressed);
            Assert.DoesNotContain("R2", below.Pressed);
        }

        [Fact]
        public void Parse_StickDirections_AreYInverted()
        {
            // ly/ry werden invertiert: 0x00 (oben im Rohreport) ⇒ +1 in Y-Richtung
            var (_, lx, ly, rx, ry) = ManualReportParser.Parse(
                Report(lx: 0x00, ly: 0x00, rx: 0xFF, ry: 0xFF));
            Assert.Equal(-1f, lx, 3);
            Assert.Equal(1f, ly, 3);
            Assert.Equal(1f, rx, 3);                                 // 0xFF wird auf +1 geklemmt
            Assert.Equal(-1f, ry, 3);                                // -(+1) nach Invertierung... 
        }

        // --- 34-Byte-Report: 8BitDo Ultimate 2 Wireless Bluetooth (VID_2DC8&PID_6012) ---
        // Neutraler Report laut Log-Analyse: 01 0F 7F 7F 7F 7F 00 00 00 00 00 00 12 60 64 94 ...

        private static byte[] Report34(byte face = 0x00, byte menu = 0x00, byte hat = 0x0F,
            byte lx = 0x7F, byte ly = 0x7F, byte rx = 0x7F, byte ry = 0x7F,
            byte r2 = 0x00, byte l2 = 0x00)
        {
            var report = new byte[34];
            report[0] = 0x01;                                       // ReportID
            report[1] = hat;
            report[2] = lx;
            report[3] = ly;
            report[4] = rx;
            report[5] = ry;
            report[6] = r2;
            report[7] = l2;
            report[8] = face;
            report[9] = menu;
            report[15] = 0x94;                                      // Checksummen-Byte (variiert)
            return report;
        }

        [Fact]
        public void Parse34_NeutralReport_PressesNothing()
        {
            var (pressed, lx, ly, rx, ry) = ManualReportParser.Parse(Report34());
            Assert.Empty(pressed);
            Assert.Equal(0f, lx, 3);
            Assert.Equal(0f, ly, 3);
            Assert.Equal(0f, rx, 3);
            Assert.Equal(0f, ry, 3);
        }

        [Fact]
        public void Parse34_FaceButtons()
        {
            // byte[8]: bit0=A, bit1=B, bit3=X, bit4=Y, bit6=L1, bit7=R1 (wie 2C)
            var (pressed, _, _, _, _) = ManualReportParser.Parse(Report34(face: 0b1101_1011));
            Assert.Contains("A", pressed);
            Assert.Contains("B", pressed);
            Assert.Contains("X", pressed);
            Assert.Contains("Y", pressed);
            Assert.Contains("L1", pressed);
            Assert.Contains("R1", pressed);
            Assert.Equal(6, pressed.Count);
        }

        [Fact]
        public void Parse34_SecondaryButtons()
        {
            // byte[9]: bit0=L2, bit1=R2, bit4=HOME, bit5=L3, bit6=R3 (wie 2C)
            var (pressed, _, _, _, _) = ManualReportParser.Parse(Report34(menu: 0b0111_0011));
            Assert.Contains("L2", pressed);
            Assert.Contains("R2", pressed);
            Assert.Contains("HOME", pressed);
            Assert.Contains("L3", pressed);
            Assert.Contains("R3", pressed);
            Assert.DoesNotContain("SELECT", pressed);
            Assert.DoesNotContain("START", pressed);
        }

        [Fact]
        public void Parse34_HatPositions()
        {
            var (pressed, _, _, _, _) = ManualReportParser.Parse(Report34(hat: 0x04));   // runter
            Assert.Contains("DPAD_DOWN", pressed);

            var up = ManualReportParser.Parse(Report34(hat: 0x00));                     // hoch
            Assert.Contains("DPAD_UP", up.Pressed);
        }

        [Fact]
        public void Parse34_AnalogTriggers_CountAsPressedAboveThreshold()
        {
            var (pressed, _, _, _, _) = ManualReportParser.Parse(Report34(l2: 0xFF, r2: 0x40));
            Assert.Contains("L2", pressed);
            Assert.Contains("R2", pressed);
        }

        [Fact]
        public void Parse34_Sticks_NormalizeAndInvertY()
        {
            var (_, lx, ly, rx, ry) = ManualReportParser.Parse(Report34(lx: 0x00, ly: 0x00, rx: 0xFF, ry: 0xFF));
            Assert.Equal(-1f, lx, 3);
            Assert.Equal(1f, ly, 3);
            Assert.Equal(1f, rx, 3);
            Assert.Equal(-1f, ry, 3);
        }

        // ============================================================
        //  32-Byte-Layout: 8BitDo Ultimate 3 im Bluetooth-Betrieb
        //  (Sammlung ohne HID-Achsen/-Buttons; Werte aus einer Aufzeichnung)
        // ============================================================

        private static byte[] Report32(byte hat = 0x08,
            byte lx = 0x80, byte ly = 0x7F, byte rx = 0x80, byte ry = 0x7F,
            byte l2 = 0x00, byte r2 = 0x00, byte b8 = 0x00, byte b9 = 0x00)
        {
            var report = new byte[32];
            report[0] = 0x01;          // ReportID
            report[1] = hat;
            report[2] = lx;
            report[3] = ly;
            report[4] = rx;
            report[5] = ry;
            report[6] = r2;            // RT analog (gemessen)
            report[7] = l2;            // LT analog (gemessen)
            report[8] = b8;
            report[9] = b9;
            report[14] = 0x64;         // Akkustand 100 %
            return report;
        }

        [Fact]
        public void Parse32_NeutralReport_PressesNothingAndSticksAtCenter()
        {
            var (pressed, lx, ly, rx, ry) = ManualReportParser.Parse(Report32());
            Assert.Empty(pressed);
            Assert.InRange(lx, -0.02f, 0.02f);
            Assert.InRange(ly, -0.02f, 0.02f);
            Assert.InRange(rx, -0.02f, 0.02f);
            Assert.InRange(ry, -0.02f, 0.02f);
        }

        [Theory]
        [InlineData(0x01, "A")]
        [InlineData(0x02, "B")]
        [InlineData(0x08, "X")]
        [InlineData(0x10, "Y")]
        [InlineData(0x40, "L1")]
        [InlineData(0x80, "R1")]
        public void Parse32_ErstesTastenbyte_BelegtDieGesichtstasten(byte mask, string expected)
        {
            var (pressed, _, _, _, _) = ManualReportParser.Parse(Report32(b8: mask));
            Assert.Equal(new[] { expected }, pressed);
        }

        [Theory]
        [InlineData(0x01)]
        [InlineData(0x02)]
        [InlineData(0x08)]
        [InlineData(0x10)]
        [InlineData(0x40)]
        [InlineData(0x80)]
        public void Parse32_FaceBits_StimmenMitDerUltimateFamilieUeberein(byte mask)
        {
            // Regression: im 32-Byte-Layout waren A/X/Y um eine Position verschoben
            // (0x10=A, 0x01=X). Dieselbe Maske muss in allen Ultimate-Layouts
            // dieselbe Taste ergeben – das 34-Byte-Layout ist die gemessene Referenz.
            var expected = ManualReportParser.Parse(Report34(face: mask)).Pressed;
            var actual = ManualReportParser.Parse(Report32(b8: mask)).Pressed;
            Assert.Equal(expected, actual);
        }

        [Theory]
        [InlineData(0x20, "L3")]
        [InlineData(0x40, "R3")]
        [InlineData(0x08, "START")]
        [InlineData(0x04, "SELECT")]
        [InlineData(0x10, "HOME")]
        public void Parse32_ZweitesTastenbyte_BelegtSystemtasten(byte mask, string expected)
        {
            var (pressed, _, _, _, _) = ManualReportParser.Parse(Report32(b9: mask));
            Assert.Equal(new[] { expected }, pressed);
        }

        [Fact]
        public void Parse32_Trigger_AnalogUndDigital()
        {
            var analog = ManualReportParser.Parse(Report32(l2: 0xFF, r2: 0xFF)).Pressed;
            Assert.Contains("L2", analog);
            Assert.Contains("R2", analog);

            // Digitalbits in Byte 9: 0x01 = LT, 0x02 = RT
            var digital = ManualReportParser.Parse(Report32(b9: 0x03)).Pressed;
            Assert.Contains("L2", digital);
            Assert.Contains("R2", digital);
            Assert.DoesNotContain("R3", digital);
        }

        [Theory]
        [InlineData(0xFF, 0x00, "L2")]      // nur Byte 7 analog ⇒ LT
        [InlineData(0x00, 0xFF, "R2")]      // nur Byte 6 analog ⇒ RT
        public void Parse32_Trigger_AnalogBytesSindGetrennt(byte l2, byte r2, string expected)
        {
            var pressed = ManualReportParser.Parse(Report32(l2: l2, r2: r2)).Pressed;
            Assert.Equal(new[] { expected }, pressed);
        }

        [Theory]
        [InlineData(0x01, "L2")]
        [InlineData(0x02, "R2")]
        public void Parse32_Trigger_DigitalbitsSindGetrennt(byte mask, string expected)
        {
            var pressed = ManualReportParser.Parse(Report32(b9: mask)).Pressed;
            Assert.Equal(new[] { expected }, pressed);
        }

        [Fact]
        public void Parse32_Sticks_NormalizeAndInvertY()
        {
            var (_, lx, ly, rx, ry) = ManualReportParser.Parse(
                Report32(lx: 0x00, ly: 0x00, rx: 0xFF, ry: 0xFF));
            Assert.Equal(-1f, lx, 3);
            Assert.Equal(1f, ly, 3);
            Assert.Equal(1f, rx, 3);
            Assert.Equal(-1f, ry, 3);
        }

        [Theory]
        [InlineData(0x00, "DPAD_UP")]
        [InlineData(0x02, "DPAD_RIGHT")]
        [InlineData(0x04, "DPAD_DOWN")]
        [InlineData(0x06, "DPAD_LEFT")]
        public void Parse32_HatDirections(byte hat, string expected)
        {
            var (pressed, _, _, _, _) = ManualReportParser.Parse(Report32(hat: hat));
            Assert.Equal(new[] { expected }, pressed);
        }

        [Fact]
        public void Parse32_HatNeutral_PressesNoDirection()
        {
            var (pressed, _, _, _, _) = ManualReportParser.Parse(Report32(hat: 0x08));
            Assert.Empty(pressed);
        }

        [Fact]
        public void Parse32_FremdeReportId_WirdNichtAlsLayoutGedeutet()
        {
            var report = Report32(b8: 0x10, b9: 0x20, l2: 0xFF, r2: 0xFF);
            report[0] = 0x02;                       // andere ReportID
            var (pressed, lx, ly, rx, ry) = ManualReportParser.Parse(report);
            Assert.Empty(pressed);
            Assert.Equal(0f, lx, 3);
            Assert.Equal(0f, ly, 3);
            Assert.Equal(0f, rx, 3);
            Assert.Equal(0f, ry, 3);
        }

        // --- Neutral-Guard: unbekannte Längen dürfen NIE Fantasie-Buttons liefern ---

        // --- XInput-HID-Rahmen (xinputhid): 16 Byte, [2]=0x80 [3]=0xFF [4]=0x7F ---
        // Gemessen am 26.10.2026 am 8BitDo Ultimate 3 (PID_202F am Kabel): dieser
        // Rahmen wurde vorher als 8BitDo-16-Byte-Layout gedeutet und lieferte
        // dauerhaft "R2" plus zappelnde Sticks.

        [Fact]
        public void Parse_XInputHidFrame_ReportsNothing()
        {
            var report = new byte[]
            {
                0x00, 0x00, 0x80, 0xFF, 0x7F, 0xD9, 0x7F, 0x0D,
                0x80, 0x00, 0x80, 0x00, 0x00, 0x00, 0x00, 0x00
            };
            var (pressed, lx, ly, rx, ry) = ManualReportParser.Parse(report);
            Assert.Empty(pressed);
            Assert.Equal(0f, lx, 3);
            Assert.Equal(0f, ly, 3);
            Assert.Equal(0f, rx, 3);
            Assert.Equal(0f, ry, 3);
        }

        [Theory]
        [InlineData(0xD9, 0x7F, 0x0D, 0x80)]
        [InlineData(0xEA, 0x7F, 0x06, 0x80)]
        [InlineData(0x00, 0x80, 0xFF, 0x7F)]
        public void Parse_XInputHidFrame_WithMovingAxisBytes_StaysNeutral(byte b5, byte b6, byte b7, byte b8)
        {
            // Achsenbytes wandern bei jedem Report – daraus wurden vorher
            // "zappelnde Sticks" und ein dauerhaft gedrücktes R2.
            var report = new byte[]
            {
                0x00, 0x00, 0x80, 0xFF, 0x7F, b5, b6, b7,
                b8, 0x00, 0x80, 0x00, 0x00, 0x00, 0x00, 0x00
            };
            var (pressed, _, _, _, _) = ManualReportParser.Parse(report);
            Assert.Empty(pressed);
        }

        [Fact]
        public void IsXInputHidFrame_RequiresFixedFrameAndLength()
        {
            var frame = new byte[] { 0x00, 0x00, 0x80, 0xFF, 0x7F, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
            Assert.True(ManualReportParser.IsXInputHidFrame(frame));
            Assert.False(ManualReportParser.IsXInputHidFrame(null));

            // 15-Byte-Report desselben Pads (IG_00 im USB-Modus) ist kein XInput-Rahmen
            Assert.False(ManualReportParser.IsXInputHidFrame(new byte[15]));

            // Face-Taste in [1] ⇒ generisches 8BitDo-Layout, kein XInput-Rahmen
            frame[1] = 0x01;
            Assert.False(ManualReportParser.IsXInputHidFrame(frame));
        }

        [Fact]
        public void Parse_Generic16Layout_WithUnusedHighBit_StillParses()
        {
            // [2]=0x80 ist im generischen Layout ein unbenutztes Bit; nur der
            // komplette XInput-Rahmen ([3]=0xFF) darf die Deutung sperren.
            var (pressed, _, _, _, _) = ManualReportParser.Parse(Report(b2: 0x80, hat: 0x0F, r2: 0x80));
            Assert.Contains("R2", pressed);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(5)]
        [InlineData(10)]
        [InlineData(12)]
        [InlineData(33)]
        [InlineData(35)]
        public void Parse_UnknownLength_ReturnsNeutral(int length)
        {
            var report = new byte[length];
            for (int i = 0; i < length; i++) report[i] = 0xFF;       // alles gesetzt – darf trotzdem nichts ergeben
            var (pressed, lx, ly, rx, ry) = ManualReportParser.Parse(report);
            Assert.Empty(pressed);
            Assert.Equal(0f, lx, 3);
            Assert.Equal(0f, ly, 3);
            Assert.Equal(0f, rx, 3);
            Assert.Equal(0f, ry, 3);
        }
    }
}

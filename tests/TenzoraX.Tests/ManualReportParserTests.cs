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

        // --- Batterie: 11-Byte-Report, Byte [10] = Akkustand 0..100 ---
        // Log-verifiziert: sinkt über den Tag monoton (0x46=70% → 0x3B=59%).

        [Fact]
        public void ReadBattery_11ByteReport_ReturnsByte10()
        {
            var report = new byte[11];
            report[0] = 0x01;
            report[10] = 0x3B;                                      // 59 %
            Assert.Equal((byte)59, ManualReportParser.ReadBattery(report));
        }

        [Theory]
        [InlineData(100)]
        [InlineData(87)]
        [InlineData(5)]
        public void ReadBattery_ValidRange_IsAccepted(int value)
        {
            var report = new byte[11];
            report[10] = (byte)value;
            Assert.Equal((byte)value, ManualReportParser.ReadBattery(report));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(4)]
        [InlineData(101)]
        [InlineData(0xFF)]
        public void ReadBattery_OutOfRange_ReturnsNull(int value)
        {
            var report = new byte[11];
            report[10] = (byte)value;
            Assert.Null(ManualReportParser.ReadBattery(report));
        }

        [Fact]
        public void ReadBattery_OtherLengths_ReturnNull()
        {
            // 34-Byte-Report: Bytes [10..13] sind konstant (kein Akku) → null
            var report34 = new byte[34];
            report34[0] = 0x01;
            report34[13] = 0x60;                                    // wäre 96 % – darf NICHT geliefert werden
            Assert.Null(ManualReportParser.ReadBattery(report34));

            Assert.Null(ManualReportParser.ReadBattery(new byte[16]));
            Assert.Null(ManualReportParser.ReadBattery(null));
        }

        // --- Neutral-Guard: unbekannte Längen dürfen NIE Fantasie-Buttons liefern ---

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

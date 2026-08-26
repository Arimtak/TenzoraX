using TenzoraX.Detection;
using Xunit;

namespace TenzoraX.Tests
{
    /// <summary>
    /// Tests für den generischen HID-Batterie-Erkenner (Detection.HidBatteryProbe).
    /// Der Erkenner sucht in Feature-Reports heuristisch einen stabilen 0..100 %-Wert,
    /// damit er für beliebige kabellose Gamepads funktioniert (keine hartkodierte
    /// Byte-Position). Wichtig: Byte 0 (Report-ID) zählt nie, Werte außerhalb des
    /// plausiblen Bereichs werden ignoriert, und erst ein stabiler Kandidat über
    /// mehrere Abfragen wird übernommen.
    /// </summary>
    public class HidBatteryProbeTests
    {
        private static byte[] Report(byte reportId, byte[] payload)
        {
            var r = new byte[1 + payload.Length];
            r[0] = reportId;
            for (int i = 0; i < payload.Length; i++) r[i + 1] = payload[i];
            return r;
        }

        [Fact]
        public void StableValue_IsDetectedAfterThreeSamples()
        {
            var probe = new HidBatteryProbe();
            // Feature-Report 0x06, Batterie = 87 bei Byte-Position 3
            probe.Feed(0x06, Report(0x06, new byte[] { 0x01, 0x02, 87, 0x00 }));
            Assert.Null(probe.BatteryPercent);          // 1 Sample reicht nicht
            probe.Feed(0x06, Report(0x06, new byte[] { 0x01, 0x02, 87, 0x00 }));
            Assert.Null(probe.BatteryPercent);
            probe.Feed(0x06, Report(0x06, new byte[] { 0x01, 0x02, 87, 0x00 }));
            Assert.Equal(87, probe.BatteryPercent);
            Assert.Contains("0x06", probe.SourceDescription);
        }

        [Fact]
        public void JitterWithinTolerance_IsStillAccepted()
        {
            var probe = new HidBatteryProbe();
            probe.Feed(0x01, Report(0x01, new byte[] { 0x00, 64 }));
            probe.Feed(0x01, Report(0x01, new byte[] { 0x00, 66 }));   // ±2 ist erlaubt
            probe.Feed(0x01, Report(0x01, new byte[] { 0x00, 65 }));
            Assert.Equal(65, probe.BatteryPercent);
        }

        [Fact]
        public void ReportIdByte_NeverCountsAsBattery()
        {
            var probe = new HidBatteryProbe();
            // Byte 0 = Report-ID 0x06 (6%) – darf NICHT als Akku gezählt werden.
            probe.Feed(0x06, Report(0x06, new byte[] { 0x00, 0x00 }));
            probe.Feed(0x06, Report(0x06, new byte[] { 0x00, 0x00 }));
            probe.Feed(0x06, Report(0x06, new byte[] { 0x00, 0x00 }));
            Assert.Null(probe.BatteryPercent);
        }

        [Fact]
        public void OutOfRangeValues_AreIgnored()
        {
            var probe = new HidBatteryProbe();
            // 0, 4 (zu niedrig), 101 (zu hoch), 0xFF → nie ein Kandidat
            var payload = new byte[] { 0x00, 0x04, 101, 0xFF };
            for (int i = 0; i < 5; i++)
                probe.Feed(0x01, Report(0x01, payload));
            Assert.Null(probe.BatteryPercent);
        }

        [Fact]
        public void JumpingValue_RestartsCounter()
        {
            var probe = new HidBatteryProbe();
            probe.Feed(0x01, Report(0x01, new byte[] { 0x00, 50 }));
            probe.Feed(0x01, Report(0x01, new byte[] { 0x00, 50 }));
            probe.Feed(0x01, Report(0x01, new byte[] { 0x00, 90 }));   // Sprung um 40
            probe.Feed(0x01, Report(0x01, new byte[] { 0x00, 90 }));
            Assert.Null(probe.BatteryPercent);                          // 2 stabile Samples reichen nicht
            probe.Feed(0x01, Report(0x01, new byte[] { 0x00, 90 }));
            Assert.Equal(90, probe.BatteryPercent);
        }

        [Fact]
        public void DifferentReportIds_AreTrackedSeparately()
        {
            var probe = new HidBatteryProbe();
            // Report 0x01 konstant 80, Report 0x02 konstant 60 – beide stabil,
            // der zuerst stabile (0x01) gewinnt, ohne sich gegenseitig zu stören.
            for (int i = 0; i < 4; i++)
            {
                probe.Feed(0x01, Report(0x01, new byte[] { 0x00, 80 }));
                probe.Feed(0x02, Report(0x02, new byte[] { 0x00, 60 }));
            }
            Assert.Equal(80, probe.BatteryPercent);
        }

        [Fact]
        public void Reset_ClearsDetection()
        {
            var probe = new HidBatteryProbe();
            for (int i = 0; i < 4; i++)
                probe.Feed(0x01, Report(0x01, new byte[] { 0x00, 75 }));
            Assert.Equal(75, probe.BatteryPercent);
            probe.Reset();
            Assert.Null(probe.BatteryPercent);
            Assert.Null(probe.SourceDescription);
        }
    }
}

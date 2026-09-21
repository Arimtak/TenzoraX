using System;
using TenzoraX.Detection;
using Xunit;

namespace TenzoraX.Tests
{
    /// <summary>
    /// Die Lade-Erkennung darf nur behaupten, was messbar ist: „am Kabel" kommt von
    /// der Quelle, „lädt" ausschließlich von einem gestiegenen echten Akkuwert.
    /// </summary>
    public class BatteryChargeDetectorTests
    {
        private static readonly DateTime T0 = new DateTime(2026, 9, 13, 12, 0, 0, DateTimeKind.Utc);

        [Fact]
        public void OhneBeobachtung_Unbekannt()
        {
            var d = new BatteryChargeDetector();
            Assert.Equal(BatteryChargeState.Unknown, d.State);
            Assert.False(d.IsCharging);
        }

        [Fact]
        public void EchterWertOhneAnstieg_Ruht()
        {
            var d = new BatteryChargeDetector();
            d.Observe(T0, 57, false);
            Assert.Equal(BatteryChargeState.Idle, d.State);
            Assert.False(d.IsCharging);
            Assert.Equal(57, d.LastRealPercent);
        }

        [Fact]
        public void UsbVerbindungOhneAnstieg_IstKeineLadung()
        {
            // BATTERY_TYPE_WIRED stimmt am Kabel UND über den 2,4-GHz-Empfänger –
            // deshalb darf daraus keine Ladung (und keine Animation) werden.
            var d = new BatteryChargeDetector();
            d.Observe(T0, 57, true);
            Assert.Equal(BatteryChargeState.UsbConnected, d.State);
            Assert.False(d.IsCharging);
        }

        [Fact]
        public void SteigenderWert_WirdLaden()
        {
            var d = new BatteryChargeDetector();
            d.Observe(T0, 57, false);
            d.Observe(T0.AddMinutes(1), 58, false);
            Assert.Equal(BatteryChargeState.Charging, d.State);
        }

        [Fact]
        public void FallenderWert_LoestKeinLadenAus()
        {
            var d = new BatteryChargeDetector();
            d.Observe(T0, 60, false);
            d.Observe(T0.AddMinutes(2), 59, false);
            d.Observe(T0.AddMinutes(4), 58, false);
            Assert.Equal(BatteryChargeState.Idle, d.State);
        }

        [Fact]
        public void LadeAnzeigeBleibtKlebrigUndLaeuftDannAb()
        {
            var d = new BatteryChargeDetector();
            d.Observe(T0, 57, false);
            d.Observe(T0.AddMinutes(1), 58, false);
            Assert.Equal(BatteryChargeState.Charging, d.State);

            // Innerhalb der Klebrigkeit hält die Animation
            d.Observe(T0.AddMinutes(4), 58, false);
            Assert.Equal(BatteryChargeState.Charging, d.State);

            // Danach fällt sie sauber auf „ruht" zurück
            d.Observe(T0.AddMinutes(20), 58, false);
            Assert.Equal(BatteryChargeState.Idle, d.State);
        }

        [Fact]
        public void ZwischendurchGeladen_HoherWertNachLuecke_WirdLaden()
        {
            var d = new BatteryChargeDetector();
            d.Observe(T0, 40, false);

            // Pad war aus (Laden bei ausgeschaltetem Controller), kommt mit mehr zurück
            d.Observe(T0.AddMinutes(30), 55, false);
            Assert.Equal(BatteryChargeState.Charging, d.State);
        }

        [Fact]
        public void OhneEchtenWertUndOhneKabel_Unbekannt()
        {
            var d = new BatteryChargeDetector();
            d.Observe(T0, 57, false);
            d.Observe(T0.AddMinutes(1), null, false);
            Assert.Equal(BatteryChargeState.Unknown, d.State);
            Assert.False(d.IsCharging);
        }

        [Fact]
        public void Reset_SetztAllesZurueck()
        {
            var d = new BatteryChargeDetector();
            d.Observe(T0, 57, true);
            d.Reset();
            Assert.Equal(BatteryChargeState.Unknown, d.State);
            Assert.Null(d.LastRealPercent);
            Assert.Null(d.LastRealAt);
        }

        // --- Rechenregeln des Timer-Modus ---

        [Fact]
        public void Prozent_RechnetAusNennlaufzeit()
        {
            Assert.Equal(100, BatteryEstimate.Percent(0, 15), 3);
            Assert.Equal(50, BatteryEstimate.Percent(450, 15), 3);   // 7,5 h von 15 h
            Assert.Equal(0, BatteryEstimate.Percent(1000, 15), 3);
        }

        [Fact]
        public void Nachziehen_TrifftDenGemessenenWertExakt()
        {
            double minutes = BatteryEstimate.MinutesForPercent(57, 15);
            Assert.Equal(57, BatteryEstimate.Percent(minutes, 15), 3);
        }

        [Fact]
        public void Nachziehen_BeiUnbekannterNennlaufzeit_ohneWirkung()
        {
            Assert.Equal(0, BatteryEstimate.MinutesForPercent(50, 0), 3);
        }

        [Theory]
        [InlineData(0x01, false)]   // WIRED: am Kabel ist der Stufenwert keine Messung
        [InlineData(0x02, true)]    // ALKALINE
        [InlineData(0x03, true)]    // NIMH
        [InlineData(0xFF, true)]    // UNKNOWN
        public void XInputStufenwert_NurAusserhalbDesKabelsAussagekraeftig(byte type, bool expected)
        {
            Assert.Equal(expected, BatteryEstimate.IsXInputLevelMeaningful(type));
        }

        [Fact]
        public void GenauererWertGewinnt_HidSchlaegtXInputStufen()
        {
            // Am Kabel meldet XInput "100%" (BATTERY_LEVEL_FULL), der HID-Report den echten Wert.
            Assert.Equal("57%", BatteryEstimate.PreferMorePrecise("100%", 57));
            // Ohne HID-Wert bleibt der XInput-Wert stehen (auch "N/A").
            Assert.Equal("100%", BatteryEstimate.PreferMorePrecise("100%", null));
            Assert.Equal("N/A", BatteryEstimate.PreferMorePrecise("N/A", null));
        }

        [Theory]
        [InlineData(BatteryChargeState.Charging, false)]
        [InlineData(BatteryChargeState.UsbConnected, true)]   // Funk läuft auch über USB
        [InlineData(BatteryChargeState.Idle, true)]
        [InlineData(BatteryChargeState.Unknown, true)]
        public void Verbrauch_ZaehltNurWennKeinLaden(BatteryChargeState state, bool expected)
        {
            Assert.Equal(expected, BatteryEstimate.ShouldCountActivity(state));
        }
    }
}

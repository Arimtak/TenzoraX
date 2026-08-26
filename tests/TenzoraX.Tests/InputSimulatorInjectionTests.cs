using System;
using System.Collections.Generic;
using Xunit;

namespace TenzoraX.Tests
{
    /// <summary>
    /// Testet die UIPI-Erkennung der Tastatur-Ausgabe: SendInput wird von Windows
    /// STILL verworfen, wenn das aktive Fenster mit höheren Rechten läuft – weder
    /// Rückgabewert noch GetLastError melden das (laut Doku). Erkannt wird es über
    /// den Integritäts-Level-Vergleich (Equal-or-Lesser-Regel von UIPI).
    ///
    /// Über den Test-Seam ForegroundIntegrityOverride wird der Vergleich
    /// deterministisch geprüft, ohne von der echten Umgebung abzuhängen:
    /// int.MaxValue ist garantiert höher als unser Integritäts-Level (auch wenn
    /// die Test-Session als Administrator läuft) → blockiert, SendInput wird
    /// übersprungen. 0 ist garantiert nicht höher → nicht blockiert.
    /// </summary>
    public class InputSimulatorInjectionTests : IDisposable
    {
        public InputSimulatorInjectionTests()
        {
            InputSimulator.ResetInjectionReportingForTesting();
        }

        public void Dispose()
        {
            InputSimulator.ResetInjectionReportingForTesting();
        }

        [Fact]
        public void BlockedByUipi_ReportsOnce_AndSkipsSendInput()
        {
            InputSimulator.ForegroundIntegrityOverride = () => int.MaxValue;

            var messages = new List<string>();
            InputSimulator.InjectionBlocked += messages.Add;

            try
            {
                // Trigger-Pfad: KeyDown/KeyUp → SendKeys → SendInputChecked.
                // Bei Blockade wird gar nicht erst gesendet (würde still verworfen).
                InputSimulator.SimulateKeyDown(new List<string> { "F13" });
                InputSimulator.SimulateKeyUp(new List<string> { "F13" });

                // Throttle: trotz mehrerer SendInput-Aufrufe nur EINE Meldung,
                // damit der Poll-Thread (bis ~250×/s) nicht spammt.
                Assert.Single(messages);
                Assert.Contains("administrator", messages[0], StringComparison.OrdinalIgnoreCase);
                Assert.NotNull(InputSimulator.LastInjectionBlockMessage);
                Assert.NotNull(InputSimulator.LastInjectionBlockAt);
            }
            finally
            {
                InputSimulator.InjectionBlocked -= messages.Add;
            }
        }

        [Fact]
        public void NotBlockedByUipi_DoesNotReport()
        {
            InputSimulator.ForegroundIntegrityOverride = () => 0;

            // Kein Send-Pfad hier: bei "nicht blockiert" würde SimulateKeyDown
            // echte Tastatureingaben injizieren. Nur die Erkennung prüfen.
            Assert.False(InputSimulator.IsBlockedByUipi());
            Assert.Null(InputSimulator.LastInjectionBlockMessage);
            Assert.Null(InputSimulator.LastInjectionBlockAt);
        }

        [Fact]
        public void Reset_ClearsBlockState()
        {
            InputSimulator.ForegroundIntegrityOverride = () => int.MaxValue;
            InputSimulator.SimulateKeyDown(new List<string> { "F13" });
            Assert.NotNull(InputSimulator.LastInjectionBlockMessage);

            InputSimulator.ResetInjectionReportingForTesting();

            Assert.Null(InputSimulator.LastInjectionBlockMessage);
            Assert.Null(InputSimulator.LastInjectionBlockAt);
            Assert.Null(InputSimulator.ForegroundIntegrityOverride);
        }
    }
}

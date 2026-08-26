using System;
using Xunit;

namespace TenzoraX.Tests
{
    /// <summary>
    /// Testet die Grenzfälle des Latenz-Monitors: leere Messwerte, RecordOutputDone
    /// ohne vorheriges MarkInputEvent sowie die Zusammenfassung nach Messwerten.
    /// </summary>
    public class LatencyMonitorTests
    {
        [Fact]
        public void Summary_WithoutMeasurements_ShowsPlaceholderPerLine()
        {
            LatencyMonitor.ResetForTesting();
            var s = LatencyMonitor.FormatSummary();

            Assert.Contains("Eingabe→SendInput : noch keine Messwerte", s);
            Assert.Contains("Poll-Intervall    : noch keine Messwerte", s);
            Assert.Contains("Dispatcher-Stau   : noch keine Messwerte", s);
        }

        [Fact]
        public void RecordOutputDone_WithoutMarkInputEvent_IsIgnored()
        {
            // Darf nicht werfen und darf keine Messwerte erzeugen
            // (es gab kein vorheriges MarkInputEvent auf dem Poll-Thread).
            LatencyMonitor.ResetForTesting();
            LatencyMonitor.RecordOutputDone();

            Assert.Contains("Eingabe→SendInput : noch keine Messwerte", LatencyMonitor.FormatSummary());
        }

        [Fact]
        public void RecordPollInterval_AppearsInSummary()
        {
            LatencyMonitor.ResetForTesting();
            LatencyMonitor.RecordPollInterval(TimeSpan.FromMilliseconds(4));
            LatencyMonitor.RecordPollInterval(TimeSpan.FromMilliseconds(9));

            var lines = LatencyMonitor.FormatSummary().Split('\n');
            Assert.DoesNotContain("noch keine", lines[1]);
            Assert.Contains("Ø", lines[1]);
            Assert.Contains("max", lines[1]);
            Assert.Contains("(n=2)", lines[1]);
        }
    }
}

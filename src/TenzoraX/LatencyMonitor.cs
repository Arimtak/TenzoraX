using System;
using System.Diagnostics;
using System.Text;

namespace TenzoraX
{
    /// <summary>
    /// Erfasst Latenz-Kennzahlen des Eingabe-Pfads – thread-sicher, da Poll-Thread
    /// und UI-Thread gleichzeitig schreiben:
    ///  • Eingabe→SendInput : Zeit von StateChanged (Poll-Thread) bis SendInput fertig
    ///  • Poll-Intervall    : tatsächlicher Abstand der Poll-Iterationen (inkl. Sleep)
    ///  • Dispatcher-Stau   : Zeit von StateChanged bis der UI-Callback läuft
    /// Werte werden als letzte/Ø/max seit App-Start gehalten und im Diagnose-Fenster
    /// live angezeigt.
    /// </summary>
    public static class LatencyMonitor
    {
        private sealed class Metric
        {
            public int Count;
            public long LastTicks;
            public long MinTicks = long.MaxValue;
            public long MaxTicks;
            public long SumTicks;

            public void Reset()
            {
                Count = 0;
                LastTicks = 0;
                MinTicks = long.MaxValue;
                MaxTicks = 0;
                SumTicks = 0;
            }
        }

        private static readonly object _lock = new();
        private static readonly Metric _output = new();
        private static readonly Metric _poll = new();
        private static readonly Metric _dispatcher = new();
        private static long _eventStartTicks;

        /// <summary>
        /// Muss auf dem Poll-Thread beim Eintreffen eines StateChanged aufgerufen
        /// werden (Startpunkt für Eingabe→SendInput).
        /// </summary>
        public static void MarkInputEvent()
        {
            lock (_lock)
            {
                _eventStartTicks = Stopwatch.GetTimestamp();
            }
        }

        /// <summary>
        /// Muss direkt nach dem SendInput-Aufruf aufgerufen werden – im selben
        /// Thread wie MarkInputEvent (Poll-Thread), damit die Messung stimmt.
        /// </summary>
        public static void RecordOutputDone()
        {
            lock (_lock)
            {
                if (_eventStartTicks == 0) return;   // kein vorheriges MarkInputEvent
                Record(_output, Stopwatch.GetElapsedTime(_eventStartTicks));
            }
        }

        /// <summary>Erfasst die tatsächliche Dauer einer Poll-Iteration.</summary>
        public static void RecordPollInterval(TimeSpan elapsed)
        {
            lock (_lock) Record(_poll, elapsed);
        }

        /// <summary>Erfasst die Wartezeit eines StateChanged in der UI-Dispatcher-Queue.</summary>
        public static void RecordDispatcherBacklog(TimeSpan elapsed)
        {
            lock (_lock) Record(_dispatcher, elapsed);
        }

        private static void Record(Metric m, TimeSpan d)
        {
            long ticks = d.Ticks;
            m.Count++;
            m.LastTicks = ticks;
            if (ticks < m.MinTicks) m.MinTicks = ticks;
            if (ticks > m.MaxTicks) m.MaxTicks = ticks;
            m.SumTicks += ticks;
        }

        /// <summary>Interner Test-Seam: setzt alle Messwerte zurück (Isolation zwischen Tests).</summary>
        internal static void ResetForTesting()
        {
            lock (_lock)
            {
                _eventStartTicks = 0;
                _output.Reset();
                _poll.Reset();
                _dispatcher.Reset();
            }
        }

        /// <summary>Mehrzeilige Zusammenfassung für Diagnose-Fenster und -Bericht.</summary>
        public static string FormatSummary()
        {
            lock (_lock)
            {
                return
                    "Eingabe→SendInput : " + Line(_output) + "\n" +
                    "Poll-Intervall    : " + Line(_poll) + "\n" +
                    "Dispatcher-Stau   : " + Line(_dispatcher);
            }
        }

        private static string Line(Metric m)
        {
            if (m.Count == 0) return "noch keine Messwerte";
            return $"letzte {Ms(m.LastTicks)} | Ø {Ms(m.SumTicks / m.Count)} | max {Ms(m.MaxTicks)} (n={m.Count})";
        }

        private static string Ms(long ticks) => TimeSpan.FromTicks(ticks).TotalMilliseconds.ToString("0.00") + " ms";
    }
}

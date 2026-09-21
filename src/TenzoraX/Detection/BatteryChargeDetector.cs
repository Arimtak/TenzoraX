using System;
using System.Collections.Generic;

namespace TenzoraX.Detection
{
    /// <summary>
    /// Ladezustand des Controllers, soweit er sich belegen lässt.
    ///
    /// Wichtig (gemessen am 13.09.2026): XInput <c>BATTERY_TYPE_WIRED</c> bedeutet bei
    /// diesem Pad **nicht** „am Kabel", sondern nur „hängt an einem USB-Anschluss" –
    /// der 2,4-GHz-Empfänger ist selbst USB und meldet denselben Typ. Deshalb ist
    /// <see cref="UsbConnected"/> kein Ladungsbeweis; als Ladung gilt ausschließlich
    /// ein nachweislich steigender Akkuwert.
    /// </summary>
    public enum BatteryChargeState
    {
        /// <summary>Nichts bekannt – kein Controller, kein Wert.</summary>
        Unknown,

        /// <summary>Controller liefert Werte, aber keinen Ladefinhinweis.</summary>
        Idle,

        /// <summary>Über USB verbunden (Kabel **oder** Funkempfänger) – kein Ladungsnachweis.</summary>
        UsbConnected,

        /// <summary>Der Akkuwert ist nachweislich gestiegen – es wird geladen.</summary>
        Charging
    }

    /// <summary>
    /// Erkennt „am Kabel" und „lädt" aus zwei unabhängigen Beobachtungen:
    ///
    /// 1. <c>onCable</c> – vom Aufrufer gemeldet (XInput <c>BATTERY_TYPE_WIRED</c>).
    /// 2. Steigender Akkuwert – der einzige Nachweis, der ohne Hersteller-Bit
    ///    auskommt. Er wirkt auch nachträglich: war das Pad zwischenzeitlich aus
    ///    (Laden bei ausgeschaltetem Controller), springt der Wert beim
    ///    Zurückkommen nach oben und gilt als „zwischendurch geladen".
    ///
    /// Es zählen ausschließlich **echte** Messwerte. Die Schätzung des Timers darf
    /// niemals ein „lädt" auslösen, sonst behauptet die Anzeige ein Laden, das nie
    /// gemessen wurde.
    /// </summary>
    public sealed class BatteryChargeDetector
    {
        /// <summary>Zeitfenster, in dem ein Anstieg als Laden gilt.</summary>
        private static readonly TimeSpan RiseWindow = TimeSpan.FromMinutes(10);

        /// <summary>Ab dieser Lücke gilt ein Anstieg als „zwischendurch geladen".</summary>
        private static readonly TimeSpan OffGapThreshold = TimeSpan.FromMinutes(2);

        /// <summary>So lange bleibt „lädt" nach dem letzten Anstieg stehen (gegen Flackern).</summary>
        private static readonly TimeSpan StickyAfterRise = TimeSpan.FromMinutes(5);

        /// <summary>Kleinster Anstieg in Prozentpunkten, der als Laden zählt.</summary>
        private const int MinRisePercent = 1;

        private const int MaxSamples = 32;

        private struct Sample
        {
            public DateTime Utc;
            public int Percent;
        }

        private readonly List<Sample> _samples = new List<Sample>();
        private DateTime _lastRiseAt = DateTime.MinValue;

        /// <summary>Aktueller Zustand – ausschließlich aus Beobachtungen abgeleitet.</summary>
        public BatteryChargeState State { get; private set; } = BatteryChargeState.Unknown;

        /// <summary>Letzter echter Messwert (Prozent) oder null.</summary>
        public int? LastRealPercent { get; private set; }

        /// <summary>Zeitpunkt des letzten echten Messwerts (UTC) oder null.</summary>
        public DateTime? LastRealAt { get; private set; }

        /// <summary>
        /// True nur bei nachgewiesener Ladung. USB-Verbindung allein genügt nicht:
        /// der Funkempfänger ist selbst USB (gemessen), sonst würde die Anzeige bei
        /// jedem kabellosen Spielen eine Ladung behaupten.
        /// </summary>
        public bool IsCharging => State == BatteryChargeState.Charging;

        /// <summary>
        /// Füttert eine Beobachtung. Wird im Sekundentakt aufgerufen; beliebig oft
        /// mit demselben Wert aufrufbar (Leerlauf verändert nichts).
        /// </summary>
        /// <param name="utc">Zeitpunkt der Beobachtung.</param>
        /// <param name="realPercent">Echter Akkuwert in Prozent oder null (kein echter Wert).</param>
        /// <param name="onCable">Meldet die Quelle „hängt am Kabel".</param>
        public void Observe(DateTime utc, int? realPercent, bool onCable)
        {
            if (realPercent is int pct)
            {
                pct = Math.Clamp(pct, 0, 100);

                if (LastRealPercent is int previous && pct > previous)
                {
                    bool offGap = LastRealAt is DateTime prevAt && (utc - prevAt) >= OffGapThreshold;
                    bool withinWindow = RiseWithinWindow(utc, pct);
                    if ((offGap && pct > previous) || withinWindow)
                        _lastRiseAt = utc;
                }

                LastRealPercent = pct;
                LastRealAt = utc;
                AddSample(utc, pct);

                State = (utc - _lastRiseAt) <= StickyAfterRise
                    ? BatteryChargeState.Charging
                    : onCable ? BatteryChargeState.UsbConnected : BatteryChargeState.Idle;
                return;
            }

            // Kein echter Wert: bleibt die USB-Verbindung erkennbar, sonst ist nichts bekannt.
            State = onCable ? BatteryChargeState.UsbConnected : BatteryChargeState.Unknown;
        }

        /// <summary>
        /// True, wenn der Wert gegenüber einer Probe im Zeitfenster gestiegen ist.
        /// Nur echte Proben; die klebrige Anzeige hängt nicht davon ab.
        /// </summary>
        private bool RiseWithinWindow(DateTime utc, int pct)
        {
            foreach (var s in _samples)
            {
                if ((utc - s.Utc) > RiseWindow) continue;
                if (pct - s.Percent >= MinRisePercent) return true;
            }
            return false;
        }

        private void AddSample(DateTime utc, int pct)
        {
            _samples.Add(new Sample { Utc = utc, Percent = pct });

            // Nur Proben im Zeitfenster behalten; den Fall „zwischendurch geladen"
            // trägt der Vergleich mit LastRealPercent (eigene Felder), nicht die Liste.
            while (_samples.Count > MaxSamples)
                _samples.RemoveAt(0);
            for (int i = _samples.Count - 2; i >= 0; i--)
            {
                if (utc - _samples[i].Utc > RiseWindow)
                    _samples.RemoveAt(i);
            }
        }

        /// <summary>Setzt den Erkenner zurück (Controller getrennt, Nutzer-Reset).</summary>
        public void Reset()
        {
            _samples.Clear();
            _lastRiseAt = DateTime.MinValue;
            State = BatteryChargeState.Unknown;
            LastRealPercent = null;
            LastRealAt = null;
        }
    }

    /// <summary>
    /// Reine Rechenregeln für die Timer-Schätzung („Timer-Modus"). Getrennt von der
    /// Oberfläche, damit Pause und Nachziehen testbar bleiben.
    /// </summary>
    public static class BatteryEstimate
    {
        public const double DefaultHours = 15.0;

        /// <summary>Verbrauchte Prozent aus aktiven Minuten und Nennlaufzeit.</summary>
        public static double Percent(double activeMinutes, double hours)
        {
            if (hours <= 0) return 100;
            double used = activeMinutes * (100.0 / (hours * 60.0));
            return Math.Max(0, Math.Min(100, 100.0 - used));
        }

        /// <summary>
        /// Aktive Minuten, die zu einem bestimmten Prozentwert gehören – damit die
        /// Schätzung nach einer Ladung wieder auf dem gemessenen Wert steht, statt
        /// weiter hinterherzulaufen.
        /// </summary>
        public static double MinutesForPercent(double percent, double hours)
        {
            if (hours <= 0) return 0;
            double clamped = Math.Max(0, Math.Min(100, percent));
            return (100.0 - clamped) * hours * 60.0 / 100.0;
        }

        /// <summary>
        /// Darf in diesem Zustand Verbrauch gezählt werden? Nur beim nachgewiesenen
        /// Laden nicht – dort fließt kein Akkustrom. Eine bloße USB-Verbindung pausiert
        /// absichtlich **nicht**: kabelloses Spielen läuft ebenfalls über den USB-Empfänger.
        /// </summary>
        public static bool ShouldCountActivity(BatteryChargeState state) =>
            state != BatteryChargeState.Charging;

        /// <summary>BATTERY_TYPE_WIRED der XInput-API.</summary>
        public const byte XInputBatteryTypeWired = 0x01;

        /// <summary>
        /// Ist der XInput-Stufenwert eine Aussage über den Akkustand? Bei USB-Verbindung
        /// (Kabel oder Funkempfänger) nicht – dann meldet XInput pauschal FULL:
        /// dort meldet XInput immer BATTERY_LEVEL_FULL (100 %) – gemessen am 13.09.2026
        /// an einem Pad, das real bei 57 % stand. „Unbekannt“ ist ehrlicher als „100 %“.
        /// </summary>
        public static bool IsXInputLevelMeaningful(byte batteryType) =>
            batteryType != XInputBatteryTypeWired;

        /// <summary>
        /// Wählt den genaueren Wert. XInput kennt nur vier Stufen und meldet am Kabel
        /// immer FULL (100 %) – gemessen am 13.09.2026: bei einem Pad mit 57 % meldete
        /// XInput WIRED/FULL. Der HID-Report-Prozentwert desselben Pads ist deshalb
        /// vorzuziehen, wenn er vorliegt.
        /// </summary>
        public static string PreferMorePrecise(string xinputInfo, int? hidPercent) =>
            hidPercent is int percent ? percent + "%" : xinputInfo;
    }
}

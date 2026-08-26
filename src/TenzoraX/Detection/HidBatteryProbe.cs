using System;
using System.Collections.Generic;

namespace TenzoraX.Detection
{
    /// <summary>
    /// Generischer Batterie-Erkenner für HID-Gamepads (Bluetooth / 2,4 GHz).
    ///
    /// Kabellose Controller melden ihren Akkustand in der Regel NICHT im
    /// Eingabe-Report (beim 8BitDo Ultimate 2C sind z.B. die Bytes [10..13]
    /// über Stunden konstant – dort steckt kein Akku), sondern in einem
    /// Feature-Report. Da sich die Report-Layouts pro Hersteller unterscheiden,
    /// wird hier keine Byte-Position hartkodiert, sondern heuristisch gesucht:
    ///
    /// - Jeder Byte-Wert eines Feature-Reports (außer Byte 0 = Report-ID) gilt
    ///   als Kandidat, wenn er im plausiblen Bereich 5..100 % liegt.
    /// - Erst wenn derselbe Kandidat (Report-ID + Byte-Position) über mehrere
    ///   Abfragen stabil bleibt (innerhalb einer kleinen Toleranz), wird er als
    ///   Akkustand übernommen. Ein einzelner Zufallswert reicht nie aus.
    /// - Die stabilste Position gewinnt (z.B. wenn ein Report mehrere Kandidaten
    ///   enthält).
    /// </summary>
    public sealed class HidBatteryProbe
    {
        private const int MinPercent = 5;          // darunter gilt ein Byte als leer/ungesetzt
        private const int MaxPercent = 100;
        private const int RequiredStableSamples = 3;
        private const int MaxJitter = 3;           // erlaubte Abweichung zwischen zwei Abfragen

        private readonly Dictionary<(byte ReportId, int Index), (int Value, int Count)> _tracker = new();
        private int _bestCount;

        /// <summary>Erkannter Akkustand in Prozent oder null, solange kein stabiler Kandidat vorliegt.</summary>
        public int? BatteryPercent { get; private set; }

        /// <summary>Beschreibung der Fundstelle (z.B. "Feature 0x06 Byte[3]") für die Diagnose.</summary>
        public string? SourceDescription { get; private set; }

        /// <summary>
        /// Füttert den Inhalt eines Reports (Feature- oder Eingabe-Report).
        /// Byte 0 ist die Report-ID und zählt nie als Wert. Über <paramref name="label"/>
        /// lässt sich die Fundstelle für die Diagnose beschreiben (z.B. bekannte
        /// Input-Report-Positionen); ohne Label wird die Report-ID verwendet.
        /// </summary>
        public void Feed(byte reportId, byte[] data, string? label = null)
        {
            if (data == null || data.Length < 2) return;

            for (int i = 1; i < data.Length; i++)
            {
                int v = data[i];
                if (v < MinPercent || v > MaxPercent) continue;

                var key = (reportId, i);
                if (_tracker.TryGetValue(key, out var cur))
                {
                    if (Math.Abs(cur.Value - v) <= MaxJitter)
                    {
                        int count = cur.Count + 1;
                        _tracker[key] = (v, count);
                        if (count >= RequiredStableSamples && count > _bestCount)
                        {
                            _bestCount = count;
                            BatteryPercent = v;
                            SourceDescription = label ?? $"Report 0x{reportId:X2} Byte[{i}]";
                        }
                    }
                    else
                    {
                        // Wert gesprungen → kein stabiler Kandidat, Zähler neu starten
                        _tracker[key] = (v, 1);
                    }
                }
                else
                {
                    _tracker[key] = (v, 1);
                }
            }
        }

        /// <summary>Setzt den Erkenner zurück (z.B. nach Geräte-Reconnect).</summary>
        public void Reset()
        {
            _tracker.Clear();
            _bestCount = 0;
            BatteryPercent = null;
            SourceDescription = null;
        }
    }
}

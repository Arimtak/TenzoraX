using System;
using TenzoraX.Effects;
using Xunit;

namespace TenzoraX.Tests
{
    /// <summary>
    /// Die Absicherung darf nur messbare Gründe kennen: Renderstufe, Stromversorgung und den
    /// gemessenen Anzeigetakt mit absoluten Ruckelgrenzen. Sie soll zuerst die <b>Rechenarbeit</b>
    /// des Shaders verringern und erst zuletzt abschalten.
    ///
    /// Die Modelle unten bilden ab, dass der Anzeigetakt davon abhängt, wie teuer der Effekt ist.
    /// Die Zahlen entsprechen dem, was am 13.09.2026 auf echter Hardware gemessen wurde: gesund
    /// 60–129 Takte/s, niedrigster legitimer Wert 33,2 Takte/s, überfordert 15 Takte/s.
    /// </summary>
    public class BackgroundPerformancePolicyTests
    {
        private static readonly DateTime T0 = new(2026, 9, 13, 15, 0, 0, DateTimeKind.Utc);

        private static double Healthy(BackgroundQuality _) => 60.0;

        private static double LowestMeasuredLegit(BackgroundQuality _) => 33.0;

        private static double Stutters(BackgroundQuality _) => 15.0;

        /// <summary>
        /// Führt <b>genau ein</b> Auswertungsfenster aus und bricht ab, sobald sich die Stufe
        /// ändert. Rückgabe: hat sich die Stufe geändert?
        /// </summary>
        private static bool RunOneWindow(BackgroundPerformancePolicy policy, ref DateTime now,
            Func<BackgroundQuality, double> tickFps, bool onBattery, int renderTier)
        {
            var before = policy.Quality;
            double since = 0;
            int guard = 0;

            while (since < BackgroundPerformancePolicy.EvaluationWindow.TotalSeconds + 1.0 && guard++ < 100_000)
            {
                double fps = Math.Max(1.0, tickFps(policy.Quality));
                double step = 1.0 / fps;
                now = now.AddSeconds(step);
                since += step;

                policy.Report(now, onBattery, renderTier);

                if (policy.Quality != before) return true;
            }
            return false;
        }

        [Fact]
        public void OhneHardwarebeschleunigung_BleibtAus()
        {
            var policy = new BackgroundPerformancePolicy();
            policy.Initialize(onBattery: false, renderTier: 0);

            Assert.Equal(BackgroundQuality.Off, policy.Quality);
            Assert.False(policy.Enabled);
            Assert.Equal(0, policy.TargetFps);
            Assert.Equal(0, policy.Layers);
        }

        [Fact]
        public void AmNetz_StartetInVollerQualitaet()
        {
            var policy = new BackgroundPerformancePolicy();
            policy.Initialize(onBattery: false, renderTier: 2);

            Assert.Equal(BackgroundQuality.Full, policy.Quality);
            Assert.Equal(4, policy.Layers);            // wie im Original
            Assert.Equal(0.05, policy.Grain);
            Assert.Equal(BackgroundPerformancePolicy.FullFps, policy.TargetFps);
        }

        [Fact]
        public void ImAkkubetrieb_BleibtVolleQualitaet_AberNur15Fps()
        {
            // Akkubetrieb spart über die Bildrate, nicht über die Rechenarbeit.
            var policy = new BackgroundPerformancePolicy();
            policy.Initialize(onBattery: true, renderTier: 2);

            Assert.Equal(BackgroundQuality.Full, policy.Quality);
            Assert.Equal(4, policy.Layers);
            Assert.Equal(BackgroundPerformancePolicy.BatteryFps, policy.TargetFps);
        }

        [Fact]
        public void FesteVorgabe_SchlaegtDieRegel_AberNichtDasAus()
        {
            var policy = new BackgroundPerformancePolicy();
            policy.Initialize(onBattery: false, renderTier: 2);

            // Vorgabe "minimal" → es wird minimal gerechnet, obwohl die Regel "voll" sagt.
            policy.ForcedQuality = (int)BackgroundQuality.Minimal;
            Assert.Equal(BackgroundQuality.Full, policy.Quality);
            Assert.Equal(BackgroundQuality.Minimal, policy.EffectiveQuality);
            Assert.Equal(2, policy.Layers);
            Assert.Equal(0.0, policy.Grain);
            Assert.Equal(BackgroundPerformancePolicy.MinimalFps, policy.TargetFps);

            // Ohne Hardware-Beschleunigung bleibt es trotz Vorgabe aus.
            policy.ForcedQuality = (int)BackgroundQuality.Full;
            policy.Initialize(onBattery: false, renderTier: 0);
            Assert.Equal(BackgroundQuality.Off, policy.EffectiveQuality);
            Assert.Equal(0, policy.Layers);
            Assert.Equal(0, policy.TargetFps);
        }

        [Fact]
        public void Akkubetrieb_DrosseltSofortOhneFenster()
        {
            var policy = new BackgroundPerformancePolicy();
            policy.Initialize(onBattery: false, renderTier: 2);
            Assert.Equal(BackgroundPerformancePolicy.FullFps, policy.TargetFps);

            DateTime now = T0;
            policy.Report(now, onBattery: true, renderTier: 2);

            Assert.Equal(BackgroundPerformancePolicy.BatteryFps, policy.TargetFps);
            Assert.Equal(BackgroundQuality.Full, policy.Quality);   // Auflösung unverändert
        }

        [Fact]
        public void GesundeAnzeige_HaeltVolleAufloesung()
        {
            var policy = new BackgroundPerformancePolicy();
            policy.Initialize(onBattery: false, renderTier: 2);
            DateTime now = T0;

            for (int i = 0; i < 6; i++)
                Assert.False(RunOneWindow(policy, ref now, Healthy, onBattery: false, renderTier: 2));

            Assert.Equal(BackgroundQuality.Full, policy.Quality);
        }

        [Fact]
        public void GemessenerNiedrigsterLegitimerTakt_SchaltetNichtZurueck()
        {
            // 33 Takte/s wurden auf einem gesunden Rechner ohne Effekt gemessen – das darf die
            // Absicherung nicht als Langsamkeit deuten.
            var policy = new BackgroundPerformancePolicy();
            policy.Initialize(onBattery: false, renderTier: 2);
            DateTime now = T0;

            for (int i = 0; i < 6; i++)
                Assert.False(RunOneWindow(policy, ref now, LowestMeasuredLegit, onBattery: false, renderTier: 2));

            Assert.Equal(BackgroundQuality.Full, policy.Quality);
        }

        [Fact]
        public void RuckelnderTakt_VerringertErstDieRechenarbeit_UndSchaltetZuletztAb()
        {
            var policy = new BackgroundPerformancePolicy();
            policy.Initialize(onBattery: false, renderTier: 2);
            DateTime now = T0;

            // Stufe 1: sparsamer – der Effekt bleibt an, nur langsamer und mit weniger Schichten.
            Assert.True(RunOneWindow(policy, ref now, Stutters, onBattery: false, renderTier: 2));
            Assert.Equal(BackgroundQuality.Reduced, policy.Quality);
            Assert.True(policy.Enabled);
            Assert.Equal(3, policy.Layers);
            Assert.Equal(BackgroundPerformancePolicy.ReducedFps, policy.TargetFps);

            // Stufe 2: minimal – immer noch an.
            Assert.True(RunOneWindow(policy, ref now, Stutters, onBattery: false, renderTier: 2));
            Assert.Equal(BackgroundQuality.Minimal, policy.Quality);
            Assert.True(policy.Enabled);
            Assert.Equal(2, policy.Layers);
            Assert.Equal(0.0, policy.Grain);
            Assert.Equal(BackgroundPerformancePolicy.MinimalFps, policy.TargetFps);

            // Erst wenn auch das nicht reicht, bleibt der Effekt aus.
            Assert.True(RunOneWindow(policy, ref now, Stutters, onBattery: false, renderTier: 2));
            Assert.Equal(BackgroundQuality.Off, policy.Quality);
            Assert.False(policy.Enabled);
        }

        [Fact]
        public void UnklaresBand_LaesstDieStufeUnangetastet()
        {
            var policy = new BackgroundPerformancePolicy();
            policy.Initialize(onBattery: true, renderTier: 2);   // startet in voller Auflösung
            DateTime now = T0;

            // Dazwischen (>= 25 und < 40 Takte/s) wird weder abgestuft noch aufgestuft.
            for (int i = 0; i < 6; i++)
                Assert.False(RunOneWindow(policy, ref now, LowestMeasuredLegit, onBattery: false, renderTier: 2));

            Assert.Equal(BackgroundQuality.Full, policy.Quality);
        }

        [Fact]
        public void ImAkkubetrieb_WirdNichtAufgestuft()
        {
            var policy = new BackgroundPerformancePolicy();
            policy.Initialize(onBattery: false, renderTier: 2);
            DateTime now = T0;

            Assert.True(RunOneWindow(policy, ref now, Stutters, onBattery: false, renderTier: 2));
            Assert.Equal(BackgroundQuality.Reduced, policy.Quality);

            // Am Akku läuft es fehlerfrei – trotzdem bleibt es bei der Sparstufe (Wartezeit egal).
            for (int i = 0; i < 6; i++)
                Assert.False(RunOneWindow(policy, ref now, Healthy, onBattery: true, renderTier: 2));

            Assert.Equal(BackgroundQuality.Reduced, policy.Quality);
            Assert.Equal(BackgroundPerformancePolicy.BatteryFps, policy.TargetFps);
        }

        [Fact]
        public void NachZuLangsam_FlattertNichtSofortZurueck()
        {
            var policy = new BackgroundPerformancePolicy();
            policy.Initialize(onBattery: false, renderTier: 2);
            DateTime now = T0;

            Assert.True(RunOneWindow(policy, ref now, Stutters, onBattery: false, renderTier: 2));
            Assert.Equal(BackgroundQuality.Reduced, policy.Quality);

            // Jetzt läuft die Stufe sauber – trotzdem darf nicht sofort aufgestuft werden.
            for (int i = 0; i < 4; i++)
                Assert.False(RunOneWindow(policy, ref now, Healthy, onBattery: false, renderTier: 2));

            Assert.Equal(BackgroundQuality.Reduced, policy.Quality);
        }

        [Fact]
        public void NachDerWartezeit_WirdWiederBessereAufloesungVersucht()
        {
            var policy = new BackgroundPerformancePolicy();
            policy.Initialize(onBattery: false, renderTier: 2);
            DateTime now = T0;

            Assert.True(RunOneWindow(policy, ref now, Stutters, onBattery: false, renderTier: 2));
            Assert.Equal(BackgroundQuality.Reduced, policy.Quality);

            // Wartezeit verstreichen lassen (die erste beträgt 60 s) → zwei gute Fenster stufen auf.
            now = now.AddSeconds(70);
            Assert.False(RunOneWindow(policy, ref now, Healthy, onBattery: false, renderTier: 2));
            Assert.True(RunOneWindow(policy, ref now, Healthy, onBattery: false, renderTier: 2));

            Assert.Equal(BackgroundQuality.Full, policy.Quality);
        }

        [Fact]
        public void NachDreiRueckstuerzen_WirdNichtMehrAufgestuft()
        {
            var policy = new BackgroundPerformancePolicy();
            policy.Initialize(onBattery: false, renderTier: 2);
            DateTime now = T0;

            // Immer wiederkehrende Schwäche: jede bessere Stufe bricht die Anzeige wieder ein.
            for (int runde = 0; runde < 8; runde++)
            {
                RunOneWindow(policy, ref now, Stutters, onBattery: false, renderTier: 2);
                now = now.AddMinutes(6);      // Wartezeit sicher verstreichen lassen
                RunOneWindow(policy, ref now, Healthy, onBattery: false, renderTier: 2);
                RunOneWindow(policy, ref now, Healthy, onBattery: false, renderTier: 2);
            }

            // Nach drei Rückstürzen wird nicht mehr aufgestuft – eine schwache GPU wird also nicht
            // dauerhaft alle paar Minuten erneut belastet.
            Assert.True(policy.Layers <= 3);
            Assert.True(policy.Enabled);
            Assert.True(policy.TargetFps <= BackgroundPerformancePolicy.FullFps);
        }

        [Fact]
        public void GrosseZeitluecke_WirdNichtAlsZuLangsamFehlgedeutet()
        {
            var policy = new BackgroundPerformancePolicy();
            policy.Initialize(onBattery: false, renderTier: 2);
            DateTime now = T0;
            RunOneWindow(policy, ref now, Healthy, onBattery: false, renderTier: 2);
            Assert.Equal(BackgroundQuality.Full, policy.Quality);

            // Fenster lag zehn Minuten im Tray: danach darf die Regel nicht auf "zu langsam"
            // schließen, nur weil zwischen zwei Takten eine große Zeit lag.
            now = now.AddMinutes(10);
            policy.Report(now, onBattery: false, renderTier: 2);
            Assert.Equal(BackgroundQuality.Full, policy.Quality);

            Assert.False(RunOneWindow(policy, ref now, Healthy, onBattery: false, renderTier: 2));
            Assert.Equal(BackgroundQuality.Full, policy.Quality);
        }

        [Fact]
        public void HardwarebeschleunigungFaelltWeg_SchaltetAb()
        {
            var policy = new BackgroundPerformancePolicy();
            policy.Initialize(onBattery: false, renderTier: 2);
            DateTime now = T0;
            policy.Report(now.AddSeconds(1), onBattery: false, renderTier: 0);
            Assert.Equal(BackgroundQuality.Off, policy.Quality);
        }

        [Fact]
        public void GemessenerAnzeigetakt_WirdAusgewiesen()
        {
            var policy = new BackgroundPerformancePolicy();
            policy.Initialize(onBattery: false, renderTier: 2);
            Assert.Null(policy.LastMeasuredDisplayFps);   // vor der ersten Auswertung nichts behaupten

            DateTime now = T0;
            RunOneWindow(policy, ref now, Healthy, onBattery: false, renderTier: 2);

            Assert.NotNull(policy.LastMeasuredDisplayFps);
            Assert.InRange(policy.LastMeasuredDisplayFps!.Value, 55.0, 65.0);
        }

        [Fact]
        public void Beschreibung_NenntDieStufeUndDenGrund()
        {
            var policy = new BackgroundPerformancePolicy();

            policy.Initialize(onBattery: false, renderTier: 2);
            Assert.Contains("voll", policy.Describe(false, 2));
            Assert.Contains("4 Faserschichten", policy.Describe(false, 2));
            Assert.Contains("Renderstufe 2", policy.Describe(false, 2));

            policy.Initialize(onBattery: true, renderTier: 2);
            Assert.Contains("Akkubetrieb", policy.Describe(true, 2));

            policy.Initialize(onBattery: false, renderTier: 0);
            Assert.Contains("Hardware-Beschleunigung", policy.Describe(false, 0));

            policy.Initialize(onBattery: false, renderTier: 2);
            DateTime now = T0;
            RunOneWindow(policy, ref now, Stutters, onBattery: false, renderTier: 2);
            Assert.Contains("sparsam", policy.Describe(false, 2));
            Assert.Contains("3 Faserschichten", policy.Describe(false, 2));
            Assert.Contains("20 Bilder/s", policy.Describe(false, 2));

            RunOneWindow(policy, ref now, Stutters, onBattery: false, renderTier: 2);
            Assert.Contains("minimal", policy.Describe(false, 2));
            Assert.Contains("ohne Grain", policy.Describe(false, 2));

            RunOneWindow(policy, ref now, Stutters, onBattery: false, renderTier: 2);
            Assert.Contains("sparsamsten Stufe", policy.Describe(false, 2));
        }
    }
}

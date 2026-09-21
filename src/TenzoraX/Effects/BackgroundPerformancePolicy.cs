using System;

namespace TenzoraX.Effects
{
    /// <summary>
    /// Wie teuer der animierte Hintergrund gerade sein darf. Jede Stufe senkt zwei Dinge auf
    /// einmal: die <b>Bildrate</b> (der einzige hier messbar wirksame Hebel) und die
    /// <b>Rechenarbeit pro Pixel</b> (Faserschichten, Grain).
    ///
    /// Warum beides: gemessen bringt die Bildrate eine klare, wiederholbare Einsparung (13,09,2026:
    /// 2,3 % GPU bei 30/s gegen 0,8 % bei 15/s, gleiche Fläche). Die Schichten liegen mit erwarteten
    /// ~1 Punkt unter der Streuung der Zähler (±4 Punkte), sind also mit Hausmitteln nicht
    /// nachweisbar – sie werden trotzdem mitgenommen, weil sie garantiert Arbeit sparen und nichts
    /// kosten.
    /// </summary>
    public enum BackgroundQuality
    {
        /// <summary>Kein Effekt (keine Hardware-Beschleunigung oder auch die sparsamste Stufe zu teuer).</summary>
        Off = 0,

        /// <summary>Zwei Faserschichten, ohne Grain – etwa ein Drittel der Rechenarbeit.</summary>
        Minimal = 1,

        /// <summary>Drei Faserschichten, wenig Grain.</summary>
        Reduced = 2,

        /// <summary>Vier Faserschichten und Grain, wie vom Original vorgegeben.</summary>
        Full = 3
    }

    /// <summary>
    /// Entscheidet, wie viel der animierte Hintergrund kosten darf. Die Regeln stützen sich
    /// ausschließlich auf Messbares:
    ///
    /// 1. <b>WPF-Renderstufe</b> (<c>RenderCapability.Tier</c>): unter 1 läuft WPF ohne
    ///    Hardware-Beschleunigung, ein Fullscreen-Pixel-Shader wäre dann reine Softwarearbeit –
    ///    der Effekt bleibt aus.
    /// 2. <b>Stromversorgung</b>: im Akkubetrieb werden die Bilder auf 15/s begrenzt.
    /// 3. <b>Anzeigetakt</b>: fällt der Takt der Zeichenschleife über ein ganzes Fenster unter
    ///    <see cref="StutterFps"/> Bilder/s, ruckelt die <i>ganze</i> Oberfläche – dann wird die
    ///    Auflösung stufenweise verringert, statt den Effekt abzuschalten. Erst wenn auch die
    ///    sparsamste Stufe nicht reicht, bleibt der Effekt aus.
    ///
    /// <b>Warum Rechenarbeit statt Abschaltung:</b> der Shader wiederholt je Faserschicht einen
    /// Wellen-, Polar-, <c>pow(…,16)</c>- und Glow-Durchlauf und obendrauf fünf Grain-Hashes.
    /// Diese Kosten sind unabhängig davon, wie oft gezeichnet wird – wer weniger Schichten
    /// rechnet, spart garantiert GPU-Zeit und behält trotzdem 30 Bilder/s.
    ///
    /// <b>Was gemessen NICHT funktioniert (13.09.2026, RTX 4070, 2400×1500 Gerätepixel):</b>
    /// eine echte <i>Auflösungsreduktion</i> ließ sich in WPF nicht zuverlässig erreichen.
    /// Eine Skalierung des Elements (<c>ScaleTransform</c>) spart nichts (9,5 % gegen 9,4 %), weil
    /// WPF den Effekt trotzdem in voller Geräteauflösung rasterisiert; <c>BitmapCache(0.5)</c>
    /// ebenso (9,5 %). Der Weg über eine klein gerenderte Fläche und eine VisualBrush sparte in
    /// einer Sonde zwar auf 3,5 %, in der laufenden App aber <b>nichts</b> (4,6–4,8 % gegen 4,8 %)
    /// und ließ sich dort auch nicht als lebendige Animation belegen. Zusätzlich gefunden: feste
    /// <c>Width</c>/<c>Height</c> auf der klein gerenderten Fläche machten es sogar teurer als
    /// gar keine Sparstufe (12,5 % gegen 7,3 %). Ohne Direct3D-Interop gibt es in WPF also keinen
    /// verlässlichen Weg zu weniger Pixeln – deshalb wird die Rechenarbeit verringert.
    ///
    /// <b>Warum absolute Schwellen statt eines Vergleichs mit der Bildwiederholrate:</b> WPF
    /// lieferte auf demselben 120-Hz-Bildschirm je nach Zustand 33 bis 129 Takte/s – auch bei
    /// völlig abgeschaltetem Effekt (33,2 Takte/s im ersten Fenster). Eine Referenz auf die
    /// Monitorrate hätte daraus „vielfach zu langsam“ gemacht und den Effekt auf einer gesunden
    /// Maschine abgeschaltet.
    ///
    /// Die Klasse ist absichtlich frei von WPF-Typen und Zeitgebern, damit die Regeln mit festen
    /// Zeitpunkten testbar bleiben.
    /// </summary>
    public sealed class BackgroundPerformancePolicy
    {
        /// <summary>Ziel-Bildrate in voller Qualität.</summary>
        public const double FullFps = 30.0;

        /// <summary>Ziel-Bildrate der Stufe „sparsam“. Lieber langsamer als abgeschaltet.</summary>
        public const double ReducedFps = 20.0;

        /// <summary>Ziel-Bildrate der sparsamsten Stufe.</summary>
        public const double MinimalFps = 15.0;

        /// <summary>Obergrenze der Bildrate im Akkubetrieb.</summary>
        public const double BatteryFps = 15.0;

        /// <summary>In diesem Abstand wird neu entschieden.</summary>
        public static readonly TimeSpan EvaluationWindow = TimeSpan.FromSeconds(5);

        /// <summary>
        /// Unter diesem Anzeigetakt gilt die Oberfläche als sichtbar überfordert (40 ms pro Bild).
        /// Gemessen lag der niedrigste legitime Wert bei 33,2 Takten/s – die Grenze liegt also mit
        /// Sicherheit darunter und kann nicht fälschlich auslösen.
        /// </summary>
        public const double StutterFps = 25.0;

        /// <summary>
        /// Ab diesem Anzeigetakt gilt die Lage als komfortabel (dann darf wieder aufgestuft
        /// werden). Dazwischen – dem unklaren Band – bleibt die aktuelle Stufe unangetastet.
        /// </summary>
        public const double HealthyFps = 40.0;

        /// <summary>So viele gute Fenster braucht ein Hochstufen.</summary>
        private const int GoodWindowsForUpgrade = 2;

        /// <summary>
        /// Erste Wartezeit, bis nach einem Rücksturz erneut aufgestuft wird. Ohne diese Bremse
        /// würde der Effekt im Minutentakt zwischen den Stufen springen.
        /// </summary>
        private const double UpgradeBaseDelaySeconds = 60;

        /// <summary>Obergrenze der Wartezeit (nach mehreren erfolglosen Versuchen).</summary>
        private const double UpgradeMaxDelaySeconds = 300;

        /// <summary>
        /// So oft darf nach einem Rücksturz überhaupt noch aufgestuft werden. Danach bleibt es für
        /// diese Sitzung bei der erreichten Sparstufe – sonst würde eine zu schwache GPU dauerhaft
        /// alle paar Minuten erneut belastet.
        /// </summary>
        private const int MaxUpgradeAttempts = 3;

        private DateTime _windowStart = DateTime.MinValue;
        private int _framesInWindow;
        private int _goodWindows;
        private bool _onBattery;
        private DateTime? _steppedDownAt;
        private int _stepDowns;

        public BackgroundQuality Quality { get; private set; } = BackgroundQuality.Full;

        /// <summary>
        /// Zuletzt gemessener Anzeigetakt (Takte der Zeichenschleife pro Sekunde) – der Beleg,
        /// ob die Oberfläche noch mitkommt. Null, solange noch keine Auswertung stattgefunden hat.
        /// </summary>
        public double? LastMeasuredDisplayFps { get; private set; }

        /// <summary>Läuft der Rechner gerade auf Akku?</summary>
        public bool OnBattery => _onBattery;

        /// <summary>Gilt der Effekt überhaupt (False = ausschalten)?</summary>
        public bool Enabled => Quality != BackgroundQuality.Off;

        /// <summary>Feste Stufe aus den Einstellungen (0 = automatisch nach Messung).</summary>
        public int ForcedQuality { get; set; }

        /// <summary>
        /// Die Stufe, die wirklich gerechnet wird: eine feste Vorgabe aus den Einstellungen schlägt
        /// die Entscheidung der Regel. „Aus“ bleibt aber in jedem Fall „aus“ – ohne
        /// Hardware-Beschleunigung hilft auch die sparsamste Stufe nicht.
        /// </summary>
        public BackgroundQuality EffectiveQuality => Quality == BackgroundQuality.Off
            ? BackgroundQuality.Off
            : (ForcedQuality >= (int)BackgroundQuality.Minimal && ForcedQuality <= (int)BackgroundQuality.Full
                ? (BackgroundQuality)ForcedQuality
                : Quality);

        /// <summary>Anzahl der Faserschichten, die der Shader je Pixel durchläuft (Original: 4).</summary>
        public int Layers => EffectiveQuality switch
        {
            BackgroundQuality.Minimal => 2,
            BackgroundQuality.Reduced => 3,
            BackgroundQuality.Full => 4,
            _ => 0
        };

        /// <summary>Stärke des Korns (Original: 0,05) – 0 lässt die fünf Hash-Durchläufe weg.</summary>
        public double Grain => EffectiveQuality switch
        {
            BackgroundQuality.Minimal => 0.0,
            BackgroundQuality.Reduced => 0.03,
            BackgroundQuality.Full => 0.05,
            _ => 0
        };

        /// <summary>Bildrate, die der Aufrufer einhalten soll (Akkubetrieb deckelt zusätzlich).</summary>
        public double TargetFps
        {
            get
            {
                if (!Enabled) return 0;

                double fps = EffectiveQuality switch
                {
                    BackgroundQuality.Minimal => MinimalFps,
                    BackgroundQuality.Reduced => ReducedFps,
                    _ => FullFps
                };
                return _onBattery ? Math.Min(fps, BatteryFps) : fps;
            }
        }

        /// <summary>Zielabstand zwischen zwei gezeichneten Bildern.</summary>
        public TimeSpan TargetInterval => TargetFps > 0
            ? TimeSpan.FromSeconds(1.0 / TargetFps)
            : TimeSpan.MaxValue;

        /// <summary>
        /// Startentscheidung ohne Messung: Renderstufe und Stromversorgung stehen sofort fest,
        /// die Anzeigetakte noch nicht.
        /// </summary>
        public void Initialize(bool onBattery, int renderTier)
        {
            _windowStart = DateTime.MinValue;
            _framesInWindow = 0;
            _goodWindows = 0;
            _onBattery = onBattery;
            _steppedDownAt = null;
            _stepDowns = 0;
            LastMeasuredDisplayFps = null;

            Quality = renderTier <= 0 ? BackgroundQuality.Off : BackgroundQuality.Full;
        }

        /// <summary>
        /// Einen Anzeigetakt melden (bei <b>jedem</b> Aufruf der Zeichenschleife, unabhängig
        /// davon, ob gerade ein Bild des Effekts gezeichnet wurde). Nach jedem Auswertungsfenster
        /// wird neu entschieden; die aktuelle Qualität steht danach in <see cref="Quality"/>.
        /// </summary>
        /// <param name="now">Zeitpunkt des Taktes.</param>
        /// <param name="onBattery">Läuft der Rechner gerade auf Akku?</param>
        /// <param name="renderTier">WPF-Renderstufe (0 = ohne Hardware-Beschleunigung).</param>
        public BackgroundQuality Report(DateTime now, bool onBattery, int renderTier)
        {
            _onBattery = onBattery;

            if (renderTier <= 0 && Quality != BackgroundQuality.Off)
            {
                Quality = BackgroundQuality.Off;
                return Quality;
            }

            if (_windowStart == DateTime.MinValue)
            {
                _windowStart = now;
                _framesInWindow = 0;
                return Quality;
            }

            _framesInWindow++;

            double elapsedSeconds = (now - _windowStart).TotalSeconds;

            // Lücke erkannt: Zwischen zwei Takten lag deutlich mehr als ein Auswertungsfenster –
            // z.B. weil das Fenster im Tray lag, der Rechner im Standby war oder die Oberfläche
            // gerade blockiert wurde. So eine Lücke sagt nichts über den Anzeigetakt aus und wird
            // deshalb verworfen statt als "viel zu langsam" fehlgedeutet.
            if (elapsedSeconds > EvaluationWindow.TotalSeconds * 2)
            {
                ResetWindow(now);
                return Quality;
            }

            if (elapsedSeconds < EvaluationWindow.TotalSeconds)
                return Quality;

            double measuredInterval = _framesInWindow > 0 ? elapsedSeconds / _framesInWindow : elapsedSeconds;
            LastMeasuredDisplayFps = measuredInterval > 0 ? 1.0 / measuredInterval : 0;

            if (Quality == BackgroundQuality.Off)
            {
                ResetWindow(now);
                return Quality;
            }

            if (LastMeasuredDisplayFps < StutterFps)
            {
                // Die Anzeige kommt nicht mit: erst die Auflösung verringern, erst zuletzt aus.
                Quality = Quality switch
                {
                    BackgroundQuality.Full => BackgroundQuality.Reduced,
                    BackgroundQuality.Reduced => BackgroundQuality.Minimal,
                    _ => BackgroundQuality.Off
                };
                _stepDowns++;
                _steppedDownAt = now;
                _goodWindows = 0;
            }
            else if (LastMeasuredDisplayFps >= HealthyFps && !onBattery && CanUpgrade(now))
            {
                // Die Stufe läuft sauber → einen Versuch in besserer Auflösung wagen. Im
                // Akkubetrieb nicht (dort gilt Sparen) und nach einem Rücksturz erst nach
                // Wartezeit (Verdopplung bis 5 Minuten, höchstens dreimal), sonst würde der
                // Effekt flattern und eine schwache GPU dauerhaft belasten.
                _goodWindows++;
                if (_goodWindows >= GoodWindowsForUpgrade)
                {
                    Quality = Quality switch
                    {
                        BackgroundQuality.Minimal => BackgroundQuality.Reduced,
                        BackgroundQuality.Reduced => BackgroundQuality.Full,
                        _ => Quality
                    };
                    _goodWindows = 0;
                }
            }
            else
            {
                _goodWindows = 0;
            }

            ResetWindow(now);
            return Quality;
        }

        /// <summary>Kurztext für Log und Diagnose – nennt die Stufe, die wirklich gerechnet wird.</summary>
        public string Describe(bool onBattery, int renderTier)
        {
            if (Quality == BackgroundQuality.Off)
            {
                return renderTier <= 0
                    ? "aus (keine Hardware-Beschleunigung)"
                    : "aus (Anzeige kommt auch in der sparsamsten Stufe nicht mit)";
            }

            string extra = onBattery
                ? ", Akkubetrieb"
                : (EffectiveQuality == BackgroundQuality.Full ? ", Renderstufe " + renderTier : "");

            return EffectiveQuality switch
            {
                BackgroundQuality.Minimal => "minimal (" + (int)TargetFps + " Bilder/s, " + Layers
                    + " Faserschichten, ohne Grain" + extra + ")",
                BackgroundQuality.Reduced => "sparsam (" + (int)TargetFps + " Bilder/s, " + Layers
                    + " Faserschichten" + extra + ")",
                _ => "voll (" + (int)TargetFps + " Bilder/s, " + Layers + " Faserschichten" + extra + ")"
            };
        }

        /// <summary>
        /// Darf aufgestuft werden? Solange „voll" (oder eine höhere Stufe) nie zu teuer war, immer.
        /// Danach erst nach einer Wartezeit, die sich mit jedem Rücksturz verdoppelt (60 s → 5 min),
        /// und höchstens <see cref="MaxUpgradeAttempts"/>-mal.
        /// </summary>
        private bool CanUpgrade(DateTime now)
        {
            if (!_steppedDownAt.HasValue) return true;
            if (_stepDowns > MaxUpgradeAttempts) return false;

            double delaySeconds = Math.Min(
                UpgradeBaseDelaySeconds * Math.Pow(2, Math.Max(0, _stepDowns - 1)),
                UpgradeMaxDelaySeconds);
            return (now - _steppedDownAt.Value).TotalSeconds >= delaySeconds;
        }

        private void ResetWindow(DateTime now)
        {
            _windowStart = now;
            _framesInWindow = 0;
        }
    }
}

using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace TenzoraX
{
    /// <summary>
    /// Drei Oberflächen-Stile: „Flat“ (flach, bisherige Optik), „3D“ (Licht von oben,
    /// Kanten, Tiefe) und „Shine“ (3D mit Glanzkante). Ein Stil besteht ausschließlich
    /// aus Resource-Einträgen (Flächen, Kantenpinsel, Schatten); alle Flächen in
    /// App.xaml und den Fenstern binden sie per DynamicResource. Ein Wechsel wirkt
    /// deshalb sofort, ohne Neustart.
    ///
    /// Die Schatten liegen bewusst auf einer eigenen Fläche HINTER dem Inhalt
    /// (siehe Panel3DStyle / Button-Template): so wird der Text nicht mitgerastert
    /// und bleibt scharf.
    /// </summary>
    internal static class Theme3D
    {
        public const string Flat = "flat";
        public const string ThreeD = "3d";
        public const string Shine = "shine";

        /// <summary>
        /// Stil, mit dem die App startet, wenn der Nutzer keinen bewusst gewählt hat:
        /// Shine (3D mit Glanzkante). Gewünscht am 27.10.2026; vorher war 3D der Standard.
        /// </summary>
        public const string Default = Shine;

        /// <summary>Aktiver Stil (normalisiert: flat | 3d | shine).</summary>
        public static string Current { get; private set; } = Default;

        /// <summary>Zuletzt wirklich angewendeter Stil (null = noch keiner).</summary>
        private static string? _applied;

        /// <summary>Wird nach jedem Wechsel ausgelöst (z.B. für eigene Zeichenflächen).</summary>
        public static event Action? Changed;

        public static string Normalize(string? theme)
        {
            switch ((theme ?? "").Trim().ToLowerInvariant())
            {
                case Flat: return Flat;
                // 3D muss ausdrücklich aufgeführt sein: vorher war es der Default-Zweig,
                // seit Shine der Standard ist, fiele "3d" sonst auf Shine zurück.
                case ThreeD: return ThreeD;
                case Shine: return Shine;
                default: return Default;
            }
        }

        /// <summary>
        /// Liest den beim Start anzuwendenden Stil aus dem Inhalt der config.json.
        ///
        /// Regel: Es gilt nur eine **bewusste Wahl** des Nutzers (Einstellungen →
        /// 🎨 DESIGN), erkennbar am Kennzeichen <c>"UiThemeUserChoice": true</c>.
        /// Ohne Datei, ohne Wahl, bei unbekanntem Wert oder kaputtem JSON gilt der
        /// Standard (<see cref="Default"/>, also Shine).
        ///
        /// Warum das Kennzeichen nötig ist (am echten Profil gemessen): Früher war 3D der
        /// Standard und wurde beim Speichern mitgeschrieben – ein gespeichertes "3d" allein
        /// sagt also nicht, ob es gewählt wurde. Ohne Kennzeichen wäre der neue Standard bei
        /// jeder bestehenden Installation nie sichtbar geworden.
        /// </summary>
        public static string ThemeFromConfigJson(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) return Default;
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object)
                {
                    bool chosen =
                        doc.RootElement.TryGetProperty("UiThemeUserChoice", out var flag) &&
                        flag.ValueKind == System.Text.Json.JsonValueKind.True;

                    if (chosen &&
                        doc.RootElement.TryGetProperty("UiTheme", out var element) &&
                        element.ValueKind == System.Text.Json.JsonValueKind.String)
                    {
                        return Normalize(element.GetString());
                    }
                }
            }
            catch
            {
                // Beschädigte Datei: Standard statt Absturz beim Start.
            }
            return Default;
        }

        public static void Apply(string? theme)
        {
            string mode = Normalize(theme);
            Current = mode;

            var res = System.Windows.Application.Current?.Resources;
            if (res == null) return;   // z.B. in Unit-Tests ohne WPF-Anwendung

            // Gleicher Stil schon aktiv? Dann nichts tun – sonst stünde die
            // Design-Zeile beim Start doppelt im Log (Konstruktor + LoadSettings)
            // und alle Ressourcen würden unnötig neu erzeugt.
            if (_applied == mode) return;
            _applied = mode;

            try { App.LogApp("Design: " + mode); } catch { }

            bool flat = mode == Flat;
            bool shine = mode == Shine;

            // ---- Flächen -------------------------------------------------------
            // Zurückhaltend dosiert: die Verläufe trennen Flächen nur noch eben,
            // statt sie plastisch aufzuwölben (Wunsch: dezenter).
            Set(res, "SurfacePanelBrush", flat
                ? Solid("#D916161E")
                : shine
                    ? GradV("#EC242432", "#E0181821", "#D60E0E14")
                    : GradV("#E61A1A24", "#DC0F0F16"));

            Set(res, "SurfaceCardBrush", flat
                ? Solid("#0D0D12")
                : shine
                    ? GradV("#1A1A26", "#13131B", "#0C0C12")
                    : GradV("#141420", "#0E0E14"));

            Set(res, "SurfaceListCardBrush", flat
                ? Solid("#1A1A24")
                : shine
                    ? GradV("#2A2A38", "#1E1E28", "#16161E")
                    : GradV("#20202C", "#171720"));

            Set(res, "SurfaceRaisedBrush", flat
                ? Solid("#20202A")
                : shine
                    ? GradV("#3A3A4E", "#2A2A38", "#1C1C24")
                    : GradV("#2A2A3A", "#1E1E27"));

            Set(res, "SurfaceRaisedHoverBrush", flat
                ? Solid("#2A2A38")
                : shine
                    ? GradV("#47475C", "#333345", "#22222B")
                    : GradV("#333345", "#23232C"));

            Set(res, "SurfacePressedBrush", flat
                ? Solid("#00D2FF")
                : shine
                    ? GradV("#2FDDF2", "#12B4D4", "#0A93AC")
                    : GradV("#1FD6EF", "#0A9DB8"));

            Set(res, "SurfaceSunkenBrush", flat
                ? Solid("#1E1E26")
                : shine
                    ? GradV("#0D0D13", "#15151C", "#1C1C24")
                    : GradV("#101016", "#191921"));

            Set(res, "SurfaceAccentBrush", flat
                ? Solid("#0F2C3A")
                : shine
                    ? GradV("#1C4E66", "#123747", "#0B2633")
                    : GradV("#143C4E", "#0B2A36"));

            Set(res, "SurfaceAccentHoverBrush", flat
                ? Solid("#14394A")
                : shine
                    ? GradV("#22596F", "#174252", "#0D2C39")
                    : GradV("#19495C", "#0E3442"));

            Set(res, "SurfaceSelectedBrush", flat
                ? Solid("#0E2A35")
                : shine
                    ? GradV("#174459", "#0D3040", "#0A2530")
                    : GradV("#103444", "#0A222C"));

            // ---- Kanten (Licht oben links, Schatten unten rechts) --------------
            // Nur so hell, dass die Kante die Form liest – kein Glanzstreifen.
            Set(res, "BevelEdgeBrush", flat
                ? Solid("#2D2D3D")
                : Bevel(shine ? "#7C7C98" : "#4A4A60", shine ? "#22222C" : "#232330"));

            Set(res, "BevelHoverBrush", flat
                ? Solid("#475569")
                : Bevel(shine ? "#9494B0" : "#5C5C78", shine ? "#2A2A36" : "#2A2A38"));

            Set(res, "BevelActiveBrush", flat
                ? Solid("#00D2FF")
                : Bevel(shine ? "#7CE4F6" : "#4FD8EE", shine ? "#0C6E82" : "#0A6B80"));

            // Eingefasste (vertiefte) Kanten: umgekehrt – dunkel oben links.
            Set(res, "BevelInsetBrush", flat
                ? Solid("#2D2D3D")
                : Bevel(shine ? "#0B0B12" : "#0B0B12", shine ? "#3C3C4E" : "#3A3A4A"));

            Set(res, "BevelInsetHoverBrush", flat
                ? Solid("#475569")
                : Bevel(shine ? "#0B0B12" : "#0B0B12", shine ? "#4E4E62" : "#4A4A5C"));

            // ---- Schatten ------------------------------------------------------
            // Deutlich flacher als zuvor: Tiefe ja, dunkler Saum nein.
            Set(res, "PanelShadow", flat
                ? Shadow(0, 0, 0)
                : shine ? Shadow(14, 2, 0.35) : Shadow(12, 2, 0.32));

            Set(res, "RaisedShadow", flat
                ? Shadow(0, 0, 0)
                : shine ? Shadow(5, 1, 0.40) : Shadow(4, 1, 0.36));

            Changed?.Invoke();
        }

        // ------------------------------------------------------------------
        // Hilfen
        // ------------------------------------------------------------------

        private static void Set(ResourceDictionary res, string key, object value)
        {
            // Austauschen statt ändern: DynamicResource-Verbraucher übernehmen den
            // neuen Eintrag sofort, StaticResource-Verbraucher bleiben unberührt.
            res[key] = value;
        }

        private static SolidColorBrush Solid(string hex)
        {
            var brush = new SolidColorBrush(Parse(hex));
            brush.Freeze();
            return brush;
        }

        /// <summary>Vertikaler Verlauf (Licht oben → Schatten unten).</summary>
        private static LinearGradientBrush GradV(params string[] stops)
        {
            var brush = new LinearGradientBrush
            {
                StartPoint = new System.Windows.Point(0, 0),
                EndPoint = new System.Windows.Point(0, 1),
                MappingMode = BrushMappingMode.RelativeToBoundingBox
            };
            AddStops(brush, stops);
            brush.Freeze();
            return brush;
        }

        /// <summary>Diagonale Kante: hell oben links, dunkel unten rechts.</summary>
        private static LinearGradientBrush Bevel(string light, string dark)
        {
            var brush = new LinearGradientBrush
            {
                StartPoint = new System.Windows.Point(0, 0),
                EndPoint = new System.Windows.Point(1, 1),
                MappingMode = BrushMappingMode.RelativeToBoundingBox
            };
            AddStops(brush, new[] { light, dark });
            brush.Freeze();
            return brush;
        }

        private static void AddStops(LinearGradientBrush brush, string[] stops)
        {
            for (int i = 0; i < stops.Length; i++)
            {
                double offset = stops.Length == 1 ? 0 : (double)i / (stops.Length - 1);
                brush.GradientStops.Add(new GradientStop(Parse(stops[i]), offset));
            }
        }

        private static DropShadowEffect Shadow(double blur, double depth, double opacity)
        {
            var effect = new DropShadowEffect
            {
                Color = System.Windows.Media.Colors.Black,
                Direction = 270,           // nach unten
                BlurRadius = blur,
                ShadowDepth = depth,
                Opacity = opacity,
                RenderingBias = RenderingBias.Performance
            };
            effect.Freeze();
            return effect;
        }

        private static System.Windows.Media.Color Parse(string hex)
        {
            hex = hex.TrimStart('#');
            byte a = 255;
            if (hex.Length == 8)
            {
                a = Convert.ToByte(hex.Substring(0, 2), 16);
                hex = hex.Substring(2);
            }
            return System.Windows.Media.Color.FromArgb(
                a,
                Convert.ToByte(hex.Substring(0, 2), 16),
                Convert.ToByte(hex.Substring(2, 2), 16),
                Convert.ToByte(hex.Substring(4, 2), 16));
        }
    }
}

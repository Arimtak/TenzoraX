using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace TenzoraX.Tests
{
    /// <summary>
    /// Prüft die Vorgabe-Anordnung der Controller-Tasten (Anteile am Bild, linke obere Ecke).
    /// Anlass: Der Vorgabe-Satz war unvollständig – HOME fehlte darin, deshalb blieb der
    /// Guide-Knopf auf seiner XAML-Ausgangslage über dem Xbox-Logo hängen, und die übrigen
    /// Werte lagen neben den Merkmalen des Bildes (Bild ist 709x491, gerechnet wurde mit
    /// 400x230). Diese Tests halten beides fest: jede Taste hat Werte, und die Anordnung
    /// ist symmetrisch.
    /// </summary>
    public class GamepadLayoutTests
    {
        private static string RepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "TenzoraX.sln")))
                dir = dir.Parent;
            Assert.NotNull(dir);
            return dir!.FullName;
        }

        private static string XamlPath()
            => Path.Combine(RepoRoot(), "src", "TenzoraX", "MainWindow.xaml");

        private static Dictionary<string, double> Defaults()
            => MainWindow.DefaultRelativePositions();

        [Fact]
        public void JedeOberflaechenTaste_HatVorgabewerte()
        {
            var d = Defaults();
            foreach (var key in MainWindow.GamepadButtonKeys)
            {
                Assert.True(d.ContainsKey(key + "_X"), $"Vorgabe für {key}_X fehlt");
                Assert.True(d.ContainsKey(key + "_Y"), $"Vorgabe für {key}_Y fehlt");
            }
        }

        [Fact]
        public void JedeOberflaechenTaste_HatEineTasteImXaml()
        {
            string xaml = File.ReadAllText(XamlPath());
            foreach (var key in MainWindow.GamepadButtonKeys)
            {
                Assert.Contains($"x:Name=\"Btn_{key}\"", xaml);
            }
        }

        [Fact]
        public void Home_IstTeilDerVorgabe()
        {
            // Der eigentliche Fehler: HOME stand nur im XAML, nicht in der Vorgabe.
            var d = Defaults();
            Assert.True(d.ContainsKey("HOME_X"));
            Assert.True(d.ContainsKey("HOME_Y"));
        }

        [Fact]
        public void Werte_LiegenImBildbereich()
        {
            foreach (var kvp in Defaults())
            {
                Assert.InRange(kvp.Value, -0.20, 1.05);
            }
        }

        [Fact]
        public void Gesichtstasten_BildenSymmetrischeRaute()
        {
            var d = Defaults();
            // Y und A liegen übereinander, X und B auf gleicher Höhe.
            Assert.Equal(d["Y_X"], d["A_X"], 3);
            Assert.Equal(d["X_Y"], d["B_Y"], 3);
            // Reihenfolge im Kreuz: Y oben, X links, B rechts, A unten.
            Assert.True(d["Y_Y"] < d["X_Y"]);
            Assert.True(d["X_Y"] < d["A_Y"]);
            Assert.True(d["X_X"] < d["Y_X"]);
            Assert.True(d["Y_X"] < d["B_X"]);
            // Gleicher Radius in beiden Richtungen, umgerechnet über die Bildmaße 709x491.
            double radiusX = (d["B_X"] - d["X_X"]) / 2.0 * 709.0;
            double radiusY = (d["A_Y"] - d["Y_Y"]) / 2.0 * 491.0;
            Assert.InRange(Math.Abs(radiusX - radiusY), 0, 2.0);
        }

        [Fact]
        public void Mitte_IstSymmetrischUmHome()
        {
            var d = Defaults();
            // SELECT, HOME und START sind gleich breit – gleiche Abstände heißt symmetrisch.
            Assert.Equal(d["HOME_X"] - d["SELECT_X"], d["START_X"] - d["HOME_X"], 3);
            Assert.Equal(d["SELECT_Y"], d["HOME_Y"], 3);
            Assert.Equal(d["HOME_Y"], d["START_Y"], 3);
        }

        [Fact]
        public void Schultertasten_SindSpiegelgleich()
        {
            var d = Defaults();
            // L1/R1 (und L2/R2) stehen spiegelgleich; der Abstand zur Bildmitte ist gleich.
            Assert.Equal(0.503, (d["L1_X"] + d["R1_X"]) / 2.0 + 65.0 / 2.0 / 400.0, 2);
            Assert.Equal(d["L1_Y"], d["R1_Y"], 3);
            Assert.Equal(d["L1_X"], d["L2_X"], 3);
            Assert.Equal(d["R1_X"], d["R2_X"], 3);
            Assert.True(d["L2_Y"] < d["L1_Y"]);
        }

        [Fact]
        public void AlteAnordnung_WirdEinmaligErsetzt()
        {
            // Der zweite, an einem echten Profil gemessene Fehlerfall: Die gespeicherte
            // Anordnung hatte HOME bereits – aber geschätzt und im falschen Bezugsrahmen.
            // Eine Prüfung auf „HOME fehlt“ griff deshalb nicht, die wackelige Anordnung
            // blieb sichtbar. Jetzt entscheidet allein die Layout-Fassung.
            Assert.True(MainWindow.NeedsLayoutUpgrade(0, hasStoredPositions: true));
            Assert.True(MainWindow.NeedsLayoutUpgrade(MainWindow.CurrentButtonLayoutVersion - 1, hasStoredPositions: true));
        }

        [Fact]
        public void AktuelleAnordnung_BleibtErhalten()
        {
            // Kein Dauer-Überschreiben: mit aktueller Fassung bleibt die eigene Anordnung,
            // und ohne gespeicherte Positionen greift der normale Vorgabe-Weg.
            Assert.False(MainWindow.NeedsLayoutUpgrade(MainWindow.CurrentButtonLayoutVersion, hasStoredPositions: true));
            Assert.False(MainWindow.NeedsLayoutUpgrade(MainWindow.CurrentButtonLayoutVersion + 1, hasStoredPositions: true));
            Assert.False(MainWindow.NeedsLayoutUpgrade(0, hasStoredPositions: false));
        }

        [Fact]
        public void LayoutFassung_UeberlebtSpeichernUndLaden()
        {
            // Sonst liefe die Migration bei jedem Start erneut und würde Verschiebungen
            // des Nutzers jedes Mal wieder überschreiben.
            // Fensterlage muss gesetzt sein: WindowLeft/WindowTop sind standardmäßig
            // double.NaN, und System.Text.Json schreibt NaN nicht als JSON-Zahl.
            var settings = new AppSettings
            {
                ButtonLayoutVersion = MainWindow.CurrentButtonLayoutVersion,
                WindowLeft = 100,
                WindowTop = 100,
            };
            string json = System.Text.Json.JsonSerializer.Serialize(settings);
            var wieder = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(json)!;
            Assert.Equal(MainWindow.CurrentButtonLayoutVersion, wieder.ButtonLayoutVersion);
        }

        // Bildmaße der eingebauten Aufnahme (709x491 px) und die daraus abgeleitete Zeichenfläche
        // (Breite 400 DIP, Höhe über das Seitenverhältnis) – Grundlage aller DIP-Rechnungen.
        private const double ImageWidth = 709.0;
        private const double ImageHeight = 491.0;
        private const double CanvasWidth = 400.0;
        private static double CanvasHeight => CanvasWidth * ImageHeight / ImageWidth;

        /// <summary>Breite eines D-Pad-Pfeils, aus dem XAML gelesen statt doppelt gepflegt.</summary>
        private static double DpadGlyphWidth()
        {
            string xaml = File.ReadAllText(XamlPath());
            var m = Regex.Match(xaml, "x:Name=\"Btn_DPAD_UP\"[^>]*?Width=\"(\\d+(?:[.,]\\d+)?)\"");
            Assert.True(m.Success, "Btn_DPAD_UP mit Width im XAML nicht gefunden");
            return double.Parse(m.Groups[1].Value.Replace(',', '.'), System.Globalization.CultureInfo.InvariantCulture);
        }

        /// <summary>Mittelpunkt einer Taste in Zeichenflächen-Einheiten (DIP).</summary>
        private static (double X, double Y) Center(Dictionary<string, double> d, string key, double size)
            => (d[key + "_X"] * CanvasWidth + size / 2.0, d[key + "_Y"] * CanvasHeight + size / 2.0);

        [Fact]
        public void Dpad_PfeileBildenEinKompaktesKreuz()
        {
            var d = Defaults();
            double s = DpadGlyphWidth();
            var up = Center(d, "DPAD_UP", s);
            var down = Center(d, "DPAD_DOWN", s);
            var left = Center(d, "DPAD_LEFT", s);
            var right = Center(d, "DPAD_RIGHT", s);

            // Gleiche Armweite in beiden Richtungen, sonst sitzt das Kreuz schief.
            double rx = (right.X - left.X) / 2.0;
            double ry = (down.Y - up.Y) / 2.0;
            Assert.InRange(Math.Abs(rx - ry), 0, 0.5);

            // Die Pfeile liegen auf den Achsen des Kreuzes, nicht daneben.
            Assert.Equal(up.X, down.X, 2);
            Assert.Equal(left.Y, right.Y, 2);
            Assert.Equal(up.X, (left.X + right.X) / 2.0, 2);
            Assert.Equal(left.Y, (up.Y + down.Y) / 2.0, 2);

            // Zusammen ein Kreuz (52 DIP) im hellgrauen Plus der Vorlage (119 px = 67,1 DIP).
            double cluster = 2 * rx + s;
            double plus = 119.0 / ImageHeight * CanvasHeight;
            Assert.True(cluster < plus, $"Kreuz {cluster:0.0} DIP passt nicht ins Plus ({plus:0.0} DIP)");

            // Fuge zwischen den Pfeilen: dicht beieinander, ohne sich zu überlappen.
            Assert.InRange(2 * rx - s, 2.0, 8.0);

            // Und das Kreuz sitzt mittig auf dem Plus: dessen Mitte ist (249|257) px im Bild.
            double cx = 249.0 / ImageWidth * CanvasWidth;
            double cy = 257.0 / ImageHeight * CanvasHeight;
            Assert.InRange(Math.Abs(left.X + rx - cx), 0, 1.0);
            Assert.InRange(Math.Abs(up.Y + ry - cy), 0, 1.0);
        }

        [Fact]
        public void Dpad_PfeileHabenKeineEigeneFlaeche()
        {
            // Grund des Umbaus: Die vier Pfeile hatten je eine eigene dunkle Kachel und
            // zerschnitten damit das hellgraue Plus der Vorlage in vier Kästchen. Sie liegen
            // jetzt ohne Fläche auf dem Kreuz und leuchten beim Drücken selbst auf.
            foreach (var key in new[] { "DPAD_UP", "DPAD_DOWN", "DPAD_LEFT", "DPAD_RIGHT" })
                Assert.True(MainWindow.IsDpadKey(key));

            foreach (var key in MainWindow.GamepadButtonKeys)
                if (!key.StartsWith("DPAD_"))
                    Assert.False(MainWindow.IsDpadKey(key));
        }
    }
}

using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
// Das Projekt bindet WinForms ein; ohne Alias wären Color/Point mehrdeutig.
using Color = System.Windows.Media.Color;
using Point = System.Windows.Point;

namespace TenzoraX.Effects
{
    /// <summary>
    /// „GhostFibers" als WPF-Pixel-Shader: ein dunkelblaues Faserfeld mit leuchtenden
    /// Bändern, radialem Twist, zentraler Atmosphäre und weichem Grain.
    ///
    /// Die Mathematik ist unverändert aus dem react-bits-Registry-Eintrag
    /// „GhostFibers-JS-CSS" (WebGL2/GLSL) übernommen; übersetzt wurde nur die Umgebung.
    /// Quelle und Neubau-Anleitung liegen in <c>Effects/GhostFibers.fx</c>.
    ///
    /// Wichtig: Namen, Typen und Reihenfolge der Konstanten müssen zum HLSL passen –
    /// die Register werden über <see cref="ShaderEffect.PixelShaderConstantCallback"/>
    /// fest zugeordnet, damit keine Pack-Regeln erraten werden müssen.
    /// </summary>
    public sealed class GhostFibersEffect : ShaderEffect
    {
        private const string ShaderResource = "pack://application:,,,/Effects/GhostFibers.ps";

        private static readonly PixelShader? LoadedShader = TryLoadShader();

        /// <summary>False, wenn der Shader nicht geladen werden konnte (dann nichts einbauen).</summary>
        public static bool IsAvailable => LoadedShader != null;

        // --- Register 0/1: Farben (float4) ---
        public static readonly DependencyProperty LineColorProperty = DependencyProperty.Register(
            nameof(LineColor), typeof(Color), typeof(GhostFibersEffect),
            new UIPropertyMetadata(Color.FromRgb(0x14, 0x0E, 0x35), PixelShaderConstantCallback(0)));

        public static readonly DependencyProperty GlowColorProperty = DependencyProperty.Register(
            nameof(GlowColor), typeof(Color), typeof(GhostFibersEffect),
            new UIPropertyMetadata(Color.FromRgb(0x34, 0x37, 0xA0), PixelShaderConstantCallback(1)));

        // --- Register 2..24: Skalare (je eines, .x wird gelesen) ---
        public static readonly DependencyProperty TimeProperty = DoubleProperty("Time", 0.0, 2);
        public static readonly DependencyProperty SpeedProperty = DoubleProperty("Speed", 0.2, 3);
        public static readonly DependencyProperty ScaleProperty = DoubleProperty("Scale", 2.0, 4);
        public static readonly DependencyProperty RotationProperty = DoubleProperty("Rotation", 0.0, 5);
        public static readonly DependencyProperty RotationSpeedProperty = DoubleProperty("RotationSpeed", 0.25, 6);
        public static readonly DependencyProperty LayersProperty = DoubleProperty("Layers", 4.0, 7);
        public static readonly DependencyProperty WaveAmplitudeProperty = DoubleProperty("WaveAmplitude", 0.015, 8);
        public static readonly DependencyProperty WaveFrequencyProperty = DoubleProperty("WaveFrequency", 3.0, 9);
        public static readonly DependencyProperty WaveSpeedProperty = DoubleProperty("WaveSpeed", 0.15, 10);
        public static readonly DependencyProperty LayerSpeedProperty = DoubleProperty("LayerSpeed", 0.08, 11);
        public static readonly DependencyProperty TwistProperty = DoubleProperty("Twist", 0.1, 12);
        public static readonly DependencyProperty TwistFrequencyProperty = DoubleProperty("TwistFrequency", 5.0, 13);
        public static readonly DependencyProperty TwistSpeedProperty = DoubleProperty("TwistSpeed", 1.2, 14);
        public static readonly DependencyProperty LineFrequencyProperty = DoubleProperty("LineFrequency", 5.0, 15);
        public static readonly DependencyProperty LineSpacingProperty = DoubleProperty("LineSpacing", 2.0, 16);
        public static readonly DependencyProperty LineSharpnessProperty = DoubleProperty("LineSharpness", 16.0, 17);
        public static readonly DependencyProperty GlowFalloffProperty = DoubleProperty("GlowFalloff", 10.0, 18);
        public static readonly DependencyProperty GlowIntensityProperty = DoubleProperty("GlowIntensity", 1.6, 19);
        public static readonly DependencyProperty BrightnessProperty = DoubleProperty("Brightness", 2.0, 20);
        public static readonly DependencyProperty BlueBoostProperty = DoubleProperty("BlueBoost", 1.25, 21);
        public static readonly DependencyProperty VignetteProperty = DoubleProperty("Vignette", 0.8, 22);
        public static readonly DependencyProperty GrainProperty = DoubleProperty("Grain", 0.05, 23);
        public static readonly DependencyProperty LightModeProperty = DoubleProperty("LightMode", 0.0, 24);

        // --- Register 25: Auflösung (float2) ---
        public static readonly DependencyProperty ResolutionProperty = DependencyProperty.Register(
            nameof(Resolution), typeof(Point), typeof(GhostFibersEffect),
            new UIPropertyMetadata(new Point(1, 1), PixelShaderConstantCallback(25)));

        public GhostFibersEffect()
        {
            PixelShader = LoadedShader;
            UpdateShaderValue(LineColorProperty);
            UpdateShaderValue(GlowColorProperty);
            UpdateShaderValue(TimeProperty);
            UpdateShaderValue(SpeedProperty);
            UpdateShaderValue(ScaleProperty);
            UpdateShaderValue(RotationProperty);
            UpdateShaderValue(RotationSpeedProperty);
            UpdateShaderValue(LayersProperty);
            UpdateShaderValue(WaveAmplitudeProperty);
            UpdateShaderValue(WaveFrequencyProperty);
            UpdateShaderValue(WaveSpeedProperty);
            UpdateShaderValue(LayerSpeedProperty);
            UpdateShaderValue(TwistProperty);
            UpdateShaderValue(TwistFrequencyProperty);
            UpdateShaderValue(TwistSpeedProperty);
            UpdateShaderValue(LineFrequencyProperty);
            UpdateShaderValue(LineSpacingProperty);
            UpdateShaderValue(LineSharpnessProperty);
            UpdateShaderValue(GlowFalloffProperty);
            UpdateShaderValue(GlowIntensityProperty);
            UpdateShaderValue(BrightnessProperty);
            UpdateShaderValue(BlueBoostProperty);
            UpdateShaderValue(VignetteProperty);
            UpdateShaderValue(GrainProperty);
            UpdateShaderValue(LightModeProperty);
            UpdateShaderValue(ResolutionProperty);
        }

        public Color LineColor { get => (Color)GetValue(LineColorProperty); set => SetValue(LineColorProperty, value); }
        public Color GlowColor { get => (Color)GetValue(GlowColorProperty); set => SetValue(GlowColorProperty, value); }
        public double Time { get => (double)GetValue(TimeProperty); set => SetValue(TimeProperty, value); }
        public double Speed { get => (double)GetValue(SpeedProperty); set => SetValue(SpeedProperty, value); }
        public double Scale { get => (double)GetValue(ScaleProperty); set => SetValue(ScaleProperty, value); }
        public double Rotation { get => (double)GetValue(RotationProperty); set => SetValue(RotationProperty, value); }
        public double RotationSpeed { get => (double)GetValue(RotationSpeedProperty); set => SetValue(RotationSpeedProperty, value); }
        public double Layers { get => (double)GetValue(LayersProperty); set => SetValue(LayersProperty, value); }
        public double WaveAmplitude { get => (double)GetValue(WaveAmplitudeProperty); set => SetValue(WaveAmplitudeProperty, value); }
        public double WaveFrequency { get => (double)GetValue(WaveFrequencyProperty); set => SetValue(WaveFrequencyProperty, value); }
        public double WaveSpeed { get => (double)GetValue(WaveSpeedProperty); set => SetValue(WaveSpeedProperty, value); }
        public double LayerSpeed { get => (double)GetValue(LayerSpeedProperty); set => SetValue(LayerSpeedProperty, value); }
        public double Twist { get => (double)GetValue(TwistProperty); set => SetValue(TwistProperty, value); }
        public double TwistFrequency { get => (double)GetValue(TwistFrequencyProperty); set => SetValue(TwistFrequencyProperty, value); }
        public double TwistSpeed { get => (double)GetValue(TwistSpeedProperty); set => SetValue(TwistSpeedProperty, value); }
        public double LineFrequency { get => (double)GetValue(LineFrequencyProperty); set => SetValue(LineFrequencyProperty, value); }
        public double LineSpacing { get => (double)GetValue(LineSpacingProperty); set => SetValue(LineSpacingProperty, value); }
        public double LineSharpness { get => (double)GetValue(LineSharpnessProperty); set => SetValue(LineSharpnessProperty, value); }
        public double GlowFalloff { get => (double)GetValue(GlowFalloffProperty); set => SetValue(GlowFalloffProperty, value); }
        public double GlowIntensity { get => (double)GetValue(GlowIntensityProperty); set => SetValue(GlowIntensityProperty, value); }
        public double Brightness { get => (double)GetValue(BrightnessProperty); set => SetValue(BrightnessProperty, value); }
        public double BlueBoost { get => (double)GetValue(BlueBoostProperty); set => SetValue(BlueBoostProperty, value); }
        public double Vignette { get => (double)GetValue(VignetteProperty); set => SetValue(VignetteProperty, value); }
        public double Grain { get => (double)GetValue(GrainProperty); set => SetValue(GrainProperty, value); }
        public double LightMode { get => (double)GetValue(LightModeProperty); set => SetValue(LightModeProperty, value); }
        public Point Resolution { get => (Point)GetValue(ResolutionProperty); set => SetValue(ResolutionProperty, value); }

        /// <summary>Kürzel für die vielen gleich gebauten Zahlen-Konstanten.</summary>
        private static DependencyProperty DoubleProperty(string name, double defaultValue, int register) =>
            DependencyProperty.Register(name, typeof(double), typeof(GhostFibersEffect),
                new UIPropertyMetadata(defaultValue, PixelShaderConstantCallback(register)));

        private static PixelShader? TryLoadShader()
        {
            try
            {
                var uri = new Uri(ShaderResource, UriKind.RelativeOrAbsolute);
                var info = System.Windows.Application.GetResourceStream(uri);
                if (info == null)
                {
                    App.LogApp("GhostFibers: Shader-Ressource nicht gefunden (" + ShaderResource + ")");
                    return null;
                }

                using (var stream = info.Stream)
                {
                    // WPF parst hier den Shader-Header (Shader Model) und wirft bei einer
                    // ungültigen Datei – deshalb bleibt der Aufruf im try/catch.
                    var shader = new PixelShader();
                    shader.SetStreamSource(stream);
                    App.LogApp("GhostFibers: Pixel-Shader geladen");
                    return shader;
                }
            }
            catch (Exception ex)
            {
                // Kein harter Fehler: das Fenster bleibt dann ohne Hintergrund-Effekt nutzbar.
                App.LogApp("GhostFibers: Shader konnte nicht geladen werden – " + ex.Message);
                return null;
            }
        }
    }
}

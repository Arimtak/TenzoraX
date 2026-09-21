// GhostFibers - Portierung des WebGL2-Fragment-Shaders aus dem react-bits-Registry-
// Eintrag "GhostFibers-JS-CSS" (Registry: https://reactbits.dev/r/GhostFibers-JS-CSS.json).
//
// Die Mathematik ist unveraendert: mehrschichtiges Faserfeld mit Welle, radialem Twist,
// Glow-Baendern, zentraler Atmosphaere, Vignette, Belichtung, Blau-Anhebung und
// mehrstufigem Grain. Angepasst ist nur die Umgebung:
//   - WebGL2/GLSL ES 3.0  ->  HLSL Shader Model 3.0 (WPF ShaderEffect)
//   - gl_FragCoord        ->  uv (0..1, Ursprung oben links) * Resolution
//   - Uniforms            ->  je ein eigenes Register, gesetzt per PixelShaderConstantCallback
//
// Neu bauen (Windows SDK, fxc.exe):
//   fxc /T ps_3_0 /E main /Fo GhostFibers.ps GhostFibers.fx
//
// Eingabe-Textur ist der Inhalt des Elements, auf dem der Effekt liegt; sie wird hier
// nicht gelesen, der Shader erzeugt die Flaeche vollstaendig selbst (daher optional).

sampler2D InputTexture;

float4 LineColor      : register(c0);
float4 GlowColor      : register(c1);
float  Time           : register(c2);
float  Speed          : register(c3);
float  Scale          : register(c4);
float  Rotation       : register(c5);
float  RotationSpeed  : register(c6);
float  Layers         : register(c7);
float  WaveAmplitude  : register(c8);
float  WaveFrequency  : register(c9);
float  WaveSpeed      : register(c10);
float  LayerSpeed     : register(c11);
float  Twist          : register(c12);
float  TwistFrequency : register(c13);
float  TwistSpeed     : register(c14);
float  LineFrequency  : register(c15);
float  LineSpacing    : register(c16);
float  LineSharpness  : register(c17);
float  GlowFalloff    : register(c18);
float  GlowIntensity  : register(c19);
float  Brightness     : register(c20);
float  BlueBoost      : register(c21);
float  Vignette       : register(c22);
float  Grain          : register(c23);
float  LightMode      : register(c24);
float2 Resolution     : register(c25);

#define MAX_LAYERS 10

// Entspricht GLSL mat2(c, -s, s, c): dort spaltenweise, in HLSL zeilenweise.
float2x2 Rotate2d(float angle)
{
    float s = sin(angle);
    float c = cos(angle);
    return float2x2(c, s, -s, c);
}

float GrainHash(float2 cell)
{
    cell = floor(cell);
    float hash = 52.9829189 * frac(dot(cell, float2(0.065, 0.005)));
    return frac(hash);
}

float LayeredGrain(float2 fragmentPixel)
{
    float2 cell = fmod(fragmentPixel + float2(Time * 30.0, -Time * 21.0), 1024.0);
    float2 rotated = mul(float2x2(0.8, -0.5, 0.5, 0.8), cell);
    float grain = 0.0;
    grain += 0.40 * GrainHash(rotated);
    grain += 0.25 * GrainHash(rotated * 2.0 + 17.0);
    grain += 0.20 * GrainHash(rotated * 4.0 + 47.0);
    grain += 0.10 * GrainHash(rotated * 8.0 + 113.0);
    grain += 0.05 * GrainHash(rotated * 16.0 + 191.0);
    return grain;
}

float4 main(float2 uv : TEXCOORD) : COLOR
{
    float2 resolution = max(Resolution, float2(1.0, 1.0));

    // GLSL rechnet vom unteren Rand aus - deshalb die y-Spiegelung.
    float2 fragmentPixel = float2(uv.x * resolution.x, (1.0 - uv.y) * resolution.y);
    float2 coord = (2.0 * fragmentPixel - resolution) / resolution.y;

    float time = Time * Speed;

    float3 backdrop = lerp(float3(0.070588, 0.058824, 0.090196), float3(1.0, 1.0, 1.0), step(0.5, LightMode));
    float3 centerTone = max(LineColor.rgb * 0.85567 - GlowColor.rgb * 0.06186, float3(0.0, 0.0, 0.0));
    float3 cloudTone = LineColor.rgb * 0.19588 + GlowColor.rgb * 0.2268;

    float2 p = coord / max(Scale, 0.05);
    p = mul(Rotate2d(radians(Rotation) + time * RotationSpeed), p);

    float3 color = float3(0.0, 0.0, 0.0);
    float fiberField = 0.0;

    [unroll]
    for (int index = 0; index < MAX_LAYERS; index++)
    {
        float fi = float(index) + 1.0;
        if (fi > Layers) break;

        p += WaveAmplitude * sin(p.yx * fi * WaveFrequency + time * (WaveSpeed + fi * LayerSpeed));

        float radius = length(p);
        float polarAngle = atan2(p.y, p.x);
        polarAngle += sin(radius * TwistFrequency - time * TwistSpeed + fi) * Twist;
        p = float2(cos(polarAngle), sin(polarAngle)) * radius;

        float lines = abs(sin(p.x * (LineFrequency + fi * LineSpacing) + sin(p.y * 3.0 + time)));
        lines = pow(max(0.0, 1.0 - lines), LineSharpness);
        fiberField += lines / fi;
        color += LineColor.rgb * lines / fi;

        float glow = exp(-GlowFalloff * abs(sin(p.x * 3.0 + time + fi)));
        color += GlowColor.rgb * glow * GlowIntensity / (fi * 2.0);
    }

    float center = exp(-2.2 * dot(coord, coord));
    color += centerTone * center;

    float cloud = exp(-1.5 * length(coord + float2(sin(time * 0.3) * 0.25, cos(time * 0.25) * 0.18)));
    color += cloudTone * cloud;

    float vign = 1.0 - smoothstep(0.35, 1.45, length(coord));
    color *= lerp(1.0 - Vignette, 1.0, vign);
    color = 1.0 - exp(-color * Brightness);
    color.b *= BlueBoost;

    float3 outputColor;
    if (LightMode > 0.5)
    {
        float edgeFade = lerp(1.0 - Vignette, 1.0, vign);
        float fibers = pow(smoothstep(0.12, 1.05, fiberField) * edgeFade, 1.5);
        float atmosphere = (center * 0.025 + cloud * 0.015) * edgeFade;
        float3 fiberInk = lerp(backdrop, LineColor.rgb, 0.52);
        float3 airColor = lerp(backdrop, GlowColor.rgb, 0.16);

        outputColor = lerp(backdrop, airColor, atmosphere);
        outputColor = lerp(outputColor, fiberInk, fibers * 0.3);
    }
    else
    {
        outputColor = backdrop + color;
    }

    float noise = (LayeredGrain(fragmentPixel) - 0.5) * Grain;
    outputColor = clamp(outputColor + noise, float3(0.0, 0.0, 0.0), float3(1.0, 1.0, 1.0));
    return float4(outputColor, 1.0);
}

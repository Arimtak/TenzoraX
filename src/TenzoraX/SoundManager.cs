using System;
using System.Collections.Generic;
using System.IO;
using System.Media;
using System.Threading.Tasks;

namespace TenzoraX
{
    public enum SoundPreset
    {
        SoftPling,
    }

    public static class SoundManager
    {
        public static bool Enabled { get; set; } = true;
        public static double Volume { get; set; } = 0.5;
        public static SoundPreset Preset { get; set; } = SoundPreset.SoftPling;

        private static readonly Dictionary<SoundPreset, byte[]> _cache = new();

        public static void PlayConfirmation()
        {
            if (!Enabled || Volume <= 0) return;
            byte[]? wav = GetPresetWav(Preset);
            if (wav == null) return;

            double vol = Math.Clamp(Volume, 0.0, 1.0);
            Task.Run(() =>
            {
                try
                {
                    byte[] volAdjusted = AdjustVolume(wav, vol);
                    using var ms = new MemoryStream(volAdjusted);
                    using var player = new SoundPlayer(ms);
                    player.PlaySync();
                }
                catch { }
            });
        }

        private static byte[]? GetPresetWav(SoundPreset preset)
        {
            if (_cache.TryGetValue(preset, out var cached))
                return cached;

            byte[] generated = preset switch
            {
                SoundPreset.SoftPling => GenerateSoftPling(),
                _ => GenerateSoftPling(),
            };
            _cache[preset] = generated;
            return generated;
        }

        // Soft Pling: warm, glassy confirmation tone with frequency sweep
        private static byte[] GenerateSoftPling()
        {
            int sampleRate = 44100;
            int channels = 1;
            int bitsPerSample = 16;
            int durationMs = 160;
            int totalSamples = sampleRate * durationMs / 1000;
            int dataSize = totalSamples * channels * (bitsPerSample / 8);

            int attackSamples = (int)(sampleRate * 0.003);   // 3 ms attack
            int decayStart = (int)(sampleRate * 0.050);      // decay starts at 50 ms
            double releaseMs = 0.050;                         // 50 ms release
            int releaseSamples = (int)(sampleRate * releaseMs);
            double peakAmp = 14000;

            using var ms = new MemoryStream();
            using var w = new BinaryWriter(ms);

            w.Write(new[] { (byte)'R', (byte)'I', (byte)'F', (byte)'F' });
            w.Write(36 + dataSize);
            w.Write(new[] { (byte)'W', (byte)'A', (byte)'V', (byte)'E' });
            w.Write(new[] { (byte)'f', (byte)'m', (byte)'t', (byte)' ' });
            w.Write(16);
            w.Write((short)1);
            w.Write((short)channels);
            w.Write(sampleRate);
            w.Write(sampleRate * channels * (bitsPerSample / 8));
            w.Write((short)(channels * (bitsPerSample / 8)));
            w.Write((short)bitsPerSample);
            w.Write(new[] { (byte)'d', (byte)'a', (byte)'t', (byte)'a' });
            w.Write(dataSize);

            for (int i = 0; i < totalSamples; i++)
            {
                double t = (double)i / sampleRate;
                double env;

                if (i < attackSamples)
                    env = (double)i / attackSamples;
                else if (i >= totalSamples - releaseSamples)
                    env = Math.Pow((double)(totalSamples - i) / releaseSamples, 1.5);
                else
                {
                    double decayPos = (double)(i - decayStart) / (totalSamples - releaseSamples - decayStart);
                    if (decayPos < 0) decayPos = 0;
                    env = Math.Pow(1 - decayPos * 0.85, 2.0);
                }

                // Frequency sweep: 800 Hz -> 1500 Hz (40 ms) -> 950 Hz (end)
                double freq;
                double sweepT = t / 0.040;
                if (t < 0.040)
                    freq = 800 + sweepT * (1500 - 800);
                else
                    freq = 1500 - ((t - 0.040) / 0.120) * (1500 - 950);

                double val = Math.Sin(2 * Math.PI * freq * t);
                val += 0.25 * Math.Sin(2 * Math.PI * freq * 2 * t); // 2nd harmonic
                val += 0.08 * Math.Sin(2 * Math.PI * freq * 3 * t); // 3rd harmonic (sparkle)

                short sample = (short)(val * env * peakAmp);
                w.Write(sample);
            }

            return ms.ToArray();
        }

        private static byte[] AdjustVolume(byte[] wav, double volume)
        {
            if (volume >= 1.0) return wav;

            var result = new byte[wav.Length];
            Buffer.BlockCopy(wav, 0, result, 0, 44);

            for (int i = 44; i < wav.Length - 1; i += 2)
            {
                short sample = (short)(wav[i] | (wav[i + 1] << 8));
                sample = (short)(sample * volume);
                result[i] = (byte)(sample & 0xFF);
                result[i + 1] = (byte)((sample >> 8) & 0xFF);
            }
            return result;
        }
    }
}

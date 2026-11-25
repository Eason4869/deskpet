using System;

namespace Dafeiyu
{
    internal enum LiquidThemeCue { Drop, Splash, Flow, Finish, Short }

    internal static class LiquidThemeAudio
    {
        internal const int SampleRate = 48000;
        const int FramesPerMillisecond = SampleRate / 1000;
        const double MaxFlowSeconds = 2;
        const double Tau = 2 * Math.PI;

        internal static byte[] Render(LiquidThemeCue cue, bool dark, TimeSpan flowDuration)
        {
            double[] mix;
            switch (cue)
            {
                case LiquidThemeCue.Drop:
                    mix = new double[280 * FramesPerMillisecond];
                    AddTexture(mix, dark, 0x52721d3bu, .155, 78);
                    AddBubble(mix, dark, 5, 313, 162, 16, .100);
                    AddBubble(mix, dark, 43, 485, -175, 12, .045);
                    AddBubble(mix, dark, 104, 228, 94, 17, .033);
                    AddBubble(mix, dark, 162, 201, 61, 17, .022);
                    break;
                case LiquidThemeCue.Splash:
                    mix = new double[160 * FramesPerMillisecond];
                    AddTexture(mix, dark, 0x9c74da21u, .265, 37);
                    AddBubble(mix, dark, 1, 219, -72, 10, .073);
                    AddBubble(mix, dark, 13, 472, -202, 8, .050);
                    AddBubble(mix, dark, 34, 317, 133, 9, .037);
                    AddBubble(mix, dark, 51, 578, -132, 9, .025);
                    AddBubble(mix, dark, 84, 278, 98, 11, .014);
                    break;
                case LiquidThemeCue.Flow:
                    int frameCount = (int)Math.Round(SampleRate * Math.Max(0, Math.Min(MaxFlowSeconds, flowDuration.TotalSeconds)),
                        MidpointRounding.AwayFromZero);
                    if (frameCount == 0) return new byte[0];
                    mix = new double[frameCount];
                    AddTexture(mix, dark, 0xde6c373fu, .169, 0);
                    AddFlowBubbles(mix, dark);
                    break;
                case LiquidThemeCue.Finish:
                    mix = new double[135 * FramesPerMillisecond];
                    AddTexture(mix, dark, 0x72dd94b9u, .115, 44);
                    AddBubble(mix, dark, 7, 303, -116, 16, .054);
                    AddBubble(mix, dark, 46, 214, 65, 14, .027);
                    break;
                case LiquidThemeCue.Short:
                    mix = new double[155 * FramesPerMillisecond];
                    AddTexture(mix, dark, 0xea1811dfu, .125, 37);
                    AddBubble(mix, dark, 5, 333, 148, 15, .072);
                    AddBubble(mix, dark, 39, 431, -146, 12, .034);
                    break;
                default:
                    throw new ArgumentOutOfRangeException("cue");
            }

            byte[] pcm = new byte[mix.Length * 2];
            for (int frame = 0; frame < mix.Length; frame++)
            {
                double fadeIn = Smooth(frame / (5.0 * FramesPerMillisecond));
                double fadeOut = Smooth((mix.Length - 1 - frame) / (22.0 * FramesPerMillisecond));
                double amplitude = Math.Max(-1, Math.Min(1, mix[frame] * fadeIn * fadeOut));
                int sample = (int)Math.Round(amplitude * short.MaxValue);
                pcm[frame * 2] = (byte)(sample & 0xff);
                pcm[frame * 2 + 1] = (byte)((sample >> 8) & 0xff);
            }
            return pcm;
        }

        // Separate the body of the water from the finer spray with one-pole filters.
        static void AddTexture(double[] mix, bool dark, uint seed, double gain, double decayMs)
        {
            double bassCoefficient = FilterCoefficient(dark ? 83 : 130);
            double bodyCoefficient = FilterCoefficient(dark ? 990 : 1920);
            double sprayCoefficient = FilterCoefficient(dark ? 1940 : 4620);
            double surfaceCoefficient = FilterCoefficient(dark ? 2100 : 3650);
            double turbulenceCoefficient = FilterCoefficient(125);
            double decay = decayMs > 0 ? Decay(decayMs) : 1;
            double bass = 0, body = 0, spray = 0, surface = 0, turbulence = 0;
            for (int frame = 0; frame < mix.Length; frame++)
            {
                double white = NextNoise(ref seed);
                bass += (white - bass) * bassCoefficient;
                body += (white - body) * bodyCoefficient;
                spray += (white - spray) * sprayCoefficient;
                turbulence += (white - turbulence) * turbulenceCoefficient;
                double texture = (body - bass) * .8 + (spray - body) * .26;
                surface += (texture - surface) * surfaceCoefficient;
                mix[frame] += surface * (1 + turbulence * 2.8) * gain;
                gain *= decay;
            }
        }

        static void AddFlowBubbles(double[] mix, bool dark)
        {
            uint seed = 0x26d0a5e1u;
            for (int offsetMs = 11; offsetMs * FramesPerMillisecond < mix.Length;)
            {
                double frequency = 282 + 117 * NextNoise(ref seed);
                double glide = 101 * NextNoise(ref seed);
                double decayMs = 12 + 4 * NextNoise(ref seed);
                double gain = .026 + .013 * NextNoise(ref seed);
                AddBubble(mix, dark, offsetMs, frequency, glide, decayMs, gain);
                offsetMs += 38 + (int)(21 * NextNoise(ref seed));
            }
        }

        // A damped, bending resonance gives each bubble a short, shifting tone.
        static void AddBubble(double[] mix, bool dark, int offsetMs, double frequency, double glideHz, double decayMs, double gain)
        {
            int start = offsetMs * FramesPerMillisecond;
            int length = Math.Min(mix.Length - start, (int)(decayMs * 6 * FramesPerMillisecond));
            double pitch = dark ? .64 : 1;
            double step = Tau * pitch * frequency / SampleRate;
            double glide = Tau * pitch * glideHz / SampleRate;
            double glideDecay = Decay(14);
            double amplitudeDecay = Decay(decayMs);
            double phase = 0;
            for (int frame = 0; frame < length; frame++)
            {
                phase += step + glide;
                double attack = Smooth(frame / (3.0 * FramesPerMillisecond));
                double release = Smooth((length - 1 - frame) / (6.0 * FramesPerMillisecond));
                double resonance = Math.Sin(phase) + .11 * Math.Sin(phase * 2.07);
                mix[start + frame] += resonance * attack * release * gain;
                glide *= glideDecay;
                gain *= amplitudeDecay;
            }
        }

        static double FilterCoefficient(double cutoff) { return 1 - Math.Exp(-Tau * cutoff / SampleRate); }
        static double Decay(double milliseconds) { return Math.Exp(-1 / (milliseconds * FramesPerMillisecond)); }

        static double Smooth(double value)
        {
            double t = Math.Max(0, Math.Min(1, value));
            return t * t * (3 - 2 * t);
        }

        static double NextNoise(ref uint seed)
        {
            seed ^= seed << 13;
            seed ^= seed >> 17;
            seed ^= seed << 5;
            return seed * (2.0 / uint.MaxValue) - 1;
        }
    }
}

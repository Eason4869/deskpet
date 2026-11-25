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

        internal static byte[] Render(LiquidThemeCue cue, bool dark, TimeSpan flowDuration, string motion = ThemeTransitionMotion.Flow)
        {
            double[] mix;
            if (motion == ThemeTransitionMotion.Ripple || motion == ThemeTransitionMotion.Refraction || motion == ThemeTransitionMotion.Zipper)
                mix = MotionCue(cue, dark, flowDuration, motion);
            else switch (cue)
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
                    mix = new double[TravelFrames(flowDuration)];
                    AddTexture(mix, dark, 0xde6c373fu, .169, 0);
                    AddFlowBubbles(mix, dark);
                    AddSurfaceSpray(mix, dark);
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

        static int TravelFrames(TimeSpan duration)
        {
            return (int)Math.Round(SampleRate * Math.Max(0, Math.Min(MaxFlowSeconds, duration.TotalSeconds)), MidpointRounding.AwayFromZero);
        }

        static double[] MotionCue(LiquidThemeCue cue, bool dark, TimeSpan duration, string motion)
        {
            int frames = cue == LiquidThemeCue.Flow ? TravelFrames(duration) :
                (cue == LiquidThemeCue.Splash ? 196 : cue == LiquidThemeCue.Drop ? 150 : 145) * FramesPerMillisecond;
            var mix = new double[frames];
            if (motion == ThemeTransitionMotion.Ripple)
            {
                if (cue == LiquidThemeCue.Flow)
                {
                    AddTexture(mix, dark, 0xb653ad71u, .045, 0);
                    for (int i = 0; i < 4; i++)
                        AddBubble(mix, dark, (int)(duration.TotalMilliseconds * i * .23), 270 + i * 42, -43, 31, .045 - i * .006);
                }
                else
                {
                    double pitch = cue == LiquidThemeCue.Splash ? 255 : 330;
                    AddBubble(mix, dark, 6, pitch, -74, 25, .062);
                    AddBubble(mix, dark, 52, pitch * 1.37, -53, 22, .041);
                    AddTexture(mix, dark, 0x115ac787u, .055, 47);
                }
            }
            else if (motion == ThemeTransitionMotion.Refraction)
            {
                AddAirSweep(mix, dark, 0x85ab3177u, cue == LiquidThemeCue.Flow ? .105 : .071);
                if (cue != LiquidThemeCue.Flow)
                {
                    AddBubble(mix, dark, 5, cue == LiquidThemeCue.Splash ? 527 : 432, 61, 19, .041);
                    AddBubble(mix, dark, 45, 654, -100, 18, .020);
                }
            }
            else
            {
                uint seed = 0xa63d531bu;
                if (cue == LiquidThemeCue.Flow)
                {
                    AddAirSweep(mix, dark, 0xd36e17b1u, .067);
                    for (int offset = 0; offset * FramesPerMillisecond < mix.Length;)
                    {
                        AddNoisePulse(mix, dark, ref seed, offset, 17, .124 + .049 * NextNoise(ref seed), 2100);
                        offset += 27 + (int)(7 * NextNoise(ref seed));
                    }
                }
                else
                {
                    AddNoisePulse(mix, dark, ref seed, 6, 31, .20, 1550);
                    AddNoisePulse(mix, dark, ref seed, 33, 20, .097, 2150);
                    AddBubble(mix, dark, 5, cue == LiquidThemeCue.Finish ? 266 : 372, -78, 16, .042);
                }
            }
            return mix;
        }

        static void AddAirSweep(double[] mix, bool dark, uint seed, double gain)
        {
            double lower = FilterCoefficient(dark ? 300 : 490);
            double start = FilterCoefficient(dark ? 730 : 1050);
            double range = FilterCoefficient(dark ? 1910 : 3100) - start;
            double low = 0, band = 0;
            for (int i = 0; i < mix.Length; i++)
            {
                double position = (i + .5) / mix.Length;
                double envelope = Math.Sin(Math.PI * position);
                double noise = NextNoise(ref seed);
                low += (noise - low) * lower;
                band += (noise - band) * (start + range * envelope);
                mix[i] += (band - low) * gain * envelope;
            }
        }

        static void AddSurfaceSpray(double[] mix, bool dark)
        {
            uint seed = 0xc7eb713fu;
            for (int offset = 20; offset * FramesPerMillisecond < mix.Length;)
            {
                AddNoisePulse(mix, dark, ref seed, offset, 48, .102 + .032 * NextNoise(ref seed), 3500);
                offset += 95 + (int)(34 * NextNoise(ref seed));
            }
        }

        static void AddNoisePulse(double[] mix, bool dark, ref uint seed, int offset, int milliseconds, double gain, double cutoff)
        {
            int start = offset * FramesPerMillisecond;
            int length = Math.Min(mix.Length - start, milliseconds * FramesPerMillisecond);
            double filter = FilterCoefficient(cutoff * (dark ? .63 : 1));
            double decay = Decay(milliseconds * .28);
            double value = 0;
            for (int i = 0; i < length; i++)
            {
                value += (NextNoise(ref seed) - value) * filter;
                double attack = Smooth(i / (2.0 * FramesPerMillisecond));
                double release = Smooth((length - 1 - i) / (4.0 * FramesPerMillisecond));
                mix[start + i] += value * gain * attack * release;
                gain *= decay;
            }
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

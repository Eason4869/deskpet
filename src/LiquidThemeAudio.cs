using System;

namespace Dafeiyu
{
    internal enum LiquidThemeCue { Drop, Splash, Flow, Finish, Short }

    internal static class LiquidThemeAudio
    {
        internal const int SampleRate = 48000;
        const int FramesPerMillisecond = SampleRate / 1000;
        const int PullMilliseconds = 174, ContactMilliseconds = 160, FinishMilliseconds = 190, ShortMilliseconds = 152;
        const double Tau = 2 * Math.PI;
        const double G3 = 196.00, C4 = 261.63, D4 = 293.66, E4 = 329.63, G4 = 392.00, A4 = 440.00, C5 = 523.25;
        static readonly double[] WaterDrops = { D4, G4, E4, C4, E4, A4, G4, D4 };
        static readonly double[] RippleEchoes = { G4, E4, C4, G3 };
        static readonly double[] GlassNotes = { G4, C5, A4, E4 };
        // Five consecutive recordings in LiquidThemeCue order, all at 48 kHz.
        static readonly int[] ZipperMilliseconds = { PullMilliseconds, ContactMilliseconds, 1221, FinishMilliseconds, ShortMilliseconds };
        static readonly Lazy<byte[]> ZipperDark = new Lazy<byte[]>(() => LoadZipper("theme-zipper-to-dark"));
        static readonly Lazy<byte[]> ZipperLight = new Lazy<byte[]>(() => LoadZipper("theme-zipper-to-light"));

        internal static byte[] Render(LiquidThemeCue cue, bool dark, TimeSpan flowDuration, string motion = ThemeTransitionMotion.Flow)
        {
            int frames;
            switch (cue)
            {
                case LiquidThemeCue.Drop: frames = PullMilliseconds * FramesPerMillisecond; break;
                case LiquidThemeCue.Splash: frames = ContactMilliseconds * FramesPerMillisecond; break;
                case LiquidThemeCue.Flow:
                    frames = (int)Math.Round(SampleRate * Math.Max(0, Math.Min(2, flowDuration.TotalSeconds)),
                        MidpointRounding.AwayFromZero);
                    break;
                case LiquidThemeCue.Finish: frames = FinishMilliseconds * FramesPerMillisecond; break;
                case LiquidThemeCue.Short: frames = ShortMilliseconds * FramesPerMillisecond; break;
                default: throw new ArgumentOutOfRangeException("cue");
            }
            if (motion == ThemeTransitionMotion.Zipper) return RenderZipper(cue, dark, frames);
            var mix = new double[frames];
            double pitch = dark ? .75 : 1;
            switch (motion)
            {
                case ThemeTransitionMotion.Ripple: Ripple(mix, cue, pitch, dark); break;
                case ThemeTransitionMotion.Refraction: Refraction(mix, cue, pitch, dark); break;
                default: Water(mix, cue, pitch, dark); break;
            }
            var pcm = new byte[frames * 2];
            for (int frame = 0; frame < frames; frame++)
            {
                double edge = Smooth(frame / (6.0 * FramesPerMillisecond)) *
                    Smooth((frames - 1 - frame) / (18.0 * FramesPerMillisecond));
                int sample = (int)Math.Round(Math.Max(-1, Math.Min(1, mix[frame] * edge)) * short.MaxValue);
                pcm[frame * 2] = (byte)(sample & 0xff);
                pcm[frame * 2 + 1] = (byte)((sample >> 8) & 0xff);
            }
            return pcm;
        }

        static void Water(double[] mix, LiquidThemeCue cue, double pitch, bool dark)
        {
            switch (cue)
            {
                case LiquidThemeCue.Drop:
                    Note(mix, pitch, 0, E4, 115, .098, 38);
                    Note(mix, pitch, 65, G4, 92, .036, -22);
                    break;
                case LiquidThemeCue.Splash:
                    Note(mix, pitch, 0, C4, 105, .091, 38);
                    Note(mix, pitch, 24, G4, 107, .048, -25);
                    Note(mix, pitch, 56, E4, 90, .028, 17);
                    break;
                case LiquidThemeCue.Flow:
                    Swell(mix, pitch * G3, .014);
                    for (int offset = 18, note = 0; offset < mix.Length / FramesPerMillisecond; offset += 113, note++)
                        Note(mix, pitch, offset, WaterDrops[note % WaterDrops.Length], 112, .037, note % 2 == 0 ? 24 : -15);
                    break;
                case LiquidThemeCue.Finish:
                    Settle(mix, pitch, dark, D4, .078);
                    break;
                case LiquidThemeCue.Short:
                    Note(mix, pitch, 0, E4, 140, .088, -19);
                    Note(mix, pitch, 29, G4, 114, .025);
                    break;
            }
        }

        static void Ripple(double[] mix, LiquidThemeCue cue, double pitch, bool dark)
        {
            switch (cue)
            {
                case LiquidThemeCue.Drop:
                    Note(mix, pitch, 0, G4, 152, .100, -12);
                    break;
                case LiquidThemeCue.Splash:
                    Note(mix, pitch, 0, C4, 140, .080, 11);
                    Note(mix, pitch, 30, G4, 122, .043);
                    break;
                case LiquidThemeCue.Flow:
                    Swell(mix, pitch * C4, .012);
                    for (int note = 0; note < RippleEchoes.Length; note++)
                        Note(mix, pitch, TravelOffset(mix, .04 + note * .225), RippleEchoes[note], 224, .057 - note * .012, 12);
                    break;
                case LiquidThemeCue.Finish:
                    Settle(mix, pitch, dark, C4, .075);
                    break;
                case LiquidThemeCue.Short:
                    Note(mix, pitch, 0, G4, 145, .082, -16);
                    break;
            }
        }

        static void Refraction(double[] mix, LiquidThemeCue cue, double pitch, bool dark)
        {
            const double glass = .19;
            switch (cue)
            {
                case LiquidThemeCue.Drop:
                    Note(mix, pitch, 0, E4, 156, .080, -13, glass);
                    Note(mix, pitch, 34, G4, 129, .039, 0, glass);
                    break;
                case LiquidThemeCue.Splash:
                    Note(mix, pitch, 0, A4, 140, .073, -11, glass);
                    Note(mix, pitch, 27, C5, 128, .035, 0, glass);
                    break;
                case LiquidThemeCue.Flow:
                    Swell(mix, pitch * A4, .010);
                    for (int note = 0; note < GlassNotes.Length; note++)
                        Note(mix, pitch, TravelOffset(mix, .04 + note * .23), GlassNotes[note], 235, .045 - note * .006, -8, glass);
                    break;
                case LiquidThemeCue.Finish:
                    Settle(mix, pitch, dark, E4, .074);
                    break;
                case LiquidThemeCue.Short:
                    Note(mix, pitch, 0, E4, 145, .081, 0, glass);
                    Note(mix, pitch, 26, A4, 112, .027, 0, glass);
                    break;
            }
        }

        static byte[] LoadZipper(string name)
        {
            int rate;
            byte[] pcm = NotificationSoundPlayer.ReadPcmResource(name, out rate);
            int totalMilliseconds = 0;
            foreach (int length in ZipperMilliseconds) totalMilliseconds += length;
            if (rate != SampleRate || pcm.Length != totalMilliseconds * FramesPerMillisecond * 2)
                throw new System.IO.InvalidDataException("Theme zipper audio does not match its cue boundaries.");
            return pcm;
        }

        static byte[] RenderZipper(LiquidThemeCue cue, bool dark, int frames)
        {
            byte[] bank = dark ? ZipperDark.Value : ZipperLight.Value;
            int index = (int)cue, offset = 0;
            for (int i = 0; i < index; i++) offset += ZipperMilliseconds[i] * FramesPerMillisecond;
            int available = ZipperMilliseconds[index] * FramesPerMillisecond;
            int count = Math.Min(available, frames);
            var pcm = new byte[frames * 2];
            Buffer.BlockCopy(bank, offset * 2, pcm, 0, count * 2);
            if (count < available)
            {
                int fade = Math.Min(count, 20 * FramesPerMillisecond);
                for (int i = count - fade; i < count; i++)
                {
                    int sample = (short)(pcm[i * 2] | pcm[i * 2 + 1] << 8);
                    sample = (int)(sample * Smooth((count - 1 - i) / (double)fade));
                    pcm[i * 2] = (byte)(sample & 0xff);
                    pcm[i * 2 + 1] = (byte)((sample >> 8) & 0xff);
                }
            }
            return pcm;
        }

        static int TravelOffset(double[] mix, double fraction)
        {
            return (int)(mix.Length * fraction / FramesPerMillisecond);
        }

        static void Settle(double[] mix, double pitch, bool dark, double root, double gain)
        {
            Note(mix, pitch, 0, root, 158, gain);
            Note(mix, pitch, 39, root * (dark ? .75 : 1.25), 146, gain * .42);
        }

        // A softly struck resonator with a short pitch bend. Harmonics give the
        // timbres some body without a broadband hiss or a sharp synthesized click.
        static void Note(double[] mix, double pitch, int offsetMs, double hz, int lengthMs, double gain,
            double bendHz = 0, double harmonic = .08)
        {
            int start = offsetMs * FramesPerMillisecond;
            int length = Math.Min(mix.Length - start, lengthMs * FramesPerMillisecond);
            double phase = 0, step = Tau * pitch * hz / SampleRate, bend = Tau * pitch * bendHz / SampleRate;
            double decay = Math.Exp(-3.2 / (lengthMs * FramesPerMillisecond));
            double bendDecay = Math.Exp(-1.0 / (23 * FramesPerMillisecond));
            for (int frame = 0; frame < length; frame++)
            {
                double envelope = Smooth(frame / (12.0 * FramesPerMillisecond)) *
                    Smooth((length - 1 - frame) / (27.0 * FramesPerMillisecond));
                double tone = Math.Sin(phase) + harmonic * (Math.Sin(phase * 2) + .17 * Math.Sin(phase * 3));
                mix[start + frame] += tone * envelope * gain;
                phase += step + bend;
                bend *= bendDecay;
                gain *= decay;
            }
        }

        // Low, slow resonance links the drops or reflections during a longer reveal.
        static void Swell(double[] mix, double hz, double gain)
        {
            double phase = 0, step = Tau * hz / SampleRate;
            for (int frame = 0; frame < mix.Length; frame++)
            {
                double time = (double)frame / SampleRate;
                double envelope = Smooth(time / .13) * Smooth((mix.Length - 1 - frame) / (.16 * SampleRate));
                mix[frame] += (Math.Sin(phase) + .11 * Math.Sin(phase * 2)) * gain * envelope *
                    (.75 + .25 * Math.Sin(Tau * 2.15 * time));
                phase += step * (1 + .014 * Math.Sin(Tau * 1.26 * time));
            }
        }

        static double Smooth(double value)
        {
            double t = Math.Max(0, Math.Min(1, value));
            return t * t * (3 - 2 * t);
        }
    }
}

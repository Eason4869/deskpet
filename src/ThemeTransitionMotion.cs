using System;

namespace Dafeiyu
{
    internal static class ThemeTransitionMotion
    {
        internal const string Off = "off";
        internal const string Flow = "flow";
        internal const string Ripple = "ripple";
        internal const string Refraction = "refraction";
        internal const string Zipper = "zipper";
        internal const string RandomSelection = "random";
        internal static readonly string[] Choices = { Off, Flow, Ripple, Refraction, Zipper, RandomSelection };
        internal static readonly string[] AnimatedEffects = { Flow, Ripple, Refraction, Zipper };
        static readonly Random random = new Random();

        internal static string Normalize(string motion)
        {
            return Array.IndexOf(Choices, motion) >= 0 ? motion : RandomSelection;
        }

        internal static string Resolve(string motion)
        {
            motion = Normalize(motion);
            if (motion != RandomSelection) return motion;
            lock (random) return AnimatedEffects[random.Next(AnimatedEffects.Length)];
        }

        internal static TimeSpan Duration(string motion, TimeSpan liquidDuration)
        {
            if (motion == Refraction) return TimeSpan.FromMilliseconds(1900);
            if (motion == Zipper) return TimeSpan.FromMilliseconds(1850);
            return liquidDuration;
        }

        internal static void SoundStages(string motion, out double start, out double onset, out double travel, out double end)
        {
            start = 0;
            onset = LiquidThemeShape.ImpactProgress;
            travel = LiquidThemeShape.FlowProgress;
            end = 1;
            switch (motion)
            {
                case Ripple: onset = .18; travel = .34; break;
                case Refraction: onset = .12; travel = .28; break;
                case Zipper:
                    start = .04;
                    onset = ZipperThemeShape.OpeningProgress + start;
                    travel = .19 + start;
                    end = .85;
                    break;
            }
        }

        internal static string Caption()
        {
            return TextResources.Get("themeTransition.motion.label");
        }

        internal static string Label(string motion)
        {
            if (motion == Off) return TextResources.Get("themeTransition.motion.off");
            if (motion == Ripple) return TextResources.Get("themeTransition.motion.ripple");
            if (motion == Refraction) return TextResources.Get("themeTransition.motion.refraction");
            if (motion == Zipper) return TextResources.Get("themeTransition.motion.zipper");
            if (motion == RandomSelection) return TextResources.Get("themeTransition.motion.random");
            return TextResources.Get("themeTransition.motion.flow");
        }
    }

    // Retarget from the last painted position, even when the click arrives between frames.
    internal sealed class ThemeRevealTimeline
    {
        double from, to, slope, velocity, milliseconds;
        bool redirected;

        internal double Progress { get; private set; }
        internal TimeSpan Duration { get { return TimeSpan.FromMilliseconds(milliseconds); } }

        internal void Start(TimeSpan duration)
        {
            from = Progress = 0;
            to = 1;
            milliseconds = duration.TotalMilliseconds;
            velocity = 1 / milliseconds;
            redirected = false;
        }

        internal void Retarget(bool reverse)
        {
            from = Progress;
            to = reverse ? 0 : 1;
            milliseconds = Math.Round(180 + 120 * Math.Abs(to - from));
            // A modest continuation of the old velocity softens the direction change.
            // Keep its overshoot inside the mask's valid range.
            slope = Math.Max(-Math.Min(.16, 3 * from),
                Math.Min(Math.Min(.16, 3 * (1 - from)), velocity * milliseconds));
            redirected = true;
        }

        internal bool Advance(TimeSpan elapsed)
        {
            if (elapsed.TotalMilliseconds >= milliseconds)
            {
                Progress = to;
                velocity = 0;
                return false;
            }
            double t = elapsed.TotalMilliseconds / milliseconds;
            double distance = to - from;
            if (redirected)
            {
                Progress = from + distance * t * t * (3 - 2 * t) + slope * t * (1 - t) * (1 - t);
                velocity = (distance * (6 * t - 6 * t * t) + slope * (1 - t) * (1 - 3 * t)) / milliseconds;
            }
            else Progress = t;
            return true;
        }
    }
}

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

        internal static void SoundStages(string motion, out double onset, out double travel, out double end)
        {
            onset = LiquidThemeShape.ImpactProgress;
            travel = LiquidThemeShape.FlowProgress;
            end = 1;
            switch (motion)
            {
                case Ripple: onset = .18; travel = .34; break;
                case Refraction: onset = .12; travel = .28; break;
                case Zipper: onset = .10; travel = .19; end = .85; break;
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
}

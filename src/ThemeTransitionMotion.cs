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
        internal static readonly string[] Choices = { Off, Flow, Ripple, Refraction, Zipper };

        internal static string Normalize(string motion)
        {
            return Array.IndexOf(Choices, motion) >= 0 ? motion : Flow;
        }

        internal static TimeSpan Duration(string motion, TimeSpan liquidDuration)
        {
            if (motion == Refraction) return TimeSpan.FromMilliseconds(820);
            if (motion == Zipper) return TimeSpan.FromMilliseconds(1350);
            return liquidDuration;
        }

        internal static bool IsLiquid(string motion)
        {
            return motion == Flow || motion == Ripple;
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
            return TextResources.Get("themeTransition.motion.flow");
        }
    }
}

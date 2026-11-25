namespace Dafeiyu
{
    internal static class ThemeTransitionMotion
    {
        internal const string Off = "off";
        internal const string Flow = "flow";
        internal const string Ripple = "ripple";
        internal static readonly string[] Choices = { Off, Flow, Ripple };

        internal static string Normalize(string motion)
        {
            return motion == Off || motion == Ripple ? motion : Flow;
        }

        internal static string Caption()
        {
            return TextResources.Get("themeTransition.motion.label");
        }

        internal static string Label(string motion)
        {
            if (motion == Off) return TextResources.Get("themeTransition.motion.off");
            if (motion == Ripple) return TextResources.Get("themeTransition.motion.ripple");
            return TextResources.Get("themeTransition.motion.flow");
        }
    }
}

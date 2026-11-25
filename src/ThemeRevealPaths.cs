using System;

namespace Dafeiyu
{
    internal sealed class ThemeRevealPaths
    {
        internal const double RefractionOffsetX = 4;
        internal const double RefractionOffsetY = -3;
        internal readonly Action<double, ILiquidThemeOutline> Reveal;
        internal readonly Action<double, ILiquidThemeOutline> Refraction;

        ThemeRevealPaths(Action<double, ILiquidThemeOutline> reveal, Action<double, ILiquidThemeOutline> refraction = null)
        {
            Reveal = reveal;
            Refraction = refraction;
        }

        internal static ThemeRevealPaths Create(string effect, double width, double height, double originX, double originY)
        {
            switch (effect)
            {
                case ThemeTransitionMotion.Ripple:
                    return new ThemeRevealPaths(new RippleThemeShape(width, height, originX, originY).WriteOutline);
                case ThemeTransitionMotion.Refraction:
                    var shape = new RefractionThemeShape(width, height, originX, originY);
                    return new ThemeRevealPaths(shape.WriteOutline, shape.WriteRefraction);
                case ThemeTransitionMotion.Zipper:
                    return new ThemeRevealPaths(new ZipperThemeShape(width, height, originX, originY).WriteOutline);
                default:
                    return new ThemeRevealPaths(new LiquidThemeShape(width, height, originX, originY).WriteOutline);
            }
        }
    }
}

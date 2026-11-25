using System;

namespace Dafeiyu
{
    internal sealed class ThemeRevealPaths
    {
        internal const uint ZipperLining = 0x99303948;
        internal const uint ZipperRim = 0xbae4eff6;
        internal const uint ZipperTeeth = 0xeaaab9c8;
        internal const uint ZipperPullLight = 0xfff4f8ff;
        internal const uint ZipperPullMiddle = 0xffa0afbe;
        internal const uint ZipperPullDark = 0xff53677d;
        internal static readonly ZipperPart[] ZipperParts = { ZipperPart.Lining, ZipperPart.Rim, ZipperPart.Teeth, ZipperPart.Pull };
        internal readonly Action<double, ILiquidThemeOutline> Reveal;
        internal readonly RefractionThemeShape Refraction;
        internal readonly ZipperThemeShape Zipper;

        ThemeRevealPaths(Action<double, ILiquidThemeOutline> reveal, RefractionThemeShape refraction = null, ZipperThemeShape zipper = null)
        {
            Reveal = reveal;
            Refraction = refraction;
            Zipper = zipper;
        }

        internal static ThemeRevealPaths Create(string effect, double width, double height, double originX, double originY)
        {
            switch (effect)
            {
                case ThemeTransitionMotion.Ripple:
                    return new ThemeRevealPaths(new RippleThemeShape(width, height, originX, originY).WriteOutline);
                case ThemeTransitionMotion.Refraction:
                    var shape = new RefractionThemeShape(width, height, originX, originY);
                    return new ThemeRevealPaths(shape.WriteOutline, refraction: shape);
                case ThemeTransitionMotion.Zipper:
                    var zipper = new ZipperThemeShape(width, height, originX, originY);
                    return new ThemeRevealPaths(zipper.WriteOutline, zipper: zipper);
                default:
                    return new ThemeRevealPaths(new LiquidThemeShape(width, height, originX, originY).WriteOutline);
            }
        }
    }
}

using System;
using System.Globalization;
using System.Text;

namespace Dafeiyu
{
    internal interface ILiquidThemeOutline
    {
        void MoveTo(double x, double y);
        void LineTo(double x, double y);
        void CurveTo(double x1, double y1, double x2, double y2, double x, double y);
        void Close();
    }

    internal sealed class LiquidThemeShape
    {
        internal static readonly TimeSpan DefaultDuration = TimeSpan.FromSeconds(2);
        internal const double ImpactProgress = .28;
        internal const double FlowProgress = .37;
        readonly double width, height, originX, originY, scale;

        internal LiquidThemeShape(double width, double height, double originX, double originY)
        {
            this.width = width;
            this.height = height;
            this.originX = Math.Max(0, Math.Min(width, originX));
            this.originY = Math.Max(0, Math.Min(height, originY));
            scale = Math.Max(.65, Math.Min(1.4, height / 700));
        }

        internal string PathAt(double progress)
        {
            var outline = new PathOutline();
            WriteOutline(progress, outline);
            return outline.ToString();
        }

        internal void WriteOutline(double progress, ILiquidThemeOutline outline)
        {
            double time = Clamp(progress);
            double fall = Clamp(time / ImpactProgress);
            double reach = fall * (.24 + .76 * fall);
            double tip = originY + (height + 2 - originY) * reach;
            if (time > ImpactProgress)
            {
                double splash = Clamp((time - ImpactProgress) / .20);
                double surface = Pool(outline, time, splash);
                tip = Math.Min(height + 2, surface + 3 * scale);
                if (splash < 1) Splash(outline, splash);
            }

            if (tip > originY)
                Stream(outline, time, tip - originY);

            for (int i = 0; i < 4; i++)
            {
                double drop = (time - .037 - i * .041) / (.23 - i * .014);
                if (drop <= 0 || drop >= 1) continue;
                double y = originY + (height - originY) * drop * (.24 + .76 * drop);
                double direction = i % 2 == 0 ? 1 : -1;
                double x = originX + direction * scale * (7 + (24 + i * 4) * drop + 4 * Math.Sin(drop * Math.PI));
                double radius = scale * (2.5 - i * .23) * Smooth(drop / .14) * (1 - Smooth((drop - .86) / .14));
                Oval(outline, x, y, radius, radius * (1.25 + .7 * drop));
            }
        }

        void Stream(ILiquidThemeOutline outline, double time, double length)
        {
            double thickness = scale * Smooth(time / .038) * Smooth(length / (30 * scale));
            double swayScale = scale * Smooth(length / (96 * scale));
            double right = StreamEdge(time, 0, thickness, swayScale, 1);
            outline.MoveTo(right, originY);
            right = StreamSide(outline, time, length, thickness, swayScale, 1, right);
            double left = StreamEdge(time, 1, thickness, swayScale, -1);
            double tip = originY + length;
            outline.CurveTo(right, tip + thickness * 1.6, left, tip + thickness * 1.6, left, tip);
            StreamSide(outline, time, length, thickness, swayScale, -1, left);
            outline.Close();
        }

        double StreamSide(ILiquidThemeOutline outline, double time, double length, double thickness,
            double swayScale, int direction, double x)
        {
            const int segments = 8;
            double step = (double)direction / segments;
            double start = direction > 0 ? 0 : 1;
            double y = originY + length * start;
            for (int i = 1; i <= segments; i++)
            {
                double section = start + step * i;
                double next = StreamEdge(time, section, thickness, swayScale, direction);
                double middle = StreamEdge(time, section - step * .5, thickness, swayScale, direction);
                double control = 2 * middle - (x + next) * .5;
                double nextY = originY + length * section;
                outline.CurveTo((x + 2 * control) / 3, y + length * step / 3,
                    (next + 2 * control) / 3, nextY - length * step / 3, next, nextY);
                x = next;
                y = nextY;
            }
            return x;
        }

        double StreamEdge(double time, double section, double thickness, double swayScale, int direction)
        {
            double sway = 8.8 * Math.Sin(section * 7 - time * 19) + 2.7 * Math.Sin(section * 13 + time * 22);
            double anchored = 1 - Smooth((section - .72) / .28) * Smooth((time - .18) / (ImpactProgress - .18));
            double center = originX + sway * swayScale * Smooth(section / .22) * anchored;
            double halfWidth = 2.65 + .75 * Math.Sin(section * 5) + 1.1 * Math.Sin(section * 12 - time * 36);
            return center + direction * halfWidth * thickness;
        }

        void Splash(ILiquidThemeOutline outline, double t)
        {
            SplashJet(outline, t, -1, 36, 53);
            SplashJet(outline, t, 1, 27, 45);
            for (int i = 0; i < 8; i++)
            {
                double delay = (i % 3) * .08;
                double flight = (t - delay) / (1 - delay);
                if (flight <= 0) continue;
                double direction = i % 2 == 0 ? -1 : 1;
                double travel = 25 + i / 2 * 18 + (i % 2) * 8;
                double lift = 25 + (i * 19 % 37);
                double x = originX + direction * scale * (5 + travel * flight);
                double y = height + 2 - scale * 4 * lift * flight * (1 - flight);
                double radius = scale * (1.4 + i % 3 * .65) * Smooth(flight / .14) * (1 - Smooth((flight - .55) / .45));
                Oval(outline, x, y, radius, radius * (1.6 - .65 * flight));
            }
        }

        void SplashJet(ILiquidThemeOutline outline, double t, int direction, double peakLift, double travel)
        {
            double arc = Math.Sin(Math.PI * t);
            double lift = peakLift * scale * arc * (1 - .4 * t);
            double baseX = originX + direction * 4 * scale;
            double drift = direction * scale * (8 + travel * t);
            double tipX = baseX + drift;
            double bottom = height + 2;
            double tipY = bottom - lift;
            double rootWidth = 6.5 * scale * arc;
            double tipWidth = 1.6 * scale * arc;
            double lowerX = baseX + drift * .13;
            double upperX = tipX - drift * .28;
            outline.MoveTo(baseX - rootWidth, bottom);
            outline.CurveTo(lowerX - rootWidth, bottom - lift * .53, upperX - rootWidth * .5, tipY + lift * .13,
                tipX - tipWidth, tipY);
            outline.CurveTo(tipX - tipWidth * .5, tipY - tipWidth, tipX + tipWidth * .65, tipY - tipWidth,
                tipX + tipWidth, tipY);
            outline.CurveTo(upperX + rootWidth * .5, tipY + lift * .28, lowerX + rootWidth, bottom - lift * .26,
                baseX + rootWidth, bottom);
            outline.Close();
        }

        double Pool(ILiquidThemeOutline outline, double time, double splash)
        {
            double bottom = height + 2;
            double deposit = Smooth((time - ImpactProgress) / (FlowProgress - ImpactProgress));
            double spread = Smooth((time - ImpactProgress) / (.55 - ImpactProgress));
            double rise = Clamp((time - FlowProgress) / (1 - FlowProgress));
            double fill = rise * rise * (2 - rise);
            double depth = 12 * scale * deposit + (height + 38 * scale) * fill;
            double amplitude = scale * deposit * (2 + 10 * Math.Sin(Math.PI * fill) + 4 * fill);
            double wavelength = Math.Max(155, width / 3.6);
            double rim = scale * (35 + 50 * spread);
            double radius = 46 * scale + (Math.Max(originX, width - originX) + rim) * spread;
            double rippleStrength = 6 * scale * Math.Sin(Math.PI * splash);
            double rippleDistance = 155 * splash;
            const double left = -1;
            double right = width + 1;
            int segments = Math.Max(22, Math.Min(80, (int)Math.Ceiling(width / 15)));
            double step = (right - left) / segments;
            double originPosition = (originX - left) / step;
            int originSegment = (int)originPosition;
            double fraction = originPosition - originSegment;
            double surface = bottom;
            double y = Wave(left, time, bottom, depth, amplitude, wavelength, radius, rim, rippleStrength, rippleDistance);
            outline.MoveTo(left, y);
            for (int i = 1; i <= segments; i++)
            {
                double x = left + step * i;
                double next = Wave(x, time, bottom, depth, amplitude, wavelength, radius, rim, rippleStrength, rippleDistance);
                double middle = Wave(x - step * .5, time, bottom, depth, amplitude, wavelength, radius, rim, rippleStrength, rippleDistance);
                double control = Math.Min(bottom, 2 * middle - (y + next) * .5);
                if (i - 1 == originSegment)
                {
                    // Use the rendered curve to join the stream to the surface.
                    double complement = 1 - fraction;
                    surface = complement * complement * y + 2 * complement * fraction * control + fraction * fraction * next;
                }
                outline.CurveTo(x - step * 2 / 3, (y + 2 * control) / 3,
                    x - step / 3, (next + 2 * control) / 3, x, next);
                y = next;
            }
            outline.LineTo(right, bottom);
            outline.LineTo(left, bottom);
            outline.Close();
            SurfaceSpray(outline, time, bottom, depth, amplitude, wavelength, radius, rim, rippleStrength, rippleDistance);
            return surface;
        }

        void SurfaceSpray(ILiquidThemeOutline outline, double time, double bottom, double depth, double amplitude,
            double wavelength, double radius, double rim, double rippleStrength, double rippleDistance)
        {
            double activity = Smooth((time - .33) / .14) * (1 - Smooth((time - .88) / .12));
            if (activity <= 0) return;
            for (int i = 0; i < 10; i++)
            {
                double cycle = (time - .34) / (.116 + (i % 4) * .014) + i * .171;
                double flight = cycle - Math.Floor(cycle);
                double direction = i % 2 == 0 ? -1 : 1;
                double reach = 18 + (i * 17 % 71);
                double lift = (15 + (i * 13 % 29)) * activity;
                double x = originX + direction * scale * (5 + reach * flight);
                if (i >= 6)
                {
                    x = width * ((i - 6 + .5) / 4) + direction * scale * (8 + 23 * flight);
                    lift *= .60;
                }
                double surface = Wave(x, time, bottom, depth, amplitude, wavelength, radius, rim, rippleStrength, rippleDistance);
                double y = surface - scale * 4 * lift * flight * (1 - flight);
                double drop = (1.25 + (i * 7 % 13) * .115) * scale * activity *
                    Smooth(flight / .15) * (1 - Smooth((flight - .66) / .34));
                if (drop > .15) Oval(outline, x, y, drop, drop * (1.42 - .43 * flight));
            }
        }

        double Wave(double x, double time, double bottom, double depth, double amplitude, double wavelength,
            double radius, double rim, double rippleStrength, double rippleDistance)
        {
            double distance = Math.Abs(x - originX);
            double wetness = Smooth((radius - distance) / rim);
            if (wetness <= 0) return bottom;
            double phase = x / wavelength * Math.PI * 2;
            double wave = amplitude * (Math.Sin(phase - time * 12) + .28 * Math.Sin(phase * 2 + time * 16));
            double offset = (distance / scale - rippleDistance) / 26;
            double ripple = rippleStrength * Math.Sin(offset * 4) * Math.Exp(-offset * offset);
            double jet = 0;
            if (distance < 50 * scale)
            {
                double contact = distance / (16 * scale);
                jet = scale * (21 + 6 * Math.Sin(time * 46)) * Smooth((time - ImpactProgress) / .12) *
                    (1 - Smooth((time - .9) / .10)) * Math.Exp(-contact * contact);
            }
            return bottom - wetness * Math.Max(0, depth - wave - ripple + jet);
        }

        static double Clamp(double value) { return Math.Max(0, Math.Min(1, value)); }
        static double Smooth(double value)
        {
            double t = Clamp(value);
            return t * t * (3 - 2 * t);
        }

        static void Oval(ILiquidThemeOutline outline, double x, double y, double rx, double ry)
        {
            double kx = rx * .5523, ky = ry * .5523;
            outline.MoveTo(x - rx, y);
            outline.CurveTo(x - rx, y - ky, x - kx, y - ry, x, y - ry);
            outline.CurveTo(x + kx, y - ry, x + rx, y - ky, x + rx, y);
            outline.CurveTo(x + rx, y + ky, x + kx, y + ry, x, y + ry);
            outline.CurveTo(x - kx, y + ry, x - rx, y + ky, x - rx, y);
            outline.Close();
        }

        sealed class PathOutline : ILiquidThemeOutline
        {
            readonly StringBuilder path = new StringBuilder("F1 ", 2048);

            public void MoveTo(double x, double y) { path.Append('M'); Point(x, y); }
            public void LineTo(double x, double y) { path.Append('L'); Point(x, y); }
            public void CurveTo(double x1, double y1, double x2, double y2, double x, double y)
            {
                path.Append('C');
                Point(x1, y1);
                Point(x2, y2);
                Point(x, y);
            }
            public void Close() { path.Append("Z "); }
            public override string ToString() { return path.Length == 3 ? "M0,0" : path.ToString(); }

            void Point(double x, double y)
            {
                path.Append(x.ToString("0.##", CultureInfo.InvariantCulture)).Append(',')
                    .Append(y.ToString("0.##", CultureInfo.InvariantCulture)).Append(' ');
            }
        }
    }

    internal static class ThemeSwitch
    {
        internal static string Label(string mode)
        {
            if (mode == "light") return TextResources.Get("themeModePicker.themeModePicker.titleModeMode.alternativeA");
            if (mode == "dark") return TextResources.Get("themeModePicker.themeModePicker.titleModeMode.alternativeB");
            return TextResources.Get("themeModePicker.themeModePicker.titleModeMode");
        }

        internal static string Icon(string mode)
        {
            if (mode == "light")
                return "M16.5 12A4.5 4.5 0 1 1 7.5 12A4.5 4.5 0 1 1 16.5 12 M12 2.5V5 M12 19v2.5 M2.5 12H5 M19 12h2.5 M5.3 5.3 7 7 M17 17l1.7 1.7 M5.3 18.7 7 17 M17 7l1.7-1.7";
            if (mode == "dark")
                return "M20.7 14.2A8.9 8.9 0 0 1 9.8 3.3A9 9 0 1 0 20.7 14.2Z";
            return "M4 5h16a1 1 0 0 1 1 1v10a1 1 0 0 1-1 1H4a1 1 0 0 1-1-1V6a1 1 0 0 1 1-1Z M12 17v3 M8 20h8";
        }
    }
}

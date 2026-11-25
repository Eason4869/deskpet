using System;
using System.Globalization;
using System.Text;

namespace Dafeiyu
{
    internal sealed class LiquidThemeShape
    {
        internal static readonly TimeSpan DefaultDuration = TimeSpan.FromMilliseconds(1050);
        const double Impact = .25;
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
            var path = new StringBuilder(750);
            double time = Clamp(progress);
            double fall = Clamp(time / Impact);
            double reach = fall * (.35 + .65 * fall);
            double tail = 1 - Smooth((time - .31) / .25);
            double tip = originY + (height - originY + 4) * reach;
            if (reach > 0 && tail > 0)
                Stream(path, time, tip, scale * tail * (4.5 + Math.Sin(time * 29) * .8));

            for (int i = 0; i < 3; i++)
            {
                double drop = (time - .055 - i * .047) / .19;
                if (drop <= 0 || drop >= 1) continue;
                double y = originY + (height - originY - 4) * drop * (.32 + .68 * drop);
                double x = originX + (i % 2 == 0 ? 1 : -1) * scale * (7 + 18 * drop);
                double radius = scale * (2.8 - i * .36) * (1 - .35 * drop);
                Oval(path, x, y, radius, radius * 1.45);
            }

            double splash = (time - Impact) / .26;
            if (splash > 0 && splash < 1) Splash(path, splash);

            double rise = Smooth((time - Impact) / (1 - Impact));
            if (rise > 0) Pool(path, time, splash, rise);
            return path.Length == 0 ? "M0,0" : "F1 " + path;
        }

        void Stream(StringBuilder path, double time, double tip, double half)
        {
            double length = tip - originY;
            double bend = Math.Sin(time * 24) * 16 * scale;
            double curl = Math.Sin(time * 20 + 1) * 20 * scale;
            double endX = originX + curl * .7 * (1 - Smooth((time - .12) / (Impact - .12)));
            Move(path, originX + half, originY);
            Curve(path, originX + half - bend * .4, originY + length * .28,
                originX + half + bend, originY + length * .72, endX + half * .7, tip);
            Curve(path, endX + half * .45, tip + half, endX - half * .45, tip + half, endX - half * .7, tip);
            Curve(path, originX - half + bend, originY + length * .72,
                originX - half - bend * .4, originY + length * .28, originX - half, originY);
            path.Append("Z ");
        }

        void Splash(StringBuilder path, double t)
        {
            for (int i = 0; i < 7; i++)
            {
                double speed = (i - 3) * (17 + (i % 2) * 7);
                double lift = 16 + (i * 23 % 41);
                double x = originX + speed * (.12 + t) * scale;
                double y = height - scale * (5 + 4 * lift * t * (1 - t));
                double radius = (2.1 + i % 3 * .7) * scale * (1 - t * .7);
                Oval(path, x, y, radius, radius * 1.3);
            }
        }

        void Pool(StringBuilder path, double time, double splash, double rise)
        {
            double spread = Smooth(splash);
            double travel = Math.Max(originX, width - originX) * spread + 22 * scale;
            double left = Math.Max(-1, originX - travel);
            double right = Math.Min(width + 1, originX + travel);
            double waterline = height + 4 * scale - (height + 48 * scale) * rise;
            double amplitude = scale * (6 + 4 * Math.Sin(Math.PI * rise)) * (.4 + .6 * spread);
            double wavelength = Math.Max(140, width / 3.4);
            double step = (right - left) / 12;
            double y = Wave(left, time, splash, waterline, amplitude, wavelength);
            Move(path, left, y);
            for (int i = 1; i <= 12; i++)
            {
                double x = left + step * i;
                double next = Wave(x, time, splash, waterline, amplitude, wavelength);
                double middle = Wave(x - step * .5, time, splash, waterline, amplitude, wavelength);
                double smooth = (4 * middle - y - next) / 2;
                Curve(path, x - step * 2 / 3, (y + 2 * smooth) / 3,
                    x - step / 3, (next + 2 * smooth) / 3, x, next);
                y = next;
            }
            Line(path, right, height + 2);
            Line(path, left, height + 2);
            path.Append("Z ");
        }

        double Wave(double x, double time, double splash, double level, double amplitude, double wavelength)
        {
            double distance = Math.Abs(x - originX);
            double ripple = splash > 0 && splash < 1
                ? 9 * scale * Math.Sin(distance / 13 - splash * 15) * Math.Exp(-distance / (105 * scale)) * Math.Sin(Math.PI * splash)
                : 0;
            return level + amplitude * Math.Sin(x / wavelength * Math.PI * 2 - time * 12)
                + amplitude * .27 * Math.Sin(x / wavelength * Math.PI * 4 + time * 16) + ripple;
        }

        static double Clamp(double value) { return Math.Max(0, Math.Min(1, value)); }
        static double Smooth(double value)
        {
            double t = Clamp(value);
            return t * t * (3 - 2 * t);
        }

        static void Oval(StringBuilder path, double x, double y, double rx, double ry)
        {
            double kx = rx * .5523, ky = ry * .5523;
            Move(path, x - rx, y);
            Curve(path, x - rx, y - ky, x - kx, y - ry, x, y - ry);
            Curve(path, x + kx, y - ry, x + rx, y - ky, x + rx, y);
            Curve(path, x + rx, y + ky, x + kx, y + ry, x, y + ry);
            Curve(path, x - kx, y + ry, x - rx, y + ky, x - rx, y);
            path.Append("Z ");
        }

        static void Move(StringBuilder path, double x, double y) { path.Append('M'); Point(path, x, y); }
        static void Line(StringBuilder path, double x, double y) { path.Append('L'); Point(path, x, y); }
        static void Curve(StringBuilder path, double x1, double y1, double x2, double y2, double x, double y)
        {
            path.Append('C');
            Point(path, x1, y1);
            Point(path, x2, y2);
            Point(path, x, y);
        }
        static void Point(StringBuilder path, double x, double y)
        {
            path.Append(x.ToString("0.##", CultureInfo.InvariantCulture)).Append(',')
                .Append(y.ToString("0.##", CultureInfo.InvariantCulture)).Append(' ');
        }
    }

    internal static class ThemeSwitch
    {
        internal static string Next(string mode)
        {
            if (mode == "light") return "dark";
            return mode == "dark" ? "system" : "light";
        }

        internal static string Label(string mode)
        {
            const string key = "themeModePicker.themeModePicker.titleModeMode";
            return TextResources.Get(mode == "light" ? key + ".alternativeA" : mode == "dark" ? key + ".alternativeB" : key);
        }

        internal static string Icon(string mode)
        {
            if (mode == "light")
                return "M16.5 12A4.5 4.5 0 1 1 7.5 12A4.5 4.5 0 1 1 16.5 12 M12 2.5V5 M12 19v2.5 M2.5 12H5 M19 12h2.5 M5.3 5.3 7 7 M17 17l1.7 1.7 M5.3 18.7 7 17 M17 7l1.7-1.7";
            if (mode == "dark")
                return "M20.7 14.2A8.9 8.9 0 0 1 9.8 3.3A9 9 0 1 0 20.7 14.2Z";
            return "M12 2.5v19 M12 7a5 5 0 0 0 0 10 M3 12h2 M5.6 5.6 7 7 M5.6 18.4 7 17 M14.5 5.5A7 7 0 0 1 15.1 18.5Q18.6 16.1 18.6 12Q18.6 8.2 14.5 5.5Z";
        }
    }
}

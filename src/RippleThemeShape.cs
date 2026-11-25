using System;

namespace Dafeiyu
{
    internal sealed class RippleThemeShape
    {
        const int Segments = 32;
        const double AngleStep = 2 * Math.PI / Segments;
        readonly double originX, originY, reach, scale, amplitude;

        internal RippleThemeShape(double width, double height, double originX, double originY)
        {
            this.originX = Math.Max(0, Math.Min(width, originX));
            this.originY = Math.Max(0, Math.Min(height, originY));
            double farX = Math.Max(this.originX, width - this.originX);
            double farY = Math.Max(this.originY, height - this.originY);
            double farCorner = Math.Sqrt(farX * farX + farY * farY);
            scale = Math.Max(.65, Math.Min(1.4, height / 700));
            amplitude = Math.Min(12 * scale, farCorner * .023);
            reach = farCorner + amplitude + 2 * scale;
        }

        internal void WriteOutline(double progress, ILiquidThemeOutline outline)
        {
            double time = Clamp(progress);
            if (time <= 0) return;

            double front = reach * time * (1.24 - .24 * time);
            double growth = Smooth(time / .12);
            double spacing = Math.Min(26 * scale, reach * .042) * growth * (1 - Smooth((time - .64) / .31));
            double mainHalf = Math.Min(4.3 * scale, reach * .009) * growth;
            double echoHalf = Math.Min(2.4 * scale, reach * .005) * growth;
            double mainInner = front + spacing - mainHalf;
            double mainOuter = front + spacing + mainHalf;
            double echoInner = front + spacing * 2.05 - echoHalf;
            double echoOuter = front + spacing * 2.05 + echoHalf;
            double height = amplitude * growth;
            double phase = time * 5.6;

            bool separateEcho = echoInner > mainOuter;
            if (!separateEcho) mainOuter = Math.Max(mainOuter, echoOuter);
            bool separateMain = mainInner > front;
            if (!separateMain) front = mainOuter;

            Contour(outline, front, height, phase, AngleStep);
            if (separateMain) Crest(outline, mainInner, mainOuter, height, phase);
            if (separateEcho) Crest(outline, echoInner, echoOuter, height, phase);
        }

        void Crest(ILiquidThemeOutline outline, double inner, double outer, double height, double phase)
        {
            // The reversed inner contour leaves a gap under nonzero fill.
            Contour(outline, outer, height, phase, AngleStep);
            Contour(outline, inner, height, phase, -AngleStep);
        }

        void Contour(ILiquidThemeOutline outline, double radius, double height, double phase, double step)
        {
            double tangent = step / 3;
            var first = Sample(radius, height, phase, 0);
            outline.MoveTo(first.X, first.Y);
            var previous = first;
            for (int i = 1; i <= Segments; i++)
            {
                var next = i == Segments ? first : Sample(radius, height, phase, i * step);
                outline.CurveTo(previous.X + tangent * previous.Dx, previous.Y + tangent * previous.Dy,
                    next.X - tangent * next.Dx, next.Y - tangent * next.Dy, next.X, next.Y);
                previous = next;
            }
            outline.Close();
        }

        WavePoint Sample(double radius, double height, double phase, double angle)
        {
            double broad = angle * 3 - phase;
            double narrow = angle * 5 + phase * .85;
            double r = radius + height * (.68 * Math.Sin(broad) + .32 * Math.Sin(narrow));
            double dr = height * (2.04 * Math.Cos(broad) + 1.6 * Math.Cos(narrow));
            double cos = Math.Cos(angle);
            double sin = Math.Sin(angle);
            return new WavePoint(originX + r * cos, originY + r * sin, dr * cos - r * sin, dr * sin + r * cos);
        }

        static double Clamp(double value) { return Math.Max(0, Math.Min(1, value)); }

        static double Smooth(double value)
        {
            double t = Clamp(value);
            return t * t * (3 - 2 * t);
        }

        struct WavePoint
        {
            internal readonly double X, Y, Dx, Dy;

            internal WavePoint(double x, double y, double dx, double dy)
            {
                X = x;
                Y = y;
                Dx = dx;
                Dy = dy;
            }
        }
    }
}

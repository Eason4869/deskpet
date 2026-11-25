using System;

namespace Dafeiyu
{
    internal sealed class RefractionThemeShape
    {
        const double SweepX = -.8;
        const double SweepY = .6;
        const double BandWidth = 16;
        const double Overscan = 3;
        const double StartFront = 12;
        const double StartBack = -9;
        const double StartSide = 10;
        readonly double originX, originY, near, far, minSide, maxSide;

        internal RefractionThemeShape(double width, double height, double originX, double originY)
        {
            this.originX = Math.Max(0, Math.Min(width, originX));
            this.originY = Math.Max(0, Math.Min(height, originY));
            double right = width - this.originX;
            double bottom = height - this.originY;
            near = Math.Min(StartBack, SweepX * right - SweepY * this.originY - Overscan);
            far = -SweepX * this.originX + SweepY * bottom + BandWidth + Overscan;
            minSide = Math.Min(-StartSide, -SweepY * this.originX + SweepX * this.originY - Overscan);
            maxSide = Math.Max(StartSide, SweepY * right - SweepX * bottom + Overscan);
        }

        internal void WriteOutline(double progress, ILiquidThemeOutline outline)
        {
            WriteSweep(progress, outline, false);
        }

        internal void WriteRefraction(double progress, ILiquidThemeOutline outline)
        {
            WriteSweep(progress, outline, true);
        }

        void WriteSweep(double progress, ILiquidThemeOutline outline, bool band)
        {
            if (!(progress > 0) || (band && progress >= 1)) return;
            double open = Smooth(progress / .095);
            double spread = Smooth((progress - .025) / .39);
            double start = -StartSide * open + (minSide + StartSide) * spread;
            double end = StartSide * open + (maxSide - StartSide) * spread;
            double front = StartFront * open + (far - StartFront) * Smooth((progress - .04) / .96);
            double bow = 13 * Smooth(progress / .24);

            double leadingStart = front + Depth(start, bow);
            outline.MoveTo(X(leadingStart, start), Y(leadingStart, start));
            WriteEdge(outline, front, start, end, bow);
            if (band)
            {
                // The curved trailing edge has the same profile, offset into the revealed theme.
                double trailing = front - BandWidth * open;
                double trailingEnd = trailing + Depth(end, bow);
                outline.LineTo(X(trailingEnd, end), Y(trailingEnd, end));
                WriteEdge(outline, trailing, end, start, bow);
            }
            else
            {
                double back = StartBack * open + (near - StartBack) * Smooth(progress / .38);
                outline.LineTo(X(back, end), Y(back, end));
                outline.LineTo(X(back, start), Y(back, start));
            }
            outline.Close();
        }

        void WriteEdge(ILiquidThemeOutline outline, double front, double start, double end, double bow)
        {
            double first = Depth(start, bow), last = Depth(end, bow);
            double control = 2 * Depth((start + end) * .5, bow) - (first + last) * .5;
            double controlU1 = front + (first + 2 * control) / 3;
            double controlU2 = front + (last + 2 * control) / 3;
            double step = (end - start) / 3;
            outline.CurveTo(X(controlU1, start + step), Y(controlU1, start + step),
                X(controlU2, end - step), Y(controlU2, end - step), X(front + last, end), Y(front + last, end));
        }

        double Depth(double side, double bow)
        {
            double position = (2 * side - minSide - maxSide) / (maxSide - minSide);
            return bow * (1 - position * position);
        }

        double X(double sweep, double side) { return originX + SweepX * sweep + SweepY * side; }
        double Y(double sweep, double side) { return originY + SweepY * sweep - SweepX * side; }

        static double Smooth(double value)
        {
            double t = Math.Max(0, Math.Min(1, value));
            return t * t * (3 - 2 * t);
        }
    }
}

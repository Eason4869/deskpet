using System;

namespace Dafeiyu
{
    internal sealed class RefractionThemeShape
    {
        const double SweepX = -.8;
        const double SweepY = .6;
        const double BandWidth = 18;
        const double CentralWidening = .18;
        const double Overscan = 3;
        const double StartFront = 12;
        const double StartBack = -9;
        const double StartSide = 10;
        readonly double originX, originY, near, far, minSide, maxSide;

        internal static readonly OpticalLayer[] Layers =
        {
            new OpticalLayer(0, 1, .6, .13),
            new OpticalLayer(.08, .92, 2, .20),
            new OpticalLayer(.18, .83, 4.1, .29),
            new OpticalLayer(.28, .74, 6.3, .39),
            new OpticalLayer(.38, .64, 8.2, .50),
            new OpticalLayer(.72, .96, 0, .22, 0x00111b2c),
            new OpticalLayer(.13, .22, 0, .37, 0x00e8f6fc)
        };
        internal static readonly double[] TintStops = { 0, .24, .46, .58, .77, 1 };
        static readonly byte[] TintAlpha = { 18, 85, 221, 255, 112, 22 };

        internal struct OpticalLayer
        {
            internal readonly double LeadingDepth, TrailingDepth, Opacity;
            readonly double shift;
            readonly uint tint;

            internal OpticalLayer(double leadingDepth, double trailingDepth, double shift, double opacity, uint tint = 0)
            {
                LeadingDepth = leadingDepth;
                TrailingDepth = trailingDepth;
                this.shift = shift;
                Opacity = opacity;
                this.tint = tint;
            }

            internal double OffsetX { get { return -SweepX * shift; } }
            internal double OffsetY { get { return -SweepY * shift; } }
            internal bool IsTint { get { return tint != 0; } }
            internal uint TintAt(int stop) { return tint | ((uint)TintAlpha[stop] << 24); }
        }

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
            WriteSweep(progress, outline, -1);
        }

        internal void WriteRefraction(double progress, ILiquidThemeOutline outline)
        {
            WriteRefraction(progress, 0, outline);
        }

        internal void WriteRefraction(double progress, int bandIndex, ILiquidThemeOutline outline)
        {
            WriteSweep(progress, outline, bandIndex);
        }

        void WriteSweep(double progress, ILiquidThemeOutline outline, int bandIndex)
        {
            bool band = bandIndex >= 0;
            if (!(progress > 0) || (band && progress >= 1)) return;
            double open = Smooth(progress / .095);
            double spread = Smooth((progress - .025) / .39);
            double start = -StartSide * open + (minSide + StartSide) * spread;
            double end = StartSide * open + (maxSide - StartSide) * spread;
            double front = StartFront * open + (far - StartFront) * Smooth((progress - .04) / .96);
            double bow = 27 * Smooth(progress / .24);
            double width = BandWidth * open;
            double leadingDepth = band ? Layers[bandIndex].LeadingDepth : 0;
            double leading = front - width * (1 - CentralWidening) * leadingDepth;
            double leadingBow = bow - width * CentralWidening * leadingDepth;

            double leadingStart = leading + Depth(start, leadingBow);
            outline.MoveTo(X(leadingStart, start), Y(leadingStart, start));
            WriteEdge(outline, leading, start, end, leadingBow);
            if (band)
            {
                double trailingDepth = Layers[bandIndex].TrailingDepth;
                double trailing = front - width * (1 - CentralWidening) * trailingDepth;
                double trailingBow = bow - width * CentralWidening * trailingDepth;
                double trailingEnd = trailing + Depth(end, trailingBow);
                outline.LineTo(X(trailingEnd, end), Y(trailingEnd, end));
                WriteEdge(outline, trailing, end, start, trailingBow);
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

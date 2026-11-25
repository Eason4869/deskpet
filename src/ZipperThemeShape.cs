using System;

namespace Dafeiyu
{
    internal sealed class ZipperThemeShape
    {
        internal const double OpeningProgress = .42;
        readonly double width, height, originX, originY, scale;

        internal ZipperThemeShape(double width, double height, double originX, double originY)
        {
            this.width = width;
            this.height = height;
            this.originX = Math.Max(0, Math.Min(width, originX));
            this.originY = Math.Max(0, Math.Min(height, originY));
            scale = Math.Max(.65, Math.Min(1.4, height / 700));
        }

        internal void WriteOutline(double progress, ILiquidThemeOutline outline)
        {
            double time = Math.Max(0, Math.Min(1, progress));
            if (time == 0) return;

            double bleed = 2 * scale;
            double floor = height + bleed;
            double seamHalf = 1.4 * scale;
            if (time <= OpeningProgress)
            {
                double front = originY + (floor + 8 * scale - originY) * Smooth(time / OpeningProgress);
                double growth = Smooth((front - originY) / (12 * scale));
                double half = seamHalf * growth;
                double end = Math.Min(front, floor);
                outline.MoveTo(originX - half, originY);
                outline.LineTo(originX + half, originY);
                outline.LineTo(originX + half, end);
                outline.LineTo(originX - half, end);
                outline.Close();
                if (front - 7 * scale * growth < height)
                    Pull(outline, front, scale * growth);
                return;
            }

            double spread = (time - OpeningProgress) / (1 - OpeningProgress);
            double opening = Smooth(spread);
            double peakBend = 4 * opening * (1 - opening);
            double leftTravel = originX - seamHalf + bleed;
            double rightTravel = width - originX - seamHalf + bleed;
            double left = originX - seamHalf - leftTravel * opening;
            double right = originX + seamHalf + rightTravel * opening;
            double leftBend = Math.Min(.29 * leftTravel, 63 * scale) * peakBend;
            double rightBend = Math.Min(.29 * rightTravel, 54 * scale) * peakBend;
            double top = originY - (originY + bleed) * Smooth(spread / .46);
            double span = floor - top;

            outline.MoveTo(left + leftBend * .31, top);
            outline.LineTo(right - rightBend * .26, top);
            outline.CurveTo(right - rightBend * .08, top + span * .18,
                right + rightBend * .56, top + span * .33, right + rightBend * .24, top + span * .54);
            outline.CurveTo(right - rightBend * .12, top + span * .77,
                right - rightBend * .46, top + span * .9, right - rightBend * .16, floor);
            outline.LineTo(left + leftBend * .24, floor);
            outline.CurveTo(left + leftBend * .5, top + span * .86,
                left - leftBend * .07, top + span * .7, left - leftBend * .27, top + span * .5);
            outline.CurveTo(left - leftBend * .55, top + span * .27,
                left + leftBend * .18, top + span * .13, left + leftBend * .31, top);
            outline.Close();
        }

        void Pull(ILiquidThemeOutline outline, double y, double unit)
        {
            double x = originX;
            outline.MoveTo(x - 1.65 * unit, y - 7 * unit);
            outline.LineTo(x + 1.65 * unit, y - 7 * unit);
            outline.CurveTo(x + 2.05 * unit, y - 4.5 * unit, x + 5 * unit, y - 4.6 * unit,
                x + 5 * unit, y - 1.3 * unit);
            outline.LineTo(x + 5 * unit, y + 2.8 * unit);
            outline.CurveTo(x + 5 * unit, y + 4.85 * unit, x + 3.8 * unit, y + 5.9 * unit,
                x + 2.25 * unit, y + 5.9 * unit);
            outline.LineTo(x - 2.25 * unit, y + 5.9 * unit);
            outline.CurveTo(x - 3.8 * unit, y + 5.9 * unit, x - 5 * unit, y + 4.85 * unit,
                x - 5 * unit, y + 2.8 * unit);
            outline.LineTo(x - 5 * unit, y - 1.3 * unit);
            outline.CurveTo(x - 5 * unit, y - 4.6 * unit, x - 2.05 * unit, y - 4.5 * unit,
                x - 1.65 * unit, y - 7 * unit);
            outline.Close();

            // Reverse the hole contour so it stays open in the native nonzero clip.
            double hole = y + 2.45 * unit;
            outline.MoveTo(x - 2 * unit, hole);
            outline.CurveTo(x - 2 * unit, hole + .85 * unit, x - .95 * unit, hole + 1.4 * unit,
                x, hole + 1.4 * unit);
            outline.CurveTo(x + .95 * unit, hole + 1.4 * unit, x + 2 * unit, hole + .85 * unit,
                x + 2 * unit, hole);
            outline.CurveTo(x + 2 * unit, hole - .85 * unit, x + .95 * unit, hole - 1.4 * unit,
                x, hole - 1.4 * unit);
            outline.CurveTo(x - .95 * unit, hole - 1.4 * unit, x - 2 * unit, hole - .85 * unit,
                x - 2 * unit, hole);
            outline.Close();
        }

        static double Smooth(double value)
        {
            double t = Math.Max(0, Math.Min(1, value));
            return t * t * (3 - 2 * t);
        }
    }
}

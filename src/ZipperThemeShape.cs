using System;

namespace Dafeiyu
{
    internal enum ZipperPart { Lining, Rim, Teeth, Pull }

    internal sealed class ZipperThemeShape
    {
        internal const double OpeningProgress = .10;
        internal const double PullExitProgress = .82;
        readonly double width, height, originX, originY, scale;
        readonly bool companion;

        internal ZipperThemeShape(double width, double height, double originX, double originY, bool companion = false)
        {
            this.width = width;
            this.height = height;
            this.originX = Math.Max(0, Math.Min(width, originX));
            this.originY = Math.Max(0, Math.Min(height, originY));
            scale = Math.Max(.65, Math.Min(1.4, height / 700));
            this.companion = companion;
        }

        internal void WriteOutline(double progress, ILiquidThemeOutline outline)
        {
            if (progress <= 0) return;
            if (companion && progress >= 1)
            {
                outline.MoveTo(0, 0);
                outline.LineTo(width, 0);
                outline.LineTo(width, height);
                outline.LineTo(0, height);
                outline.Close();
                return;
            }
            var frame = At(progress);
            outline.MoveTo(frame.Left.Top, frame.Top);
            outline.LineTo(frame.Right.Top, frame.Top);
            Edge(outline, frame, frame.Right, 0, false);
            outline.LineTo(frame.Left.Bottom, frame.Bottom);
            Edge(outline, frame, frame.Left, 0, true);
            outline.Close();
        }

        internal void WritePart(double progress, ZipperPart part, ILiquidThemeOutline outline)
        {
            if (progress <= 0 || progress >= 1) return;
            var frame = At(progress);
            switch (part)
            {
                case ZipperPart.Lining:
                    double lip = (2.5 + 8 * frame.Open) * scale;
                    Lining(outline, frame, frame.Left, -lip);
                    Lining(outline, frame, frame.Right, lip);
                    if (companion && frame.Pull < height)
                    {
                        outline.MoveTo(originX - scale, frame.Pull);
                        outline.LineTo(originX + scale, frame.Pull);
                        outline.LineTo(originX + scale, height);
                        outline.LineTo(originX - scale, height);
                        outline.Close();
                    }
                    break;
                case ZipperPart.Rim:
                    Lining(outline, frame, frame.Left, -1.4 * scale);
                    Lining(outline, frame, frame.Right, 1.4 * scale);
                    break;
                case ZipperPart.Teeth:
                    Teeth(outline, frame, progress);
                    break;
                case ZipperPart.Pull:
                    if (frame.Pull - 14 * scale < height) Pull(outline, frame.Pull, scale * Smooth(progress / .067));
                    break;
            }
        }

        internal static double HardwareOpacity(double progress)
        {
            return Smooth(progress / .065) * (1 - Smooth((progress - .88) / .12));
        }

        Frame At(double progress)
        {
            if (companion)
            {
                double pull = height * Math.Min(1, progress / .68);
                double introduction = Smooth(progress / .08);
                double open = Smooth(progress);
                double spread = Smooth((progress - .68) / .32);
                return new Frame(0, pull, pull, open,
                    Side(-1, originX, introduction, open, spread),
                    Side(1, width - originX, introduction, open, spread));
            }
            double tip = originY + (height + 25 * scale - originY) *
                Smooth((progress - .022) / (PullExitProgress - .022));
            double growth = Smooth(progress / .09);
            double opening = Smooth((progress - OpeningProgress) / (1 - OpeningProgress));
            double clearance = Smooth((progress - .72) / .28);
            double top = originY - (originY + 3 * scale) * Smooth((progress - .075) / .34);
            double bottom = Math.Min(height + 3 * scale, tip + 2 * scale * growth);
            return new Frame(top, bottom, tip, opening,
                Side(-1, originX, growth, opening, clearance),
                Side(1, width - originX, growth, opening, clearance));
        }

        Curve Side(int direction, double distance, double growth, double opening, double clearance)
        {
            double seam = 2.35 * scale * growth;
            double travel = Math.Max(0, distance + 3 * scale - seam) * opening;
            double curl = Math.Min(35 * scale, travel * .30) * (1 - clearance);
            double top = seam + travel;
            double bottom = seam + travel * clearance;
            return new Curve(
                originX + direction * top,
                originX + direction * (top + curl * .65),
                originX + direction * (bottom + (top - bottom) * .20 + curl * .12),
                originX + direction * bottom);
        }

        static void Edge(ILiquidThemeOutline outline, Frame frame, Curve curve, double offset, bool reverse)
        {
            double step = (frame.Bottom - frame.Top) / 3;
            if (reverse)
                outline.CurveTo(curve.Second + offset, frame.Bottom - step, curve.First + offset,
                    frame.Top + step, curve.Top + offset, frame.Top);
            else
                outline.CurveTo(curve.First + offset, frame.Top + step, curve.Second + offset,
                    frame.Bottom - step, curve.Bottom + offset, frame.Bottom);
        }

        static void Lining(ILiquidThemeOutline outline, Frame frame, Curve curve, double lip)
        {
            if (lip > 0)
            {
                outline.MoveTo(curve.Top, frame.Top);
                Edge(outline, frame, curve, 0, false);
                outline.LineTo(curve.Bottom + lip, frame.Bottom);
                Edge(outline, frame, curve, lip, true);
            }
            else
            {
                outline.MoveTo(curve.Top + lip, frame.Top);
                Edge(outline, frame, curve, lip, false);
                outline.LineTo(curve.Bottom, frame.Bottom);
                Edge(outline, frame, curve, 0, true);
            }
            outline.Close();
        }

        void Teeth(ILiquidThemeOutline outline, Frame frame, double progress)
        {
            double pitch = Math.Max(8.5 * scale, height / 90);
            double seamEnd = companion ? height : originY + (height - originY) * Smooth(progress / .24);
            double openEnd = Math.Min(height, frame.Pull - 8 * scale);
            double first = frame.Top + pitch / 2;
            for (double y = first; y < openEnd; y += pitch)
            {
                double t = (y - frame.Top) / (frame.Bottom - frame.Top);
                Tooth(outline, frame.Left.At(t) - 3.5 * scale, y, 1);
                Tooth(outline, frame.Right.At(t) + 3.5 * scale, y + pitch * .22, -1);
            }
            int firstClosed = Math.Max(0, (int)Math.Ceiling((frame.Pull + 9 * scale - originY) / (pitch * .6)));
            for (int i = firstClosed; originY + i * pitch * .6 < seamEnd; i++)
            {
                int direction = i % 2 == 0 ? 1 : -1;
                Tooth(outline, originX - direction * 3.45 * scale, originY + i * pitch * .6, direction);
            }
        }

        void Tooth(ILiquidThemeOutline outline, double x, double y, int direction)
        {
            double half = 3.6 * scale;
            double edge = 1.65 * scale;
            double lead = direction * half;
            double bevel = direction * 1.45 * scale;
            outline.MoveTo(x - lead, y - edge);
            outline.LineTo(x + lead - bevel, y - edge);
            outline.LineTo(x + lead, y + edge * .15);
            outline.LineTo(x + lead - bevel, y + edge);
            outline.LineTo(x - lead, y + edge);
            outline.Close();
        }

        void Pull(ILiquidThemeOutline outline, double y, double unit)
        {
            double x = originX;
            outline.MoveTo(x - 3 * unit, y - 10 * unit);
            outline.LineTo(x + 3 * unit, y - 10 * unit);
            outline.CurveTo(x + 4 * unit, y - 7 * unit, x + 8 * unit, y - 7 * unit, x + 8 * unit, y - 3 * unit);
            outline.LineTo(x + 6 * unit, y + 4 * unit);
            outline.CurveTo(x + 4 * unit, y + 6 * unit, x - 4 * unit, y + 6 * unit, x - 6 * unit, y + 4 * unit);
            outline.LineTo(x - 8 * unit, y - 3 * unit);
            outline.CurveTo(x - 8 * unit, y - 7 * unit, x - 4 * unit, y - 7 * unit, x - 3 * unit, y - 10 * unit);
            outline.Close();

            double ringCenter = y + 13 * unit;
            Oval(outline, x, ringCenter, 8 * unit, 10 * unit, false);
            // A reversed inner contour leaves a see-through ring on both native renderers.
            Oval(outline, x, ringCenter + .6 * unit, 4.55 * unit, 6.6 * unit, true);
        }

        static void Oval(ILiquidThemeOutline outline, double x, double y, double rx, double ry, bool reverse)
        {
            if (reverse) ry = -ry;
            double kx = rx * .5523, ky = ry * .5523;
            outline.MoveTo(x - rx, y);
            outline.CurveTo(x - rx, y - ky, x - kx, y - ry, x, y - ry);
            outline.CurveTo(x + kx, y - ry, x + rx, y - ky, x + rx, y);
            outline.CurveTo(x + rx, y + ky, x + kx, y + ry, x, y + ry);
            outline.CurveTo(x - kx, y + ry, x - rx, y + ky, x - rx, y);
            outline.Close();
        }

        static double Smooth(double value)
        {
            double t = Math.Max(0, Math.Min(1, value));
            return t * t * (3 - 2 * t);
        }

        struct Curve
        {
            internal readonly double Top, First, Second, Bottom;

            internal Curve(double top, double first, double second, double bottom)
            {
                Top = top;
                First = first;
                Second = second;
                Bottom = bottom;
            }

            internal double At(double t)
            {
                double back = 1 - t;
                return back * back * (back * Top + 3 * t * First) + t * t * (3 * back * Second + t * Bottom);
            }
        }

        struct Frame
        {
            internal readonly double Top, Bottom, Pull, Open;
            internal readonly Curve Left, Right;

            internal Frame(double top, double bottom, double pull, double open, Curve left, Curve right)
            {
                Top = top;
                Bottom = bottom;
                Pull = pull;
                Open = open;
                Left = left;
                Right = right;
            }
        }
    }
}

using System;
using System.Collections.Generic;

namespace CurveEditor.Core.Interaction
{
    /// <summary>
    /// The visible data window of the chart and its mapping to a plot rectangle in screen space
    /// (y grows downward on screen). Supports linear or logarithmic x.
    /// </summary>
    public sealed class ViewState
    {
        public const double MinLogX = 1e-3;
        private const double MinSpan = 1e-9;

        public double MinX { get; private set; } = 0;
        public double MaxX { get; private set; } = 60;
        public double MinY { get; private set; } = 0.8;
        public double MaxY { get; private set; } = 2;
        public bool LogX { get; private set; }

        /// <summary> Plot rectangle in screen units. </summary>
        public double Left { get; set; }
        public double Top { get; set; }
        public double Width { get; set; } = 800;
        public double Height { get; set; } = 500;

        public event EventHandler Changed;

        public ViewState Clone() => (ViewState)MemberwiseClone();

        public void SetWindow(double minX, double maxX, double minY, double maxY)
        {
            if (LogX)
            {
                minX = Math.Max(minX, MinLogX);
                maxX = Math.Max(maxX, minX * (1 + 1e-6));
            }
            else
            {
                minX = Math.Max(minX, 0);
            }

            if (maxX - minX < MinSpan) maxX = minX + MinSpan;
            if (maxY - minY < MinSpan) maxY = minY + MinSpan;

            MinX = minX;
            MaxX = maxX;
            MinY = minY;
            MaxY = maxY;
            Changed?.Invoke(this, EventArgs.Empty);
        }

        public void SetLogX(bool logX)
        {
            if (LogX == logX) return;
            LogX = logX;
            SetWindow(logX ? Math.Max(MinX, Math.Max(MaxX / 1000, MinLogX)) : MinX, MaxX, MinY, MaxY);
        }

        private double TX(double x) => LogX ? Math.Log(Math.Max(x, MinLogX * 1e-3)) : x;

        private double InvTX(double t) => LogX ? Math.Exp(t) : t;

        public double XToScreen(double x) => Left + (TX(x) - TX(MinX)) / (TX(MaxX) - TX(MinX)) * Width;

        public double YToScreen(double y) => Top + (MaxY - y) / (MaxY - MinY) * Height;

        public double ScreenToX(double sx) => InvTX(TX(MinX) + (sx - Left) / Width * (TX(MaxX) - TX(MinX)));

        public double ScreenToY(double sy) => MaxY - (sy - Top) / Height * (MaxY - MinY);

        public Point2 ToScreen(Point2 p) => new Point2(XToScreen(p.X), YToScreen(p.Y));

        public Point2 ToData(Point2 s) => new Point2(ScreenToX(s.X), ScreenToY(s.Y));

        /// <summary> Zooms around a screen point; factor &lt; 1 zooms in. </summary>
        public void ZoomAt(Point2 screen, double factorX, double factorY)
        {
            double t0 = TX(MinX), t1 = TX(MaxX);
            double ta = t0 + (screen.X - Left) / Width * (t1 - t0);
            double ya = ScreenToY(screen.Y);

            double nt0 = ta - (ta - t0) * factorX;
            double nt1 = ta + (t1 - ta) * factorX;
            double ny0 = ya - (ya - MinY) * factorY;
            double ny1 = ya + (MaxY - ya) * factorY;

            SetWindow(InvTX(nt0), InvTX(nt1), ny0, ny1);
        }

        /// <summary> Shifts the window by a screen-space delta (content follows the mouse). </summary>
        public void Pan(double dxScreen, double dyScreen)
        {
            double t0 = TX(MinX), t1 = TX(MaxX);
            double dt = -dxScreen / Width * (t1 - t0);
            double dy = dyScreen / Height * (MaxY - MinY);

            if (!LogX && t0 + dt < 0) dt = -t0;

            SetWindow(InvTX(t0 + dt), InvTX(t1 + dt), MinY + dy, MaxY + dy);
        }

        /// <summary> Zooms to a screen rectangle (box zoom). </summary>
        public void ZoomToScreenRect(Point2 a, Point2 b)
        {
            if (Math.Abs(a.X - b.X) < 4 || Math.Abs(a.Y - b.Y) < 4) return;
            var p = ToData(a);
            var q = ToData(b);
            SetWindow(Math.Min(p.X, q.X), Math.Max(p.X, q.X), Math.Min(p.Y, q.Y), Math.Max(p.Y, q.Y));
        }

        /// <summary>
        /// Fits y to the curve over the current x window (plus extra points such as handles),
        /// with padding. Keeps y = 1 in view so the baseline is always visible.
        /// </summary>
        public void FitY(Func<double, double> curve, IEnumerable<Point2> extra = null, int samples = 400)
        {
            double lo = 1, hi = 1;
            for (int i = 0; i <= samples; i++)
            {
                double x = ScreenToX(Left + Width * i / samples);
                double y = curve(x);
                if (double.IsNaN(y) || double.IsInfinity(y)) continue;
                lo = Math.Min(lo, y);
                hi = Math.Max(hi, y);
            }

            if (extra != null)
            {
                foreach (var p in extra)
                {
                    if (p.X < MinX || p.X > MaxX || double.IsNaN(p.Y) || double.IsInfinity(p.Y)) continue;
                    lo = Math.Min(lo, p.Y);
                    hi = Math.Max(hi, p.Y);
                }
            }

            double span = Math.Max(hi - lo, 0.05);
            SetWindow(MinX, MaxX, lo - span * 0.12, hi + span * 0.12);
        }

        /// <summary> "Nice" grid steps (1, 2, 5 × 10^k) targeting about <paramref name="targetLines"/> lines. </summary>
        public static double NiceStep(double span, int targetLines)
        {
            if (span <= 0 || double.IsNaN(span)) return 1;
            double raw = span / Math.Max(targetLines, 1);
            double mag = Math.Pow(10, Math.Floor(Math.Log10(raw)));
            double norm = raw / mag;
            double nice = norm < 1.5 ? 1 : norm < 3.5 ? 2 : norm < 7.5 ? 5 : 10;
            return nice * mag;
        }

        public IEnumerable<double> XTicks(int target = 10)
        {
            if (LogX)
            {
                int k0 = (int)Math.Floor(Math.Log10(MinX));
                int k1 = (int)Math.Ceiling(Math.Log10(MaxX));
                bool dense = k1 - k0 <= 2;
                for (int k = k0; k <= k1; k++)
                {
                    double d = Math.Pow(10, k);
                    foreach (int m in dense ? new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 } : new[] { 1, 2, 5 })
                    {
                        double x = m * d;
                        if (x >= MinX && x <= MaxX) yield return x;
                    }
                }
                yield break;
            }

            foreach (var t in LinearTicks(MinX, MaxX, target)) yield return t;
        }

        public IEnumerable<double> YTicks(int target = 8) => LinearTicks(MinY, MaxY, target);

        private static IEnumerable<double> LinearTicks(double min, double max, int target)
        {
            double step = NiceStep(max - min, target);
            double start = Math.Ceiling(min / step) * step;
            for (int i = 0; i < 1000; i++)
            {
                double v = start + i * step;
                if (v > max + step * 1e-9) yield break;
                yield return Math.Abs(v) < step * 1e-9 ? 0 : v;
            }
        }
    }
}

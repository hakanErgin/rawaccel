using System;
using System.Collections.Generic;
using System.Linq;

namespace CurveEditor.Core
{
    /// <summary> Turns lookup-mode control points into the table the driver reads, and back. </summary>
    public static class LutBuilder
    {
        /// <summary> Control-point limit when smoothing; leaves room for samples between points. </summary>
        public const int MaxSmoothControlPoints = 64;

        /// <param name="controlPoints"> (speed, sensitivity), x strictly increasing. </param>
        /// <param name="smooth"> Sample a monotone-preserving cubic through the points. </param>
        /// <param name="velocity"> Emit (speed, output velocity) pairs instead of (speed, sensitivity). </param>
        public static Point2[] Build(IReadOnlyList<Point2> controlPoints, bool smooth, bool velocity)
        {
            IEnumerable<Point2> points = smooth && controlPoints.Count > 2
                ? Sample(controlPoints, CurveArgs.MaxLutPoints)
                : controlPoints;

            return points
                .Select(p => velocity ? new Point2(p.X, p.X * p.Y) : p)
                .ToArray();
        }

        /// <summary> Inverse of <see cref="Build"/> for smooth == false: table entries become control points. </summary>
        public static List<Point2> ControlPointsFromLut(IEnumerable<Point2> lut, bool velocity) =>
            lut.Select(p => velocity ? new Point2(p.X, p.Y / p.X) : p).ToList();

        /// <summary>
        /// Samples the PCHIP spline through the control points into at most <paramref name="budget"/>
        /// points. Every control point is kept exactly; the remaining budget is split between
        /// segments in proportion to their width (in log-speed, so slow speeds get enough detail).
        /// </summary>
        public static List<Point2> Sample(IReadOnlyList<Point2> cps, int budget)
        {
            int n = cps.Count;
            var result = new List<Point2>(budget);
            if (n < 2)
            {
                result.AddRange(cps);
                return result;
            }

            var spline = new Pchip(cps);
            int extra = Math.Max(0, budget - n);

            var widths = new double[n - 1];
            double total = 0;
            for (int i = 0; i < n - 1; i++)
            {
                widths[i] = Math.Log(cps[i + 1].X) - Math.Log(cps[i].X);
                if (double.IsNaN(widths[i]) || double.IsInfinity(widths[i]) || widths[i] <= 0)
                {
                    widths[i] = 1e-3;
                }
                total += widths[i];
            }

            var counts = new int[n - 1];
            int assigned = 0;
            for (int i = 0; i < n - 1; i++)
            {
                counts[i] = (int)Math.Floor(extra * widths[i] / total);
                assigned += counts[i];
            }

            // hand out rounding leftovers to the widest segments
            foreach (int i in Enumerable.Range(0, n - 1).OrderByDescending(i => widths[i]))
            {
                if (assigned >= extra) break;
                counts[i]++;
                assigned++;
            }

            for (int i = 0; i < n - 1; i++)
            {
                var a = cps[i];
                var b = cps[i + 1];
                result.Add(a);
                int k = counts[i];
                for (int j = 1; j <= k; j++)
                {
                    double x = a.X + (b.X - a.X) * j / (k + 1);
                    // skip samples that would collapse onto a neighbour once stored as float
                    if ((float)x <= (float)result[result.Count - 1].X || (float)x >= (float)b.X) continue;
                    result.Add(new Point2(x, spline.Evaluate(x)));
                }
            }

            result.Add(cps[n - 1]);
            return result;
        }
    }

    /// <summary> Piecewise cubic Hermite interpolation with Fritsch–Carlson slopes (no overshoot). </summary>
    public sealed class Pchip
    {
        private readonly double[] xs;
        private readonly double[] ys;
        private readonly double[] ms;

        public Pchip(IReadOnlyList<Point2> points)
        {
            int n = points.Count;
            if (n < 2) throw new ArgumentException("need at least 2 points", nameof(points));

            xs = points.Select(p => p.X).ToArray();
            ys = points.Select(p => p.Y).ToArray();
            ms = new double[n];

            var h = new double[n - 1];
            var delta = new double[n - 1];
            for (int i = 0; i < n - 1; i++)
            {
                h[i] = xs[i + 1] - xs[i];
                delta[i] = (ys[i + 1] - ys[i]) / h[i];
            }

            if (n == 2)
            {
                ms[0] = ms[1] = delta[0];
                return;
            }

            for (int i = 1; i < n - 1; i++)
            {
                if (delta[i - 1] * delta[i] <= 0)
                {
                    ms[i] = 0;
                }
                else
                {
                    double w1 = 2 * h[i] + h[i - 1];
                    double w2 = h[i] + 2 * h[i - 1];
                    ms[i] = (w1 + w2) / (w1 / delta[i - 1] + w2 / delta[i]);
                }
            }

            ms[0] = EndSlope(h[0], h[1], delta[0], delta[1]);
            ms[n - 1] = EndSlope(h[n - 2], h[n - 3], delta[n - 2], delta[n - 3]);
        }

        private static double EndSlope(double h0, double h1, double d0, double d1)
        {
            double m = ((2 * h0 + h1) * d0 - h0 * d1) / (h0 + h1);
            if (Math.Sign(m) != Math.Sign(d0))
            {
                m = 0;
            }
            else if (Math.Sign(d0) != Math.Sign(d1) && Math.Abs(m) > Math.Abs(3 * d0))
            {
                m = 3 * d0;
            }
            return m;
        }

        public double Evaluate(double x)
        {
            int n = xs.Length;
            if (x <= xs[0]) return ys[0];
            if (x >= xs[n - 1]) return ys[n - 1];

            int i = Array.BinarySearch(xs, x);
            if (i >= 0) return ys[i];
            i = ~i - 1;

            double h = xs[i + 1] - xs[i];
            double t = (x - xs[i]) / h;
            double t2 = t * t;
            double t3 = t2 * t;

            double h00 = 2 * t3 - 3 * t2 + 1;
            double h10 = t3 - 2 * t2 + t;
            double h01 = -2 * t3 + 3 * t2;
            double h11 = t3 - t2;

            return h00 * ys[i] + h10 * h * ms[i] + h01 * ys[i + 1] + h11 * h * ms[i + 1];
        }
    }
}

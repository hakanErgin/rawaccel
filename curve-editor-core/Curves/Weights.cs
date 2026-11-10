using System;

namespace CurveEditor.Core.Curves
{
    /// <summary>
    /// A profile's domain and range weight for one direction of movement. The driver scales
    /// input speed by the domain weight before evaluating the curve, and scales how far the
    /// curve moves away from 1 by the range weight (rawaccel.hpp, callback_template).
    /// </summary>
    public readonly struct AxisWeights : IEquatable<AxisWeights>
    {
        public AxisWeights(double domain, double range)
        {
            // the driver rejects domain <= 0 and range < 0; show such values unweighted
            Domain = domain > 0 && !double.IsInfinity(domain) ? domain : 1;
            Range = range >= 0 && !double.IsInfinity(range) ? range : 1;
        }

        public static AxisWeights None { get; } = new AxisWeights(1, 1);

        public double Domain { get; }

        public double Range { get; }

        public bool IsNone => Domain == 1 && Range == 1;

        public bool Equals(AxisWeights o) => Domain == o.Domain && Range == o.Range;

        public override bool Equals(object obj) => obj is AxisWeights o && Equals(o);

        public override int GetHashCode() => Domain.GetHashCode() * 31 + Range.GetHashCode();
    }

    /// <summary> A curve as the driver applies it for one direction: 1 + (f(x·domain) − 1)·range. </summary>
    public sealed class WeightedCurve : ICurve
    {
        private readonly ICurve inner;

        public WeightedCurve(ICurve inner, AxisWeights weights)
        {
            this.inner = inner;
            Weights = weights;
        }

        public AxisWeights Weights { get; }

        public double Sensitivity(double x) => 1 + (inner.Sensitivity(x * Weights.Domain) - 1) * Weights.Range;
    }

    /// <summary>
    /// Maps between the curve's own coordinates (the speeds and multipliers in its parameters)
    /// and the chart, which shows input speed against what the driver applies for that
    /// direction (domain and range weights, then the display scale).
    /// </summary>
    public readonly struct ChartMapping
    {
        public ChartMapping(AxisWeights weights, double scale)
        {
            Weights = weights;
            Scale = scale;
        }

        public AxisWeights Weights { get; }

        /// <summary> Display multiplier (base sensitivity), or 1. </summary>
        public double Scale { get; }

        public ICurve Apply(ICurve curve)
        {
            if (curve == null) return null;
            var weighted = Weights.IsNone ? curve : new WeightedCurve(curve, Weights);
            return new ScaledCurve(weighted, Scale);
        }

        public double XToChart(double x) => x / Weights.Domain;

        public double XToCurve(double x) => x * Weights.Domain;

        public double YToChart(double y) => (1 + (y - 1) * Weights.Range) * (Scale == 0 ? 1 : Scale);

        /// <summary> NaN when the range weight is 0: every multiplier then shows as 1. </summary>
        public double YToCurve(double y)
        {
            double unscaled = Scale == 0 ? y : y / Scale;
            return Weights.Range == 0 ? double.NaN : 1 + (unscaled - 1) / Weights.Range;
        }

        public Point2 ToChart(Point2 p) => new Point2(XToChart(p.X), YToChart(p.Y));

        public Point2 ToCurve(Point2 p) => new Point2(XToCurve(p.X), YToCurve(p.Y));
    }

    /// <summary> The applied profile's settings that matter for placing live mouse movement on its curve. </summary>
    public sealed class LiveProfile
    {
        public bool Separate { get; set; }
        public double Sensitivity { get; set; } = 1;
        public double YXRatio { get; set; } = 1;
        public double LRRatio { get; set; } = 1;
        public double UDRatio { get; set; } = 1;
        public double DomainX { get; set; } = 1;
        public double DomainY { get; set; } = 1;
        public double RangeX { get; set; } = 1;
        public double RangeY { get; set; } = 1;
        public double LpNorm { get; set; } = 2;
    }

    /// <summary> Where a mouse report sits: its input speed and the weights the driver used for its direction. </summary>
    public readonly struct LiveSample
    {
        public LiveSample(double speed, AxisWeights weights, double vertical = 0)
        {
            Speed = speed;
            Weights = weights;
            Vertical = vertical;
        }

        /// <summary> Input speed, measured like the driver's distance mode (before domain weights). </summary>
        public double Speed { get; }

        /// <summary> Weights that apply to this movement's direction (horizontal and vertical blend by angle). </summary>
        public AxisWeights Weights { get; }

        /// <summary>
        /// One curve for both axes: how vertical the movement was, 0 (horizontal) to 1 (vertical),
        /// the angle blend the driver uses for range weights. 0 for a by-component profile.
        /// </summary>
        public double Vertical { get; }
    }

    public static class LiveInput
    {
        /// <summary>
        /// Converts a raw (already accelerated) mouse report into the input speed the driver saw,
        /// by inverting rawaccel.hpp modify: undo the output multipliers, then solve
        /// output speed = input speed × weighted curve × sensitivity. <paramref name="axisCurve"/>
        /// is the applied curve (the Y one when <paramref name="yAxis"/> and the profile is by
        /// component). Returns null if the report can't be placed. Speed cap and smoothing are ignored.
        /// </summary>
        public static LiveSample? Solve(LiveProfile p, ICurve axisCurve, bool yAxis, double dx, double dy, double timeMs)
        {
            if (p == null || axisCurve == null || !(timeMs > 0)) return null;

            // undo the driver's per-axis output multipliers (applied after the curve) before measuring speed
            double lr = dx < 0 ? p.LRRatio : 1;
            double ud = dy < 0 ? p.UDRatio : 1;
            // the driver allows negative (flipping) sensitivity and Y/X ratios; only their size matters here
            if (lr <= 0 || ud <= 0 || p.YXRatio == 0 || p.Sensitivity == 0) return null;
            double ax = Math.Abs(dx / lr);
            double ay = Math.Abs(dy / (Math.Abs(p.YXRatio) * ud));

            double counts, vertical = 0;
            AxisWeights weights;
            if (p.Separate)
            {
                // each axis is scaled by its own curve, from its own (domain-weighted) speed
                counts = yAxis ? ay : ax;
                weights = yAxis ? new AxisWeights(p.DomainY, p.RangeY) : new AxisWeights(p.DomainX, p.RangeX);
            }
            else
            {
                // the curve scales both axes equally, so the output keeps the input's direction
                counts = Distance(ax, ay, p.LpNorm);
                if (counts == 0) return null;
                // speed the curve sees = distance of the domain-weighted input (calc_speed_whole)
                double domain = Distance(ax * Domain(p.DomainX), ay * Domain(p.DomainY), p.LpNorm) / counts;
                double rx = Range(p.RangeX), ry = Range(p.RangeY);
                double angle = ay == 0 ? 0 : ax == 0 ? Math.PI / 2 : Math.Atan(ay / ax);
                vertical = 2 / Math.PI * angle;
                double range = rx == ry ? rx : rx + vertical * (ry - rx);
                weights = new AxisWeights(domain, range);
            }
            if (counts == 0) return null;

            double outSpeed = counts / timeMs / Math.Abs(p.Sensitivity);
            var curve = new WeightedCurve(axisCurve, weights);
            // a range weight above 1 can push the multiplier below 0 (the driver then flips the
            // output), so match the output's size, not its sign
            double v = Solver.Bisect(x => Math.Abs(curve.Velocity(x)), outSpeed, 1e-4, 1e5, logScale: true, clamp: false);
            return double.IsNaN(v) ? (LiveSample?)null : new LiveSample(v, weights, vertical);
        }

        /// <summary> rawaccel.hpp calc_speed_whole: max for p ≥ 16 (or ≤ 0), Euclidean for 2, Lp otherwise. </summary>
        public static double Distance(double ax, double ay, double p) =>
            p >= 16 || p <= 0 ? Math.Max(ax, ay)
            : p == 2 ? Math.Sqrt(ax * ax + ay * ay)
            : Math.Pow(Math.Pow(ax, p) + Math.Pow(ay, p), 1 / p);

        private static double Domain(double d) => new AxisWeights(d, 1).Domain;

        private static double Range(double r) => new AxisWeights(1, r).Range;
    }
}

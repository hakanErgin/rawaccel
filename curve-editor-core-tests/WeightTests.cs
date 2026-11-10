using System;
using CurveEditor.Core.Curves;
using CurveEditor.Core.Interaction;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CurveEditor.Core.Tests
{
    [TestClass]
    public class WeightTests
    {
        private sealed class Fn : ICurve
        {
            private readonly Func<double, double> f;
            public Fn(Func<double, double> f) { this.f = f; }
            public double Sensitivity(double x) => f(x);
        }

        // sensitivity rising with speed, then levelling off (output speed keeps rising)
        private static readonly ICurve CurveX = new Fn(x => 1 + 1.5 * x / (x + 20));
        private static readonly ICurve CurveY = new Fn(x => 1 + 0.02 * x);

        [TestMethod]
        public void WeightedCurveMatchesDriverCallback()
        {
            var w = new WeightedCurve(CurveX, new AxisWeights(2, 0.5));
            foreach (double x in new[] { 0.5, 7, 40 })
            {
                // rawaccel.hpp: 1 + (accel_fn(speed · domain) − 1) · range
                Assert.AreEqual(1 + (CurveX.Sensitivity(x * 2) - 1) * 0.5, w.Sensitivity(x), 1e-12);
            }
        }

        [TestMethod]
        public void InvalidWeightsShowUnweighted()
        {
            Assert.IsTrue(new AxisWeights(0, -1).IsNone);
            Assert.IsTrue(new AxisWeights(double.NaN, double.NaN).IsNone);
            Assert.AreEqual(0, new AxisWeights(1, 0).Range);
        }

        [TestMethod]
        public void ChartMappingRoundTripsAndPlacesHandlesOnTheCurve()
        {
            var map = new ChartMapping(new AxisWeights(2.5, 0.4), 1.7);
            var chart = map.Apply(CurveX);
            foreach (double x in new[] { 1.0, 12, 60 })
            {
                // a point on the bare curve lands on the drawn curve
                var p = map.ToChart(new Point2(x, CurveX.Sensitivity(x)));
                Assert.AreEqual(chart.Sensitivity(p.X), p.Y, 1e-12);
                var back = map.ToCurve(p);
                Assert.AreEqual(x, back.X, 1e-12);
                Assert.AreEqual(CurveX.Sensitivity(x), back.Y, 1e-12);
            }
        }

        [TestMethod]
        public void ZeroRangeWeightHasNoVerticalInverse()
        {
            var map = new ChartMapping(new AxisWeights(1, 0), 2);
            Assert.AreEqual(2, map.ToChart(new Point2(5, 3)).Y, 1e-12);
            Assert.IsTrue(double.IsNaN(map.ToCurve(new Point2(5, 3)).Y));
        }

        /// <summary> Forward model of rawaccel.hpp modify (no rotation, snap, speed cap or smoothing). </summary>
        private static (double X, double Y) Driver(LiveProfile p, double inX, double inY, double time)
        {
            double vx = Math.Abs(inX / time) * p.DomainX, vy = Math.Abs(inY / time) * p.DomainY;
            double outX, outY;
            if (p.Separate)
            {
                outX = inX * (1 + (CurveX.Sensitivity(vx) - 1) * p.RangeX);
                outY = inY * (1 + (CurveY.Sensitivity(vy) - 1) * p.RangeY);
            }
            else
            {
                double speed = LiveInput.Distance(vx, vy, p.LpNorm);
                double weight = p.RangeX;
                if (p.RangeX != p.RangeY)
                {
                    double angle = inY == 0 ? 0 : inX == 0 ? Math.PI / 2 : Math.Atan(Math.Abs(inY / inX));
                    weight += 2 / Math.PI * angle * (p.RangeY - p.RangeX);
                }
                double scale = 1 + (CurveX.Sensitivity(speed) - 1) * weight;
                outX = inX * scale;
                outY = inY * scale;
            }
            outX *= p.Sensitivity;
            outY *= p.Sensitivity * p.YXRatio;
            if (outX < 0) outX *= p.LRRatio;
            if (outY < 0) outY *= p.UDRatio;
            return (outX, outY);
        }

        private static LiveProfile Profile(bool separate, double lp) => new LiveProfile
        {
            Separate = separate,
            Sensitivity = 1.3,
            YXRatio = -1.2,
            LRRatio = 0.9,
            UDRatio = 1.1,
            DomainX = 1.5,
            DomainY = 0.6,
            RangeX = 0.8,
            RangeY = 1.6,
            LpNorm = lp,
        };

        [DataTestMethod]
        [DataRow(2.0)]
        [DataRow(3.0)]
        [DataRow(16.0)]
        public void WholeModeRecoversInputSpeedAndDirectionCurve(double lp)
        {
            var p = Profile(false, lp);
            const double time = 1;
            foreach (var (inX, inY) in new[] { (30.0, 0.0), (0.0, -25.0), (-12.0, 9.0), (4.0, 40.0) })
            {
                var (outX, outY) = Driver(p, inX, inY, time);
                var s = LiveInput.Solve(p, CurveX, false, outX, outY, time);
                Assert.IsNotNull(s);
                double speed = LiveInput.Distance(Math.Abs(inX), Math.Abs(inY), lp) / time;
                Assert.AreEqual(speed, s.Value.Speed, 1e-6 * speed, $"input ({inX}, {inY})");

                // the direction's curve gives the scale the driver applied
                double applied = Math.Abs(inX) > 0 ? outX / p.Sensitivity / inX / (outX < 0 ? p.LRRatio : 1)
                    : outY / (p.Sensitivity * p.YXRatio) / inY / (outY < 0 ? p.UDRatio : 1);
                Assert.AreEqual(applied, new WeightedCurve(CurveX, s.Value.Weights).Sensitivity(s.Value.Speed), 1e-6);
            }
        }

        [TestMethod]
        public void PureDirectionsUseThatAxisWeights()
        {
            var p = Profile(false, 2);
            var (hx, hy) = Driver(p, 30, 0, 1);
            var h = LiveInput.Solve(p, CurveX, false, hx, hy, 1).Value;
            Assert.AreEqual(new AxisWeights(p.DomainX, p.RangeX).Domain, h.Weights.Domain, 1e-12);
            Assert.AreEqual(p.RangeX, h.Weights.Range, 1e-12);

            var (vx, vy) = Driver(p, 0, 30, 1);
            var v = LiveInput.Solve(p, CurveX, false, vx, vy, 1).Value;
            Assert.AreEqual(p.DomainY, v.Weights.Domain, 1e-12);
            Assert.AreEqual(p.RangeY, v.Weights.Range, 1e-12);
            Assert.AreEqual(0, h.Vertical, 1e-12);
            Assert.AreEqual(1, v.Vertical, 1e-12);

            var (dx, dy) = Driver(p, 20, 20, 1);
            Assert.AreEqual(0.5, LiveInput.Solve(p, CurveX, false, dx, dy, 1).Value.Vertical, 1e-12);
        }

        [TestMethod]
        public void NegativeWeightedMultiplierIsStillPlaced()
        {
            // constant 0.5 with range 3: the driver scales by 1 + (0.5 − 1)·3 = −0.5 and flips the output
            var half = new Fn(x => 0.5);
            var p = new LiveProfile { RangeX = 3, RangeY = 3 };
            var s = LiveInput.Solve(p, half, false, -10, 0, 1);
            Assert.IsNotNull(s);
            Assert.AreEqual(20, s.Value.Speed, 1e-6);
            Assert.AreEqual(-0.5, new WeightedCurve(half, s.Value.Weights).Sensitivity(s.Value.Speed), 1e-12);
        }

        [DataTestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public void SeparateModeRecoversEachAxis(bool yAxis)
        {
            var p = Profile(true, 2);
            const double time = 2;
            var (outX, outY) = Driver(p, -24, 50, time);
            var s = LiveInput.Solve(p, yAxis ? CurveY : CurveX, yAxis, outX, outY, time);
            Assert.IsNotNull(s);
            Assert.AreEqual(yAxis ? 25 : 12, s.Value.Speed, 1e-6);
            Assert.AreEqual(yAxis ? p.RangeY : p.RangeX, s.Value.Weights.Range, 1e-12);
        }

        [TestMethod]
        public void UnweightedProfileMatchesThePlainInversion()
        {
            var p = new LiveProfile { LpNorm = 2 };
            var s = LiveInput.Solve(p, CurveX, false, 30, 40, 1).Value;
            Assert.IsTrue(s.Weights.IsNone);
            Assert.AreEqual(50, CurveX.Velocity(s.Speed), 1e-6);
        }

        [TestMethod]
        public void PeakTrackerKeepsTheTagOfThePeak()
        {
            var t = new PeakTracker(5);
            t.Add(0, 10, "a");
            t.Add(1, 30, "b");
            t.Add(2, 20, "c");
            Assert.AreEqual("b", t.PeakTag);
            t.Update(6.5);
            Assert.AreEqual(20, t.Peak);
            Assert.AreEqual("c", t.PeakTag);
        }
    }
}

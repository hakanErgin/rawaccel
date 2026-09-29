using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CurveEditor.Core.Tests
{
    [TestClass]
    public class LutAndSidecarTests
    {
        private static readonly List<Point2> Rising = new List<Point2>
        {
            new Point2(0.5, 1), new Point2(3, 1.05), new Point2(8, 1.3), new Point2(20, 1.7), new Point2(60, 1.9),
        };

        [TestMethod]
        public void PchipInterpolatesControlPoints()
        {
            var s = new Pchip(Rising);
            foreach (var p in Rising)
            {
                Assert.AreEqual(p.Y, s.Evaluate(p.X), 1e-12);
            }
        }

        [TestMethod]
        public void PchipPreservesMonotonicity()
        {
            var s = new Pchip(Rising);
            double prev = double.MinValue;
            for (double x = 0.5; x <= 60; x += 0.01)
            {
                double y = s.Evaluate(x);
                Assert.IsTrue(y >= prev - 1e-12, $"not monotone at {x}");
                prev = y;
            }
        }

        [TestMethod]
        public void PchipDoesNotOvershootLocalExtremes()
        {
            var bump = new List<Point2> { new Point2(1, 1), new Point2(5, 2), new Point2(10, 1), new Point2(20, 1.5) };
            var s = new Pchip(bump);
            for (double x = 1; x <= 20; x += 0.01)
            {
                Assert.IsTrue(s.Evaluate(x) <= 2 + 1e-12);
                Assert.IsTrue(s.Evaluate(x) >= 1 - 1e-12);
            }
        }

        [TestMethod]
        public void SmoothTableRespectsDriverLimits()
        {
            var cps = Enumerable.Range(1, LutBuilder.MaxSmoothControlPoints).Select(i => new Point2(i * 0.7, 1 + i * 0.01)).ToList();
            foreach (bool velocity in new[] { true, false })
            {
                var lut = LutBuilder.Build(cps, smooth: true, velocity: velocity);
                Assert.IsTrue(lut.Length <= CurveArgs.MaxLutPoints);
                Assert.IsTrue(lut.Length > cps.Count);
                for (int i = 1; i < lut.Length; i++)
                {
                    Assert.IsTrue((float)lut[i].X > (float)lut[i - 1].X, "x must be strictly increasing as float");
                }
                foreach (var p in cps)
                {
                    Assert.IsTrue(lut.Any(q => q.X == p.X), "control points are kept exactly");
                }
            }
        }

        [TestMethod]
        public void VelocityTableIsSpeedTimesSensitivity()
        {
            var lut = LutBuilder.Build(Rising, smooth: false, velocity: true);
            for (int i = 0; i < Rising.Count; i++)
            {
                Assert.AreEqual(Rising[i].X * Rising[i].Y, lut[i].Y, 1e-12);
            }
        }

        [TestMethod]
        public void SidecarRestoresMatchingControlPoints()
        {
            var args = new CurveArgs { Mode = CurveMode.Lookup, Gain = true, SmoothLut = true, ControlPoints = new List<Point2>(Rising) };
            var entry = CustomCurveSidecar.ToEntry(args);
            var driverTable = args.BuildLut().Select(p => new Point2((float)p.X, (float)p.Y)).ToList();

            var loaded = new CurveArgs { Mode = CurveMode.Lookup, Gain = true };
            CustomCurveSidecar.Restore(loaded, driverTable, entry);

            Assert.IsTrue(loaded.SmoothLut);
            CollectionAssert.AreEqual(Rising, loaded.ControlPoints);
        }

        [TestMethod]
        public void SidecarIgnoredWhenTableChangedElsewhere()
        {
            var args = new CurveArgs { Mode = CurveMode.Lookup, Gain = false, SmoothLut = true, ControlPoints = new List<Point2>(Rising) };
            var entry = CustomCurveSidecar.ToEntry(args);
            var edited = new List<Point2> { new Point2(1, 1), new Point2(10, 2) };

            var loaded = new CurveArgs { Mode = CurveMode.Lookup, Gain = false };
            CustomCurveSidecar.Restore(loaded, edited, entry);

            Assert.IsFalse(loaded.SmoothLut);
            CollectionAssert.AreEqual(edited, loaded.ControlPoints);
        }

        [TestMethod]
        public void SidecarSaveLoadRoundTrip()
        {
            var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
            try
            {
                var s = new CustomCurveSidecar();
                s.Profiles["default"] = new CustomCurveSidecar.ProfileEntry
                {
                    X = CustomCurveSidecar.ToEntry(new CurveArgs { SmoothLut = true, ControlPoints = new List<Point2>(Rising) }),
                };
                s.Save(path);

                var loaded = CustomCurveSidecar.Load(path);
                Assert.IsTrue(loaded.Profiles["default"].X.Smooth);
                Assert.AreEqual(Rising.Count, loaded.Profiles["default"].X.Points.Count);
                Assert.IsNull(loaded.Profiles["default"].Y);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [TestMethod]
        public void BrokenSidecarLoadsEmpty()
        {
            var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
            try
            {
                File.WriteAllText(path, "{ not json");
                Assert.AreEqual(0, CustomCurveSidecar.Load(path).Profiles.Count);
            }
            finally
            {
                File.Delete(path);
            }
        }
    }
}

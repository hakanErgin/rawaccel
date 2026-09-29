using System;
using System.Collections.Generic;
using System.Linq;
using CurveEditor.Core.Handles;
using CurveEditor.Core.Curves;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CurveEditor.Core.Tests
{
    [TestClass]
    public class HandleTests
    {
        private static readonly HandleContext Ctx = new HandleContext(30, 70);

        private static readonly double[] SampleXs =
            Enumerable.Range(0, 60).Select(i => 0.05 * Math.Pow(10, i * 3.5 / 59)).ToArray();

        private static IEnumerable<(string Name, CurveArgs Args)> Cases()
        {
            foreach (bool gain in new[] { true, false })
            {
                string g = gain ? "gain" : "legacy";
                yield return ($"classic-io-{g}", new CurveArgs { Mode = CurveMode.Classic, Gain = gain, Acceleration = 0.02, CapX = 20, CapY = 1.8, CapMode = CapType.InOut, InputOffset = 2 });
                yield return ($"classic-out-{g}", new CurveArgs { Mode = CurveMode.Classic, Gain = gain, Acceleration = 0.02, CapY = 1.6, CapMode = CapType.Output, ExponentClassic = 3 });
                yield return ($"classic-in-{g}", new CurveArgs { Mode = CurveMode.Classic, Gain = gain, Acceleration = 0.01, CapX = 25, CapMode = CapType.Input });
                yield return ($"classic-uncapped-{g}", new CurveArgs { Mode = CurveMode.Classic, Gain = gain, Acceleration = 0.01, CapY = 0, CapMode = CapType.Output });
                yield return ($"power-io-{g}", new CurveArgs { Mode = CurveMode.Power, Gain = gain, Scale = 0.8, ExponentPower = 0.3, CapX = 30, CapY = 1.6, CapMode = CapType.InOut });
                yield return ($"power-out-{g}", new CurveArgs { Mode = CurveMode.Power, Gain = gain, Scale = 0.8, ExponentPower = 0.3, OutputOffset = 0.5, CapY = 1.6, CapMode = CapType.Output });
                yield return ($"power-in-{g}", new CurveArgs { Mode = CurveMode.Power, Gain = gain, Scale = 0.8, ExponentPower = 0.3, OutputOffset = 0.5, CapX = 40, CapMode = CapType.Input });
                yield return ($"power-uncapped-{g}", new CurveArgs { Mode = CurveMode.Power, Gain = gain, Scale = 0.8, ExponentPower = 0.3, OutputOffset = 0.5, CapY = 0, CapMode = CapType.Output });
                yield return ($"natural-{g}", new CurveArgs { Mode = CurveMode.Natural, Gain = gain, Limit = 1.8, DecayRate = 0.1, InputOffset = 3 });
                yield return ($"jump-{g}", new CurveArgs { Mode = CurveMode.Jump, Gain = gain, CapX = 15, CapY = 1.5, Smooth = 0.5 });
                yield return ($"synchronous-{g}", new CurveArgs { Mode = CurveMode.Synchronous, Gain = gain, SyncSpeed = 8, Motivity = 1.6, Gamma = 1, Smooth = 0.5 });
                yield return ($"lookup-{g}", new CurveArgs { Mode = CurveMode.Lookup, Gain = gain, ControlPoints = new List<Point2> { new Point2(1, 1), new Point2(10, 1.3), new Point2(40, 1.8) } });
                yield return ($"lookup-smooth-{g}", new CurveArgs { Mode = CurveMode.Lookup, Gain = gain, SmoothLut = true, ControlPoints = new List<Point2> { new Point2(1, 1), new Point2(10, 1.3), new Point2(20, 1.5), new Point2(40, 1.8) } });
            }
        }

        // MSTest serializes DynamicData arguments, so pass case names and rebuild the args
        public static IEnumerable<object[]> AllModeArgs() => Cases().Select(c => new object[] { c.Name });

        private static CurveArgs Get(string name) => Cases().Single(c => c.Name == name).Args;

        private static void AssertSameCurve(CurveArgs expected, CurveArgs actual, double relTol = 1e-6)
        {
            var e = CurveMath.Create(expected);
            var a = CurveMath.Create(actual);
            foreach (double x in SampleXs)
            {
                double ye = e.Sensitivity(x);
                double ya = a.Sensitivity(x);
                Assert.AreEqual(ye, ya, 1e-9 + relTol * Math.Abs(ye), $"x={x}");
            }
        }

        private static Handle Find(CurveArgs a, string id) =>
            HandleSets.For(a.Mode).GetHandles(a, Ctx).Single(h => h.Id == id);

        [TestMethod]
        [DynamicData(nameof(AllModeArgs), DynamicDataSourceType.Method)]
        public void NormalizePreservesCurve(string name)
        {
            var args = Get(name);
            AssertSameCurve(args, HandleSets.For(args.Mode).Normalize(args), 1e-6);
        }

        [TestMethod]
        [DynamicData(nameof(AllModeArgs), DynamicDataSourceType.Method)]
        public void DraggingHandleOntoItselfKeepsCurve(string name)
        {
            var args = Get(name);
            var set = HandleSets.For(args.Mode);
            foreach (var h in set.GetHandles(args, Ctx))
            {
                var after = set.Drag(args, h.Id, h.Position, Ctx);
                AssertSameCurve(args, after, 1e-5);
            }
        }

        [TestMethod]
        [DynamicData(nameof(AllModeArgs), DynamicDataSourceType.Method)]
        public void HandlesFollowSmallDrags(string name)
        {
            var args = Get(name);
            var set = HandleSets.For(args.Mode);
            foreach (var h in set.GetHandles(args, Ctx))
            {
                // a small, valid move in the handle's free directions
                double dx = h.Constraint == HandleConstraint.YOnly ? 0 : Math.Max(Math.Abs(h.Position.X) * 0.05, 0.1);
                double dy = h.Constraint == HandleConstraint.XOnly ? 0 : 0.02;
                var target = new Point2(h.Position.X + dx, h.Position.Y + dy);

                var after = set.Drag(args, h.Id, target, Ctx);
                var moved = set.GetHandles(after, Ctx).Single(q => q.Id == h.Id);

                if (h.Constraint != HandleConstraint.YOnly)
                {
                    Assert.AreEqual(target.X, moved.Position.X, 1e-4 * Math.Max(1, target.X), $"{args.Mode} {h.Id} x");
                }
                // smoothness has a bounded effect; the handle stops once smooth reaches 0 or 1
                bool atSmoothLimit = h.Id == HandleIds.Smooth && (after.Smooth == 0 || after.Smooth == 1);

                if (h.Constraint != HandleConstraint.XOnly && !atSmoothLimit)
                {
                    Assert.AreEqual(target.Y, moved.Position.Y, 1e-4, $"{args.Mode} {h.Id} y");
                }
            }
        }

        [TestMethod]
        [DynamicData(nameof(AllModeArgs), DynamicDataSourceType.Method)]
        public void ExtremeDragsStayValid(string name)
        {
            var args = Get(name);
            var rng = new Random(1234);
            var set = HandleSets.For(args.Mode);
            var current = args;

            for (int i = 0; i < 300; i++)
            {
                var handles = set.GetHandles(current, Ctx);
                var h = handles[rng.Next(handles.Count)];
                double scale = Math.Pow(10, rng.NextDouble() * 4 - 2);
                var target = new Point2(
                    h.Position.X + (rng.NextDouble() - 0.5) * 2 * scale * 20,
                    h.Position.Y + (rng.NextDouble() - 0.5) * 2 * scale);

                current = set.Drag(current, h.Id, target, Ctx);

                var errors = Validation.Validate(current);
                Assert.AreEqual(0, errors.Count, $"{args.Mode} after dragging {h.Id} to {target}: {string.Join("; ", errors)}");

                var curve = CurveMath.Create(current);
                foreach (double x in SampleXs)
                {
                    double y = curve.Sensitivity(x);
                    Assert.IsFalse(double.IsNaN(y) || double.IsInfinity(y), $"{args.Mode} {h.Id} produced {y} at x={x}");
                }
            }
        }

        [TestMethod]
        public void DragDoesNotMutateInput()
        {
            var args = new CurveArgs { Mode = CurveMode.Lookup, ControlPoints = new List<Point2> { new Point2(1, 1), new Point2(10, 2) } };
            var copy = args.Clone();
            HandleSets.For(CurveMode.Lookup).Drag(args, HandleIds.LutPoint(0), new Point2(5, 5), Ctx);
            Assert.AreEqual(copy, args);
        }

        [TestMethod]
        public void ClassicCapHandleSetsCapPoint()
        {
            foreach (bool gain in new[] { true, false })
            {
                var args = new CurveArgs { Mode = CurveMode.Classic, Gain = gain, Acceleration = 0.02, CapY = 1.5, CapMode = CapType.Output };
                var after = HandleSets.For(CurveMode.Classic).Drag(args, HandleIds.Cap, new Point2(25, 1.9), Ctx);
                Assert.AreEqual(CapType.Output, after.CapMode);
                Assert.AreEqual(1.9, after.CapY, 1e-12);

                var cap = Find(after, HandleIds.Cap);
                Assert.AreEqual(25, cap.Position.X, 1e-6);

                // beyond the cap, legacy is flat at the cap and gain approaches it
                double far = CurveMath.Sensitivity(after, 1000);
                if (gain) Assert.IsTrue(far < 1.9 && far > 1.85);
                else Assert.AreEqual(1.9, far, 1e-12);
            }
        }

        [TestMethod]
        public void ClassicOffsetDragKeepsCapPoint()
        {
            var args = new CurveArgs { Mode = CurveMode.Classic, Gain = true, Acceleration = 0.02, CapY = 1.5, CapMode = CapType.Output };
            var before = Find(args, HandleIds.Cap).Position;
            var after = HandleSets.For(CurveMode.Classic).Drag(args, HandleIds.Offset, new Point2(4, 1), Ctx);
            Assert.AreEqual(4, after.InputOffset, 1e-12);
            var cap = Find(after, HandleIds.Cap).Position;
            Assert.AreEqual(before.X, cap.X, 1e-6);
            Assert.AreEqual(before.Y, cap.Y, 1e-12);
        }

        [TestMethod]
        public void BendChangesExponentButKeepsReferencePoint()
        {
            var classic = new CurveArgs { Mode = CurveMode.Classic, Gain = true, Acceleration = 0.02, CapY = 1.5, CapMode = CapType.Output };
            var bend = Find(classic, HandleIds.Bend);
            var capBefore = Find(classic, HandleIds.Cap).Position;
            var after = HandleSets.For(CurveMode.Classic).Drag(classic, HandleIds.Bend, new Point2(bend.Position.X, bend.Position.Y - 0.05), Ctx);
            Assert.IsTrue(after.ExponentClassic > classic.ExponentClassic);
            var capAfter = Find(after, HandleIds.Cap).Position;
            Assert.AreEqual(capBefore.X, capAfter.X, 1e-6);
            Assert.AreEqual(capBefore.Y, capAfter.Y, 1e-9);
        }

        [TestMethod]
        public void SetCappedTogglesCap()
        {
            var args = new CurveArgs { Mode = CurveMode.Classic, Acceleration = 0.02, CapY = 1.5, CapMode = CapType.Output };
            Assert.IsTrue(HandleSets.IsCapped(args));
            var off = HandleSets.SetCapped(args, false, Ctx);
            Assert.IsFalse(HandleSets.IsCapped(off));
            var on = HandleSets.SetCapped(off, true, Ctx);
            Assert.IsTrue(HandleSets.IsCapped(on));
            Assert.AreEqual(0, Validation.Validate(on).Count);
        }

        [TestMethod]
        public void LookupPointsStaySorted()
        {
            var args = new CurveArgs
            {
                Mode = CurveMode.Lookup,
                ControlPoints = new List<Point2> { new Point2(1, 1), new Point2(10, 1.5), new Point2(20, 2) },
            };
            var set = HandleSets.For(CurveMode.Lookup);
            var after = set.Drag(args, HandleIds.LutPoint(1), new Point2(50, 1.7), Ctx);
            Assert.IsTrue(after.ControlPoints[1].X < 20);
            Assert.AreEqual(1.7, after.ControlPoints[1].Y);

            after = set.Drag(args, HandleIds.LutPoint(1), new Point2(-5, -1), Ctx);
            Assert.IsTrue(after.ControlPoints[1].X > 1);
            Assert.IsTrue(after.ControlPoints[1].Y > 0);
        }

        [TestMethod]
        public void LookupAddRemove()
        {
            var args = new CurveArgs
            {
                Mode = CurveMode.Lookup,
                ControlPoints = new List<Point2> { new Point2(1, 1), new Point2(20, 2) },
            };
            Assert.AreEqual(1, LookupHandles.AddPoint(args, new Point2(10, 1.4)));
            Assert.AreEqual(3, args.ControlPoints.Count);
            Assert.AreEqual(-1, LookupHandles.AddPoint(args, new Point2(10, 1.9)), "duplicate x");
            Assert.AreEqual(3, LookupHandles.AddPoint(args, new Point2(30, 2.1)));
            Assert.IsTrue(LookupHandles.RemovePoint(args, 0));
            Assert.IsTrue(LookupHandles.RemovePoint(args, 0));
            Assert.IsFalse(LookupHandles.RemovePoint(args, 0), "keeps at least two points");
        }

        [TestMethod]
        public void SampleCurveApproximatesSource()
        {
            var classic = new CurveArgs { Mode = CurveMode.Classic, Acceleration = 0.02, CapY = 1.5, CapMode = CapType.Output };
            var custom = new CurveArgs { Mode = CurveMode.Lookup, Gain = false, SmoothLut = true, ControlPoints = LookupHandles.SampleCurve(classic, 60, 16) };
            Assert.AreEqual(0, Validation.Validate(custom).Count);
            var a = CurveMath.Create(classic);
            var b = CurveMath.Create(custom);
            foreach (double x in new[] { 1.0, 5, 12, 30, 55 })
            {
                Assert.AreEqual(a.Sensitivity(x), b.Sensitivity(x), 0.02, $"x={x}");
            }
        }
    }
}

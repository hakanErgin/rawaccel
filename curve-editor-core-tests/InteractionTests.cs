using System.Linq;
using CurveEditor.Core.Handles;
using CurveEditor.Core.Interaction;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CurveEditor.Core.Tests
{
    [TestClass]
    public class InteractionTests
    {
        private static ViewState MakeView(bool logX)
        {
            var v = new ViewState { Left = 50, Top = 10, Width = 800, Height = 400 };
            v.SetLogX(logX);
            v.SetWindow(logX ? 0.1 : 0, 100, 0.5, 2.5);
            return v;
        }

        [DataTestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public void ScreenDataRoundTrip(bool logX)
        {
            var v = MakeView(logX);
            foreach (var p in new[] { new Point2(1, 1), new Point2(37.5, 1.23), new Point2(99, 2.4) })
            {
                var back = v.ToData(v.ToScreen(p));
                Assert.AreEqual(p.X, back.X, 1e-9 * p.X);
                Assert.AreEqual(p.Y, back.Y, 1e-12);
            }
        }

        [DataTestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public void ZoomKeepsPointUnderCursor(bool logX)
        {
            var v = MakeView(logX);
            var cursor = new Point2(300, 150);
            var before = v.ToData(cursor);
            v.ZoomAt(cursor, 0.5, 0.25);
            var after = v.ToData(cursor);
            Assert.AreEqual(before.X, after.X, 1e-9 * before.X);
            Assert.AreEqual(before.Y, after.Y, 1e-9);
            Assert.AreEqual(2.0 * 0.25, v.MaxY - v.MinY, 1e-9);
        }

        [TestMethod]
        public void DeepZoomSupportsTinySpans()
        {
            var v = MakeView(false);
            for (int i = 0; i < 30; i++) v.ZoomAt(new Point2(450, 210), 0.5, 0.5);
            Assert.IsTrue(v.MaxY - v.MinY < 1e-7);
            Assert.IsTrue(v.YTicks().Count() >= 3, "grid keeps subdividing");
        }

        [TestMethod]
        public void PanMovesContentWithMouse()
        {
            var v = MakeView(false);
            v.SetWindow(20, 60, 0.5, 2.5);
            var p = new Point2(40, 1.5);
            var s0 = v.ToScreen(p);
            v.Pan(30, -20);
            var s1 = v.ToScreen(p);
            Assert.AreEqual(s0.X + 30, s1.X, 1e-9);
            Assert.AreEqual(s0.Y - 20, s1.Y, 1e-9);
        }

        [TestMethod]
        public void FineDragScalesMovement()
        {
            var v = MakeView(false);
            var h = new Point2(50, 1.5);
            var m0 = new Point2(100, 100);
            var m1 = new Point2(180, 60);

            var full = HandleInteraction.DragTarget(v, h, m0, m1, 1, false, HandleConstraint.Free);
            var fine = HandleInteraction.DragTarget(v, h, m0, m1, HandleInteraction.DragFactor(shift: true, ctrl: false), false, HandleConstraint.Free);
            var finer = HandleInteraction.DragTarget(v, h, m0, m1, HandleInteraction.DragFactor(shift: true, ctrl: true), false, HandleConstraint.Free);

            Assert.AreEqual((full.X - h.X) / 10, fine.X - h.X, 1e-9);
            Assert.AreEqual((full.Y - h.Y) / 10, fine.Y - h.Y, 1e-9);
            Assert.AreEqual((full.Y - h.Y) / 100, finer.Y - h.Y, 1e-9);
        }

        [TestMethod]
        public void DragRespectsConstraintsAndAxisLock()
        {
            var v = MakeView(false);
            var h = new Point2(50, 1.5);
            var m0 = new Point2(100, 100);
            var m1 = new Point2(180, 60);

            Assert.AreEqual(h.Y, HandleInteraction.DragTarget(v, h, m0, m1, 1, false, HandleConstraint.XOnly).Y);
            Assert.AreEqual(h.X, HandleInteraction.DragTarget(v, h, m0, m1, 1, false, HandleConstraint.YOnly).X);

            var locked = HandleInteraction.DragTarget(v, h, m0, m1, 1, true, HandleConstraint.Free);
            Assert.AreEqual(h.Y, locked.Y, "horizontal movement dominates");
            Assert.AreNotEqual(h.X, locked.X);
        }

        [TestMethod]
        public void NudgeMovesByFractionOfView()
        {
            var v = MakeView(false);
            var h = new Point2(50, 1.5);

            var up = HandleInteraction.NudgeTarget(v, h, 0, 1, 1, HandleConstraint.Free);
            Assert.AreEqual(h.Y + 2.0 * HandleInteraction.NudgeFraction, up.Y, 1e-12);
            Assert.AreEqual(h.X, up.X);

            var fineRight = HandleInteraction.NudgeTarget(v, h, 1, 0, HandleInteraction.NudgeFactor(shift: true, ctrl: false), HandleConstraint.Free);
            Assert.AreEqual(h.X + 100 * HandleInteraction.NudgeFraction * 0.1, fineRight.X, 1e-9);

            var step = HandleInteraction.NudgeStep(v, h, 1);
            Assert.AreEqual(100 * HandleInteraction.NudgeFraction, step.X, 1e-9);
            Assert.AreEqual(2.0 * HandleInteraction.NudgeFraction, step.Y, 1e-12);
        }

        [TestMethod]
        public void NudgeStepShrinksWhenZoomedIn()
        {
            var v = MakeView(false);
            var h = new Point2(50, 1.5);
            double before = HandleInteraction.NudgeStep(v, h, 1).Y;
            v.ZoomAt(v.ToScreen(h), 0.01, 0.01);
            double after = HandleInteraction.NudgeStep(v, h, 1).Y;
            Assert.AreEqual(before / 100, after, 1e-12);
        }

        [TestMethod]
        public void FitYFramesCurve()
        {
            var v = MakeView(false);
            v.FitY(x => 1 + x / 100);
            Assert.IsTrue(v.MinY < 1 && v.MinY > 0.8);
            Assert.IsTrue(v.MaxY > 2 && v.MaxY < 2.3);
        }

        [TestMethod]
        public void UndoRedo()
        {
            var u = new UndoStack<int>();
            u.Push(1);
            u.Push(2);
            Assert.IsTrue(u.TryUndo(3, out int prev));
            Assert.AreEqual(2, prev);
            Assert.IsTrue(u.TryRedo(2, out int next));
            Assert.AreEqual(3, next);
            u.Push(3);
            Assert.IsFalse(u.CanRedo);
        }

        [TestMethod]
        public void LinearAxisLeavesRoomLeftOfZero()
        {
            var v = MakeView(false);
            v.SetWindow(-100, 100, 0.5, 2.5);
            Assert.AreEqual(-ViewState.LeftMargin * 100, v.MinX, 1e-12, "clamped to the margin, not to 0");

            v.SetWindow(-2, 100, 0.5, 2.5);
            Assert.AreEqual(-2, v.MinX, 1e-12, "small negative start allowed");

            // a handle at x = 0.00025 is inside the plot, not on its edge
            Assert.IsTrue(v.XToScreen(0.00025) > v.Left + 10);

            Assert.IsTrue(v.XTicks().All(t => t >= 0), "no negative speed labels");
        }

        [TestMethod]
        public void PanStopsAtLeftMarginKeepingSpan()
        {
            var v = MakeView(false);
            v.SetWindow(0, 100, 0.5, 2.5);
            v.Pan(10000, 0);
            Assert.AreEqual(100, v.MaxX - v.MinX, 1e-9);
            Assert.AreEqual(-ViewState.LeftMargin * v.MaxX, v.MinX, 1e-9);
        }

        [DataTestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public void ZoomAroundCentresAndShrinks(bool logX)
        {
            var v = MakeView(logX);
            var p = new Point2(40, 1.7);
            double spanY = v.MaxY - v.MinY;
            v.ZoomAround(p, 0.1);

            var s = v.ToScreen(p);
            Assert.AreEqual(v.Left + v.Width / 2, s.X, 1e-6);
            Assert.AreEqual(v.Top + v.Height / 2, s.Y, 1e-6);
            Assert.AreEqual(spanY * 0.1, v.MaxY - v.MinY, 1e-9);
        }

        [TestMethod]
        public void ZoomAroundTinyOffsetKeepsItInView()
        {
            var v = MakeView(false);
            v.SetWindow(-12, 300, 0.8, 3.4);
            var offset = new Point2(0.00025, 1);
            double before = HandleInteraction.NudgeStep(v, offset, 1).X;

            v.ZoomAround(offset, 0.1);
            v.ZoomAround(offset, 0.1);

            Assert.IsTrue(v.MinX < offset.X && offset.X < v.MaxX);
            Assert.IsTrue(v.XToScreen(offset.X) > v.Left + 10, "handle not pinned to the edge");
            Assert.AreEqual(before / 100, HandleInteraction.NudgeStep(v, offset, 1).X, before * 1e-6);
        }

        [TestMethod]
        public void ShiftCtrlNudgeIsHundredTimesFiner()
        {
            Assert.AreEqual(0.01, HandleInteraction.NudgeFactor(shift: true, ctrl: true), 1e-15);
            Assert.AreEqual(0.1, HandleInteraction.NudgeFactor(shift: true, ctrl: false), 1e-15);
            Assert.AreEqual(10, HandleInteraction.NudgeFactor(shift: false, ctrl: true), 1e-15);
        }

        [TestMethod]
        public void ValueStepFollowsTheValueNotTheZoom()
        {
            Assert.AreEqual(1e-6, HandleInteraction.ValueStep(0.00025, 300), 1e-18);
            Assert.AreEqual(0.1, HandleInteraction.ValueStep(12.3, 300), 1e-12);
            Assert.AreEqual(0.3, HandleInteraction.ValueStep(0, 300), 1e-12, "zero falls back to the view span");

            var v = MakeView(false);
            v.SetWindow(-12, 300, 0.8, 3.4);
            var offset = new Point2(0.00025, 1);

            var up = HandleInteraction.ValueStepTarget(v, offset, Handles.HandleConstraint.XOnly, 1, 1, horizontal: false);
            Assert.AreEqual(0.000251, up.X, 1e-12);
            Assert.AreEqual(1, up.Y);

            var fine = HandleInteraction.ValueStepTarget(v, offset, Handles.HandleConstraint.XOnly, -1, 0.1, horizontal: false);
            Assert.AreEqual(0.0002499, fine.X, 1e-12);

            var cap = new Point2(200, 3.25);
            var capUp = HandleInteraction.ValueStepTarget(v, cap, Handles.HandleConstraint.Free, 1, 1, horizontal: false);
            Assert.AreEqual(3.26, capUp.Y, 1e-12);
            Assert.AreEqual(200, capUp.X);
            var capRight = HandleInteraction.ValueStepTarget(v, cap, Handles.HandleConstraint.Free, 1, 1, horizontal: true);
            Assert.AreEqual(201, capRight.X, 1e-9);
        }

        [TestMethod]
        public void PeakTrackerKeepsMaximumOfLastFiveSeconds()
        {
            var p = new PeakTracker(5);
            Assert.IsNull(p.Peak);

            p.Add(0, 10);
            p.Add(1, 40);
            p.Add(2, 20);
            Assert.AreEqual(40, p.Peak);

            p.Add(5.5, 15);
            Assert.AreEqual(40, p.Peak, "40 at t=1 is still inside the window");

            p.Add(6.5, 12);
            Assert.AreEqual(20, p.Peak, "40 aged out; 20 at t=2 is now the max");

            p.Update(30);
            Assert.AreEqual(20, p.Peak, "no new movement: the last peak stays readable");

            p.Add(31, 8);
            Assert.AreEqual(8, p.Peak, "new movement starts a fresh window");
        }
    }
}

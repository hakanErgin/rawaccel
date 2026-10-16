using System;
using CurveEditor.Core.Handles;

namespace CurveEditor.Core.Interaction
{
    /// <summary> Precision helpers for moving handles: scaled relative drags and keyboard nudges. </summary>
    public static class HandleInteraction
    {
        /// <summary> Fraction of the plot size moved per arrow-key press at the default precision. </summary>
        public const double NudgeFraction = 1.0 / 200;

        /// <summary> Mouse-movement scale: Shift = 1/10, Shift+Ctrl = 1/100. </summary>
        public static double DragFactor(bool shift, bool ctrl) => shift ? (ctrl ? 0.01 : 0.1) : 1;

        /// <summary> Nudge scale: Shift = 10x finer, Ctrl = 10x coarser. </summary>
        public static double NudgeFactor(bool shift, bool ctrl) => shift ? (ctrl ? 0.01 : 0.1) : (ctrl ? 10 : 1);

        /// <summary>
        /// Step for adjusting a value by its own size: one unit in the third significant digit
        /// (0.00025 steps by 0.000001, 12.3 by 0.1). Near zero, falls back to 1/1000 of the visible span.
        /// </summary>
        public static double ValueStep(double value, double visibleSpan)
        {
            double mag = Math.Abs(value);
            if (mag < 1e-12) return Math.Max(visibleSpan, 1e-9) * 1e-3;
            return Math.Pow(10, Math.Floor(Math.Log10(mag)) - 2);
        }

        /// <summary>
        /// Target for Alt+wheel: moves the handle's main coordinate (x for XOnly handles, otherwise y,
        /// or x when <paramref name="horizontal"/> is set on a free handle) by whole value steps.
        /// </summary>
        public static Point2 ValueStepTarget(ViewState view, Point2 handle, HandleConstraint constraint, int direction, double factor, bool horizontal)
        {
            bool useX = constraint == HandleConstraint.XOnly || (constraint == HandleConstraint.Free && horizontal);
            if (useX)
            {
                double step = ValueStep(handle.X, view.MaxX - view.MinX) * factor;
                return new Point2(Math.Round(handle.X / step + direction) * step, handle.Y);
            }
            else
            {
                double step = ValueStep(handle.Y, view.MaxY - view.MinY) * factor;
                return new Point2(handle.X, Math.Round(handle.Y / step + direction) * step);
            }
        }

        /// <summary>
        /// Target position for a relative drag: the handle moves by the mouse delta (in screen space)
        /// times <paramref name="factor"/>, so grabbing a handle off-centre never makes it jump.
        /// </summary>
        public static Point2 DragTarget(
            ViewState view,
            Point2 handleStart,
            Point2 mouseStart,
            Point2 mouseNow,
            double factor,
            bool axisLock,
            HandleConstraint constraint)
        {
            double dx = (mouseNow.X - mouseStart.X) * factor;
            double dy = (mouseNow.Y - mouseStart.Y) * factor;

            if (axisLock)
            {
                if (Math.Abs(dx) >= Math.Abs(dy)) dy = 0;
                else dx = 0;
            }

            if (constraint == HandleConstraint.XOnly) dy = 0;
            if (constraint == HandleConstraint.YOnly) dx = 0;

            var s = view.ToScreen(handleStart);
            var target = view.ToData(new Point2(s.X + dx, s.Y + dy));

            // keep the unconstrained coordinate exact (avoids round-off drift through the transform)
            if (dx == 0) target = new Point2(handleStart.X, target.Y);
            if (dy == 0) target = new Point2(target.X, handleStart.Y);
            return target;
        }

        /// <summary> Target after one arrow-key press; <paramref name="dirX"/>/<paramref name="dirY"/> are -1, 0 or 1 (dirY = 1 is up). </summary>
        public static Point2 NudgeTarget(ViewState view, Point2 handle, int dirX, int dirY, double factor, HandleConstraint constraint)
        {
            if (constraint == HandleConstraint.XOnly) dirY = 0;
            if (constraint == HandleConstraint.YOnly) dirX = 0;

            double dx = dirX * view.Width * NudgeFraction * factor;
            double dy = -dirY * view.Height * NudgeFraction * factor;

            var s = view.ToScreen(handle);
            var t = view.ToData(new Point2(s.X + dx, s.Y + dy));
            return new Point2(dirX == 0 ? handle.X : t.X, dirY == 0 ? handle.Y : t.Y);
        }

        /// <summary> Data-space size of one nudge at the handle (for the status bar). </summary>
        public static Point2 NudgeStep(ViewState view, Point2 handle, double factor)
        {
            var right = NudgeTarget(view, handle, 1, 0, factor, HandleConstraint.Free);
            var up = NudgeTarget(view, handle, 0, 1, factor, HandleConstraint.Free);
            return new Point2(right.X - handle.X, up.Y - handle.Y);
        }
    }
}

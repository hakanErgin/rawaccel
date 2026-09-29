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
        public static double NudgeFactor(bool shift, bool ctrl) => shift ? 0.1 : (ctrl ? 10 : 1);

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

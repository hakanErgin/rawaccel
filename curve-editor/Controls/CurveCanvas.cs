using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using CurveEditor.Core;
using CurveEditor.Core.Curves;
using CurveEditor.Core.Handles;
using CurveEditor.Core.Interaction;

namespace CurveEditor.Controls
{
    public enum ChartKind
    {
        Sensitivity,
        Velocity,
        Gain,
    }

    public sealed class HandleEventArgs : EventArgs
    {
        public HandleEventArgs(string id) => Id = id;

        public string Id { get; }
    }

    public sealed class HandleMovedEventArgs : EventArgs
    {
        public HandleMovedEventArgs(string id, Point2 target, bool isNudge)
        {
            Id = id;
            Target = target;
            IsNudge = isNudge;
        }

        public string Id { get; }

        public Point2 Target { get; }

        public bool IsNudge { get; }
    }

    public sealed class ChartPointEventArgs : EventArgs
    {
        public ChartPointEventArgs(Point2 point) => Point = point;

        public Point2 Point { get; }
    }

    /// <summary>
    /// Interactive chart: draws the curve, the last-applied ("ghost") curve and the handles, and turns
    /// mouse and keyboard input into handle moves. Precision features:
    ///   wheel = zoom at cursor (Shift: x only, Ctrl: y only), right-drag = box zoom,
    ///   left/middle-drag on empty space = pan, F = fit, Home = reset view;
    ///   dragging is relative, Shift = 1/10 speed, Shift+Ctrl = 1/100, Alt = lock to one axis;
    ///   arrows nudge the selected handle (Shift finer, Ctrl coarser), Tab cycles handles.
    /// </summary>
    public sealed class CurveCanvas : FrameworkElement
    {
        private const double MarginLeft = 64;
        private const double MarginRight = 18;
        private const double MarginTop = 14;
        private const double MarginBottom = 42;
        private const double HandleRadius = 6;
        private const double HitRadius = 11;

        private static readonly Brush BackgroundBrush = Frozen(new SolidColorBrush(Color.FromRgb(0x1B, 0x1D, 0x21)));
        private static readonly Brush PlotBrush = Frozen(new SolidColorBrush(Color.FromRgb(0x22, 0x25, 0x2A)));
        private static readonly Brush TextBrush = Frozen(new SolidColorBrush(Color.FromRgb(0xC9, 0xCD, 0xD4)));
        private static readonly Brush DimTextBrush = Frozen(new SolidColorBrush(Color.FromRgb(0x8A, 0x90, 0x99)));
        private static readonly Brush HandleBrush = Frozen(new SolidColorBrush(Color.FromRgb(0xFF, 0xB7, 0x4D)));
        private static readonly Brush SelectedBrush = Frozen(new SolidColorBrush(Color.FromRgb(0xFF, 0x70, 0x43)));
        private static readonly Brush LiveBrush = Frozen(new SolidColorBrush(Color.FromRgb(0x81, 0xC7, 0x84)));
        private static readonly Brush ReadoutBrush = Frozen(new SolidColorBrush(Color.FromArgb(0xD0, 0x1B, 0x1D, 0x21)));
        private static readonly Brush BoxBrush = Frozen(new SolidColorBrush(Color.FromArgb(0x30, 0x4F, 0xC3, 0xF7)));
        private static readonly Pen GridPen = Frozen(new Pen(new SolidColorBrush(Color.FromRgb(0x30, 0x34, 0x3A)), 1));
        private static readonly Pen BaselinePen = Frozen(new Pen(new SolidColorBrush(Color.FromRgb(0x5A, 0x60, 0x6A)), 1));
        private static readonly Pen CurvePen = Frozen(new Pen(new SolidColorBrush(Color.FromRgb(0x4F, 0xC3, 0xF7)), 2.2) { LineJoin = PenLineJoin.Round });
        private static readonly Pen GhostPen = Frozen(new Pen(new SolidColorBrush(Color.FromRgb(0x9A, 0xA0, 0xA6)), 1.4) { DashStyle = new DashStyle(new double[] { 4, 3 }, 0) });
        private static readonly Pen LevelPen = Frozen(new Pen(new SolidColorBrush(Color.FromArgb(0x90, 0xFF, 0xB7, 0x4D)), 1) { DashStyle = new DashStyle(new double[] { 6, 4 }, 0) });
        private static readonly Pen HandleOutline = Frozen(new Pen(new SolidColorBrush(Color.FromRgb(0x1B, 0x1D, 0x21)), 1.5));
        private static readonly Pen SelectedRing = Frozen(new Pen(new SolidColorBrush(Color.FromRgb(0xFF, 0x70, 0x43)), 2));
        private static readonly Pen CrosshairPen = Frozen(new Pen(new SolidColorBrush(Color.FromArgb(0x50, 0xC9, 0xCD, 0xD4)), 1));
        private static readonly Pen BoxPen = Frozen(new Pen(new SolidColorBrush(Color.FromRgb(0x4F, 0xC3, 0xF7)), 1));

        private static readonly Typeface Face = new Typeface("Segoe UI");

        private IReadOnlyList<Handle> handles = Array.Empty<Handle>();

        private enum DragMode { None, Handle, Pan, Box }

        private DragMode drag;
        private string dragId;
        private Point2 dragStartData;
        private Point dragStartMouse;
        private double dragFactor;
        private bool dragAxisLock;
        private Point lastMouse;
        private Point downPoint;
        private Point boxStart;
        private string hoverId;
        private Point? cursor;
        private bool hasMoved;

        public CurveCanvas()
        {
            Focusable = true;
            FocusVisualStyle = null;
            ClipToBounds = true;
            View.Changed += (s, e) =>
            {
                InvalidateVisual();
                ViewChanged?.Invoke(this, EventArgs.Empty);
            };
        }

        public ViewState View { get; } = new ViewState();

        public ICurve Curve { get; set; }

        public ICurve Ghost { get; set; }

        public ChartKind Kind { get; set; } = ChartKind.Sensitivity;

        /// <summary> Handles can only be edited in the sensitivity view. </summary>
        public bool HandlesVisible => Kind == ChartKind.Sensitivity;

        public IReadOnlyList<Handle> Handles
        {
            get => handles;
            set => handles = value ?? Array.Empty<Handle>();
        }

        public string SelectedHandleId { get; set; }

        /// <summary> Input speed of the latest mouse movement, and when it happened. </summary>
        public double? LiveSpeed { get; set; }

        public DateTime LiveTime { get; set; }

        public string XAxisLabel { get; set; } = "Input speed (counts/ms at 1000 DPI)";

        public event EventHandler<HandleEventArgs> HandleSelected;
        public event EventHandler<HandleEventArgs> HandleDragStarted;
        public event EventHandler<HandleMovedEventArgs> HandleMoved;
        public event EventHandler<HandleEventArgs> HandleDragCompleted;
        public event EventHandler<HandleEventArgs> RemoveRequested;
        public event EventHandler<ChartPointEventArgs> AddPointRequested;
        public event EventHandler<ChartPointEventArgs> CursorMoved;
        public event EventHandler FitRequested;
        public event EventHandler ResetViewRequested;
        public event EventHandler ViewChanged;

        public string YAxisLabel =>
            Kind == ChartKind.Sensitivity ? "Sensitivity (curve multiplier)" :
            Kind == ChartKind.Velocity ? "Output speed (counts/ms)" : "Gain (d output / d input)";

        public double Value(ICurve c, double x)
        {
            if (c == null) return double.NaN;
            switch (Kind)
            {
                case ChartKind.Velocity: return c.Velocity(x);
                case ChartKind.Gain: return c.Gain(x);
                default: return c.Sensitivity(x);
            }
        }

        private static T Frozen<T>(T f) where T : Freezable
        {
            f.Freeze();
            return f;
        }

        protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
        {
            base.OnRenderSizeChanged(sizeInfo);
            View.Left = MarginLeft;
            View.Top = MarginTop;
            View.Width = Math.Max(10, ActualWidth - MarginLeft - MarginRight);
            View.Height = Math.Max(10, ActualHeight - MarginTop - MarginBottom);
            InvalidateVisual();
        }

        private Rect PlotRect => new Rect(View.Left, View.Top, View.Width, View.Height);

        private double Dpi => VisualTreeHelper.GetDpi(this).PixelsPerDip;

        private FormattedText Text(string s, double size, Brush brush) =>
            new FormattedText(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Face, size, brush, Dpi);

        /// <summary> Screen position of a handle, pinned inside the plot horizontally (e.g. x = 0 on a log axis). </summary>
        private Point HandleScreen(Handle h)
        {
            var s = View.ToScreen(h.Position);
            double x = double.IsNaN(s.X) ? View.Left : Math.Max(View.Left, Math.Min(View.Left + View.Width, s.X));
            return new Point(x, s.Y);
        }

        protected override void OnRender(DrawingContext dc)
        {
            var bounds = new Rect(0, 0, ActualWidth, ActualHeight);
            dc.DrawRectangle(BackgroundBrush, null, bounds);

            var plot = PlotRect;
            dc.DrawRectangle(PlotBrush, null, plot);

            DrawGrid(dc, plot);

            dc.PushClip(new RectangleGeometry(plot));

            DrawCurve(dc, Ghost, GhostPen);
            DrawCurve(dc, Curve, CurvePen);

            if (HandlesVisible) DrawHandles(dc, plot);

            DrawLive(dc);

            if (cursor is Point c && plot.Contains(c) && drag != DragMode.Pan)
            {
                dc.DrawLine(CrosshairPen, new Point(c.X, plot.Top), new Point(c.X, plot.Bottom));
            }

            if (drag == DragMode.Box)
            {
                dc.DrawRectangle(BoxBrush, BoxPen, new Rect(boxStart, lastMouse));
            }

            dc.Pop();

            DrawReadout(dc, plot);
        }

        private static string FormatTick(double v, double step)
        {
            int decimals = Math.Max(0, (int)Math.Ceiling(-Math.Log10(step) - 1e-9));
            if (decimals > 12) return v.ToString("G6", CultureInfo.InvariantCulture);
            return v.ToString("F" + decimals, CultureInfo.InvariantCulture);
        }

        private void DrawGrid(DrawingContext dc, Rect plot)
        {
            double xStep = ViewState.NiceStep(View.MaxX - View.MinX, 10);
            foreach (double x in View.XTicks(10))
            {
                double sx = View.XToScreen(x);
                dc.DrawLine(GridPen, new Point(sx, plot.Top), new Point(sx, plot.Bottom));
                var t = Text(View.LogX ? x.ToString("G4", CultureInfo.InvariantCulture) : FormatTick(x, xStep), 11, DimTextBrush);
                dc.DrawText(t, new Point(sx - t.Width / 2, plot.Bottom + 4));
            }

            double yStep = ViewState.NiceStep(View.MaxY - View.MinY, 8);
            foreach (double y in View.YTicks(8))
            {
                double sy = View.YToScreen(y);
                dc.DrawLine(y == 1 && Kind == ChartKind.Sensitivity ? BaselinePen : GridPen, new Point(plot.Left, sy), new Point(plot.Right, sy));
                var t = Text(FormatTick(y, yStep), 11, DimTextBrush);
                dc.DrawText(t, new Point(plot.Left - t.Width - 6, sy - t.Height / 2));
            }

            if (Kind == ChartKind.Sensitivity && 1 > View.MinY && 1 < View.MaxY)
            {
                double sy = View.YToScreen(1);
                dc.DrawLine(BaselinePen, new Point(plot.Left, sy), new Point(plot.Right, sy));
            }

            var xl = Text(XAxisLabel + (View.LogX ? "  (log scale)" : ""), 12, TextBrush);
            dc.DrawText(xl, new Point(plot.Left + (plot.Width - xl.Width) / 2, plot.Bottom + 20));

            var yl = Text(YAxisLabel, 12, TextBrush);
            dc.PushTransform(new RotateTransform(-90, 0, 0));
            dc.DrawText(yl, new Point(-(plot.Top + (plot.Height + yl.Width) / 2), 4));
            dc.Pop();
        }

        private void DrawCurve(DrawingContext dc, ICurve curve, Pen pen)
        {
            if (curve == null) return;

            var geo = new StreamGeometry();
            using (var ctx = geo.Open())
            {
                bool open = false;
                int steps = (int)Math.Ceiling(View.Width);
                for (int i = 0; i <= steps; i++)
                {
                    double sx = View.Left + i;
                    double x = View.ScreenToX(sx);
                    if (x <= 0) x = 1e-9;
                    double y = Value(curve, x);

                    if (double.IsNaN(y) || double.IsInfinity(y))
                    {
                        open = false;
                        continue;
                    }

                    double sy = Math.Max(-1e5, Math.Min(1e5, View.YToScreen(y)));
                    var p = new Point(sx, sy);
                    if (!open)
                    {
                        ctx.BeginFigure(p, false, false);
                        open = true;
                    }
                    else
                    {
                        ctx.LineTo(p, true, false);
                    }
                }
            }
            geo.Freeze();
            dc.DrawGeometry(null, pen, geo);
        }

        private void DrawHandles(DrawingContext dc, Rect plot)
        {
            foreach (var h in handles.Where(h => h.Kind == HandleKind.Level))
            {
                double sy = View.YToScreen(h.Position.Y);
                dc.DrawLine(LevelPen, new Point(plot.Left, sy), new Point(plot.Right, sy));
            }

            bool manyPoints = handles.Count > 12;

            foreach (var h in handles)
            {
                var p = HandleScreen(h);
                bool selected = h.Id == SelectedHandleId;
                bool hover = h.Id == hoverId;
                var fill = selected ? SelectedBrush : HandleBrush;
                double r = HandleRadius + (hover || selected ? 1.5 : 0);

                if (h.Kind == HandleKind.Level)
                {
                    var diamond = new StreamGeometry();
                    using (var ctx = diamond.Open())
                    {
                        ctx.BeginFigure(new Point(p.X, p.Y - r - 1), true, true);
                        ctx.LineTo(new Point(p.X + r + 1, p.Y), true, false);
                        ctx.LineTo(new Point(p.X, p.Y + r + 1), true, false);
                        ctx.LineTo(new Point(p.X - r - 1, p.Y), true, false);
                    }
                    diamond.Freeze();
                    dc.DrawGeometry(fill, HandleOutline, diamond);
                }
                else
                {
                    dc.DrawEllipse(fill, HandleOutline, p, r, r);
                }

                if (selected)
                {
                    dc.DrawEllipse(null, SelectedRing, p, r + 4, r + 4);
                }

                if (!manyPoints || hover || selected)
                {
                    var t = Text(h.Label, 11, TextBrush);
                    dc.DrawText(t, new Point(p.X + 9, p.Y - t.Height - 4));
                }
            }
        }

        private void DrawLive(DrawingContext dc)
        {
            if (!(LiveSpeed is double v) || Ghost == null) return;
            double age = (DateTime.UtcNow - LiveTime).TotalSeconds;
            if (age > 1.5) return;

            double y = Value(Ghost, v);
            if (double.IsNaN(y) || double.IsInfinity(y)) return;
            var p = new Point(View.XToScreen(v), View.YToScreen(y));
            dc.PushOpacity(Math.Max(0, 1 - age / 1.5));
            dc.DrawEllipse(LiveBrush, null, p, 5, 5);
            dc.Pop();
        }

        private void DrawReadout(DrawingContext dc, Rect plot)
        {
            if (!(cursor is Point c) || !plot.Contains(c)) return;

            double x = View.ScreenToX(c.X);
            double y = Value(Curve, x);
            double g = Value(Ghost, x);

            string s = string.Format(CultureInfo.InvariantCulture, "speed {0:G6}   value {1:G6}", x, y);
            if (Ghost != null && !double.IsNaN(g))
            {
                double d = y - g;
                s += string.Format(CultureInfo.InvariantCulture, "   applied {0:G6}   Δ {1}{2:G4}", g, d >= 0 ? "+" : "", d);
            }

            var t = Text(s, 12, TextBrush);
            var box = new Rect(plot.Left + 8, plot.Top + 8, t.Width + 12, t.Height + 6);
            dc.DrawRoundedRectangle(ReadoutBrush, null, box, 4, 4);
            dc.DrawText(t, new Point(box.Left + 6, box.Top + 3));

            if (!double.IsNaN(y) && !double.IsInfinity(y))
            {
                var p = new Point(c.X, View.YToScreen(y));
                if (plot.Contains(p)) dc.DrawEllipse(null, CurvePen, p, 3, 3);
            }
        }

        private Handle HitTest(Point p)
        {
            if (!HandlesVisible) return null;

            Handle best = null;
            double bestDist = HitRadius;
            foreach (var h in handles)
            {
                var s = HandleScreen(h);
                double d = Math.Sqrt((s.X - p.X) * (s.X - p.X) + (s.Y - p.Y) * (s.Y - p.Y));
                if (d <= bestDist)
                {
                    best = h;
                    bestDist = d;
                }
            }
            if (best != null) return best;

            // level handles can be grabbed anywhere along their guide line
            foreach (var h in handles.Where(h => h.Kind == HandleKind.Level))
            {
                if (Math.Abs(View.YToScreen(h.Position.Y) - p.Y) <= 4) return h;
            }
            return null;
        }

        private Handle Find(string id) => id == null ? null : handles.FirstOrDefault(h => h.Id == id);

        private void Select(string id)
        {
            if (SelectedHandleId == id) return;
            SelectedHandleId = id;
            HandleSelected?.Invoke(this, new HandleEventArgs(id));
            InvalidateVisual();
        }

        private static Point2 P(Point p) => new Point2(p.X, p.Y);

        protected override void OnMouseDown(MouseButtonEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();
            var p = e.GetPosition(this);
            lastMouse = p;
            downPoint = p;
            hasMoved = false;

            if (e.ChangedButton == MouseButton.Left)
            {
                var hit = HitTest(p);

                if (e.ClickCount == 2 && hit == null)
                {
                    AddPointRequested?.Invoke(this, new ChartPointEventArgs(View.ToData(P(p))));
                    e.Handled = true;
                    return;
                }

                if (hit != null)
                {
                    Select(hit.Id);
                    drag = DragMode.Handle;
                    dragId = hit.Id;
                    dragStartData = hit.Position;
                    dragStartMouse = p;
                    dragFactor = CurrentDragFactor();
                    dragAxisLock = AxisLock();
                    HandleDragStarted?.Invoke(this, new HandleEventArgs(hit.Id));
                }
                else
                {
                    drag = DragMode.Pan;
                }
            }
            else if (e.ChangedButton == MouseButton.Middle)
            {
                drag = DragMode.Pan;
            }
            else if (e.ChangedButton == MouseButton.Right)
            {
                drag = DragMode.Box;
                boxStart = p;
            }

            if (drag != DragMode.None)
            {
                CaptureMouse();
                e.Handled = true;
            }
        }

        private static double CurrentDragFactor() =>
            HandleInteraction.DragFactor(
                (Keyboard.Modifiers & ModifierKeys.Shift) != 0,
                (Keyboard.Modifiers & ModifierKeys.Control) != 0);

        private static bool AxisLock() => (Keyboard.Modifiers & ModifierKeys.Alt) != 0;

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            var p = e.GetPosition(this);
            cursor = p;
            if (Math.Abs(p.X - downPoint.X) + Math.Abs(p.Y - downPoint.Y) > 3) hasMoved = true;

            switch (drag)
            {
                case DragMode.Handle:
                {
                    var h = Find(dragId);
                    if (h == null) break;

                    double factor = CurrentDragFactor();
                    bool axisLock = AxisLock();
                    if (factor != dragFactor || axisLock != dragAxisLock)
                    {
                        // precision changed mid-drag: continue from where the handle is now
                        dragStartData = h.Position;
                        dragStartMouse = p;
                        dragFactor = factor;
                        dragAxisLock = axisLock;
                    }

                    var target = HandleInteraction.DragTarget(View, dragStartData, P(dragStartMouse), P(p), factor, axisLock, h.Constraint);
                    HandleMoved?.Invoke(this, new HandleMovedEventArgs(dragId, target, false));
                    break;
                }
                case DragMode.Pan:
                    View.Pan(p.X - lastMouse.X, p.Y - lastMouse.Y);
                    break;
                case DragMode.Box:
                    InvalidateVisual();
                    break;
                default:
                {
                    var hit = HitTest(p)?.Id;
                    if (hit != hoverId)
                    {
                        hoverId = hit;
                        Cursor = hit != null ? Cursors.Hand : null;
                    }
                    InvalidateVisual();
                    break;
                }
            }

            lastMouse = p;

            CursorMoved?.Invoke(this, new ChartPointEventArgs(View.ToData(P(p))));
        }

        protected override void OnMouseUp(MouseButtonEventArgs e)
        {
            base.OnMouseUp(e);
            var p = e.GetPosition(this);

            switch (drag)
            {
                case DragMode.Handle when e.ChangedButton == MouseButton.Left:
                    HandleDragCompleted?.Invoke(this, new HandleEventArgs(dragId));
                    EndDrag();
                    break;
                case DragMode.Pan when e.ChangedButton == MouseButton.Left || e.ChangedButton == MouseButton.Middle:
                    if (!hasMoved && e.ChangedButton == MouseButton.Left) Select(null);
                    EndDrag();
                    break;
                case DragMode.Box when e.ChangedButton == MouseButton.Right:
                    EndDrag();
                    if (hasMoved)
                    {
                        View.ZoomToScreenRect(P(boxStart), P(p));
                    }
                    else
                    {
                        var hit = HitTest(p);
                        if (hit != null) RemoveRequested?.Invoke(this, new HandleEventArgs(hit.Id));
                    }
                    break;
            }
            e.Handled = true;
        }

        private void EndDrag()
        {
            drag = DragMode.None;
            dragId = null;
            ReleaseMouseCapture();
            InvalidateVisual();
        }

        protected override void OnLostMouseCapture(MouseEventArgs e)
        {
            base.OnLostMouseCapture(e);
            if (drag == DragMode.Handle) HandleDragCompleted?.Invoke(this, new HandleEventArgs(dragId));
            drag = DragMode.None;
            dragId = null;
        }

        protected override void OnMouseLeave(MouseEventArgs e)
        {
            base.OnMouseLeave(e);
            cursor = null;
            hoverId = null;
            InvalidateVisual();
        }

        protected override void OnMouseWheel(MouseWheelEventArgs e)
        {
            base.OnMouseWheel(e);
            if (drag == DragMode.Handle) return;

            double f = e.Delta > 0 ? 0.8 : 1.25;
            bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
            bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
            View.ZoomAt(P(e.GetPosition(this)), ctrl ? 1 : f, shift ? 1 : f);
            e.Handled = true;
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
            bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
            var key = e.Key == Key.System ? e.SystemKey : e.Key;

            int dx = 0, dy = 0;
            switch (key)
            {
                case Key.Left: dx = -1; break;
                case Key.Right: dx = 1; break;
                case Key.Up: dy = 1; break;
                case Key.Down: dy = -1; break;
                case Key.Tab:
                    if (HandlesVisible && handles.Count > 0)
                    {
                        int i = handles.ToList().FindIndex(h => h.Id == SelectedHandleId);
                        i = shift ? (i <= 0 ? handles.Count - 1 : i - 1) : (i + 1) % handles.Count;
                        Select(handles[i].Id);
                        e.Handled = true;
                    }
                    return;
                case Key.Delete:
                case Key.Back:
                    if (SelectedHandleId != null) RemoveRequested?.Invoke(this, new HandleEventArgs(SelectedHandleId));
                    e.Handled = true;
                    return;
                case Key.Escape:
                    Select(null);
                    e.Handled = true;
                    return;
                case Key.F:
                    FitRequested?.Invoke(this, EventArgs.Empty);
                    e.Handled = true;
                    return;
                case Key.Home:
                    ResetViewRequested?.Invoke(this, EventArgs.Empty);
                    e.Handled = true;
                    return;
                case Key.OemPlus:
                case Key.Add:
                    View.ZoomAt(new Point2(View.Left + View.Width / 2, View.Top + View.Height / 2), 0.8, 0.8);
                    e.Handled = true;
                    return;
                case Key.OemMinus:
                case Key.Subtract:
                    View.ZoomAt(new Point2(View.Left + View.Width / 2, View.Top + View.Height / 2), 1.25, 1.25);
                    e.Handled = true;
                    return;
                default:
                    return;
            }

            var sel = Find(SelectedHandleId);
            if (sel == null || !HandlesVisible) return;

            double factor = HandleInteraction.NudgeFactor(shift, ctrl);
            var target = HandleInteraction.NudgeTarget(View, sel.Position, dx, dy, factor, sel.Constraint);
            HandleMoved?.Invoke(this, new HandleMovedEventArgs(sel.Id, target, true));
            e.Handled = true;
        }

        /// <summary> Data-space size of one arrow-key nudge at the selected handle, for the status bar. </summary>
        public Point2? NudgeStep()
        {
            var sel = Find(SelectedHandleId);
            if (sel == null) return null;
            bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
            bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
            return HandleInteraction.NudgeStep(View, sel.Position, HandleInteraction.NudgeFactor(shift, ctrl));
        }
    }
}

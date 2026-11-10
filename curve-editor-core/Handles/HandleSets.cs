using System;
using System.Collections.Generic;
using CurveEditor.Core.Curves;

namespace CurveEditor.Core.Handles
{
    public static class HandleSets
    {
        private static readonly IHandleSet Classic = new GuardedHandleSet(new ClassicHandles());
        private static readonly IHandleSet Power = new GuardedHandleSet(new PowerHandles());
        private static readonly IHandleSet Natural = new GuardedHandleSet(new NaturalHandles());
        private static readonly IHandleSet Jump = new GuardedHandleSet(new JumpHandles());
        private static readonly IHandleSet Synchronous = new GuardedHandleSet(new SynchronousHandles());
        private static readonly IHandleSet Lookup = new GuardedHandleSet(new LookupHandles());
        private static readonly IHandleSet None = new NoHandles();

        public static IHandleSet For(CurveMode mode)
        {
            switch (mode)
            {
                case CurveMode.Classic: return Classic;
                case CurveMode.Power: return Power;
                case CurveMode.Natural: return Natural;
                case CurveMode.Jump: return Jump;
                case CurveMode.Synchronous: return Synchronous;
                case CurveMode.Lookup: return Lookup;
                default: return None;
            }
        }

        /// <summary> Whether the classic/power curve (after normalization) has a cap. </summary>
        public static bool IsCapped(CurveArgs args)
        {
            var n = For(args.Mode).Normalize(args);
            return (n.Mode == CurveMode.Classic || n.Mode == CurveMode.Power) && n.CapY > 0;
        }

        /// <summary> Turns the cap of a classic/power curve on (at the curve's value near the anchor) or off. </summary>
        public static CurveArgs SetCapped(CurveArgs args, bool capped, HandleContext ctx)
        {
            var n = For(args.Mode).Normalize(args);
            if (n.Mode != CurveMode.Classic && n.Mode != CurveMode.Power) return n;
            if (capped == (n.CapY > 0)) return n;

            if (!capped)
            {
                n.CapY = 0;
                return n;
            }

            double x = Math.Max(ctx.AnchorX, n.InputOffset * 1.5 + 1);
            double y = CurveMath.Sensitivity(n, x);
            return For(n.Mode).Drag(n, HandleIds.Cap, new Point2(x, y), ctx);
        }

        internal static double Eval(CurveArgs a, double x) => CurveMath.Sensitivity(a, x);

        public static bool Finite(double v) => !double.IsNaN(v) && !double.IsInfinity(v);

        public static bool IsFinitePositive(double v) => Finite(v) && v > 0;

        private static readonly double[] Probes = BuildProbes();

        private static double[] BuildProbes()
        {
            var xs = new double[33];
            for (int i = 0; i < xs.Length; i++) xs[i] = 0.01 * Math.Pow(10, i * 6.0 / (xs.Length - 1));
            return xs;
        }

        /// <summary> True if the driver would accept the args and the curve is finite everywhere we look. </summary>
        public static bool IsUsable(CurveArgs a)
        {
            double[] values =
            {
                a.InputOffset, a.OutputOffset, a.Acceleration, a.DecayRate, a.Gamma, a.Motivity,
                a.ExponentClassic, a.Scale, a.ExponentPower, a.Limit, a.SyncSpeed, a.Smooth, a.CapX, a.CapY,
            };
            foreach (double v in values)
            {
                if (!Finite(v)) return false;
            }

            if (Validation.Validate(a).Count > 0) return false;

            var curve = CurveMath.Create(a);
            foreach (double x in Probes)
            {
                if (!Finite(curve.Sensitivity(x))) return false;
            }
            return true;
        }

        internal static double Clamp(double v, double lo, double hi) => v < lo ? lo : (v > hi ? hi : v);

        /// <summary> Keeps a level at least <paramref name="gap"/> away from 1 on the side it is already on. </summary>
        internal static double AwayFromOne(double y, double gap = 1e-4)
        {
            if (Math.Abs(y - 1) >= gap) return y;
            return y >= 1 ? 1 + gap : 1 - gap;
        }
    }

    public static class HandleIds
    {
        public const string Offset = "offset";
        public const string Cap = "cap";
        public const string Point = "point";
        public const string Bend = "bend";
        public const string Start = "start";
        public const string Limit = "limit";
        public const string Knee = "knee";
        public const string Jump = "jump";
        public const string Smooth = "smooth";
        public const string Sync = "sync";
        public const string Motivity = "motivity";
        public const string Gamma = "gamma";

        public static string LutPoint(int index) => "p" + index;

        public static bool TryParseLutPoint(string id, out int index)
        {
            index = -1;
            return id != null && id.Length > 1 && id[0] == 'p' && int.TryParse(id.Substring(1), out index);
        }
    }

    /// <summary> Rejects any drag result the driver would refuse or that produces a non-finite curve. </summary>
    internal sealed class GuardedHandleSet : IHandleSet
    {
        private readonly IHandleSet inner;

        public GuardedHandleSet(IHandleSet inner)
        {
            this.inner = inner;
        }

        public CurveArgs Normalize(CurveArgs args) => inner.Normalize(args);

        public IReadOnlyList<Handle> GetHandles(CurveArgs args, HandleContext ctx) => inner.GetHandles(args, ctx);

        public CurveArgs Drag(CurveArgs args, string handleId, Point2 target, HandleContext ctx)
        {
            if (!HandleSets.Finite(target.X) || !HandleSets.Finite(target.Y)) return inner.Normalize(args);

            var after = inner.Drag(args, handleId, target, ctx);
            if (HandleSets.IsUsable(after)) return after;

            var before = inner.Normalize(args);
            return HandleSets.IsUsable(before) ? before : args.Clone();
        }
    }

    internal sealed class NoHandles : IHandleSet
    {
        public CurveArgs Normalize(CurveArgs args) => args.Clone();

        public IReadOnlyList<Handle> GetHandles(CurveArgs args, HandleContext ctx) => Array.Empty<Handle>();

        public CurveArgs Drag(CurveArgs args, string handleId, Point2 target, HandleContext ctx) => args.Clone();
    }

    /// <summary>
    /// Classic: 1 + (a(x - offset))^p / x, optionally capped. Stored in "output" cap mode so the
    /// acceleration field stays meaningful; the cap handle sits where the cap takes effect.
    /// </summary>
    internal sealed class ClassicHandles : IHandleSet
    {
        private const double MinExponent = 1.01;
        private const double MaxExponent = 10;

        public CurveArgs Normalize(CurveArgs args)
        {
            var n = args.Clone();
            double p = args.ExponentClassic;
            double off = args.InputOffset;

            switch (args.CapMode)
            {
                case CapType.InOut:
                    n.Acceleration = KeepPositive(AccelFromCapPoint(args.Gain, args.CapX, Math.Abs(args.CapY - 1), p, off), args.Acceleration);
                    break;
                case CapType.Input:
                    if (args.CapX > 0)
                    {
                        double c = args.Gain
                            ? ClassicGain.GainFn(args.CapX, args.Acceleration, p, off)
                            : BaseFn(args.CapX, args.Acceleration, p, off);
                        n.CapY = 1 + c;
                    }
                    else
                    {
                        n.CapY = 0;
                    }
                    break;
            }

            n.CapMode = CapType.Output;
            return n;
        }

        public IReadOnlyList<Handle> GetHandles(CurveArgs args, HandleContext ctx)
        {
            var a = Normalize(args);
            double off = a.InputOffset;
            var list = new List<Handle>
            {
                new Handle(HandleIds.Offset, "Offset", new Point2(off, 1), HandleConstraint.XOnly,
                    "Input offset: speed where acceleration starts", nameof(CurveArgs.InputOffset)),
            };

            double refX;
            if (a.CapY > 0)
            {
                refX = CapStartX(a);
                list.Add(new Handle(HandleIds.Cap, "Cap", new Point2(refX, a.CapY), HandleConstraint.Free,
                    a.Gain ? "Cap: speed where gain stops growing, and the sensitivity it levels off at"
                           : "Cap: speed where sensitivity stops growing, and its maximum",
                    nameof(CurveArgs.CapY), nameof(CurveArgs.Acceleration)));
            }
            else
            {
                refX = AnchorX(a, ctx);
                list.Add(new Handle(HandleIds.Point, "Accel", new Point2(refX, HandleSets.Eval(a, refX)), HandleConstraint.Free,
                    "Acceleration: drag up or down to steepen the curve, sideways to pick the speed it pins", nameof(CurveArgs.Acceleration)) { SlidesAlongCurve = true });
            }

            double xb = BendX(off, refX, ctx.X(HandleIds.Bend, double.NaN));
            list.Add(new Handle(HandleIds.Bend, "Bend", new Point2(xb, HandleSets.Eval(a, xb)), HandleConstraint.Free,
                "Exponent: drag up or down to change the curvature, sideways to move the bend point", nameof(CurveArgs.ExponentClassic), nameof(CurveArgs.Acceleration)) { SlidesAlongCurve = true });

            return list;
        }

        public CurveArgs Drag(CurveArgs args, string handleId, Point2 target, HandleContext ctx)
        {
            var a = Normalize(args);
            double p = a.ExponentClassic;
            bool capped = a.CapY > 0;

            switch (handleId)
            {
                case HandleIds.Offset:
                {
                    double limit = capped ? CapStartX(a) : AnchorX(a, ctx);
                    double capStart = limit;
                    double newOff = HandleSets.Clamp(target.X, 0, limit * (1 - 1e-3));
                    a.InputOffset = newOff;
                    if (capped)
                    {
                        // keep the cap point where it is
                        a.Acceleration = KeepPositive(AccelFromCapPoint(a.Gain, capStart, Math.Abs(a.CapY - 1), p, newOff), a.Acceleration);
                    }
                    return a;
                }
                case HandleIds.Cap:
                {
                    double x = Math.Max(target.X, a.InputOffset + Math.Max(1e-3, a.InputOffset * 1e-3));
                    double y = HandleSets.AwayFromOne(Math.Max(target.Y, 1e-4));
                    a.CapY = y;
                    a.Acceleration = KeepPositive(AccelFromCapPoint(a.Gain, x, Math.Abs(y - 1), p, a.InputOffset), a.Acceleration);
                    return a;
                }
                case HandleIds.Point:
                {
                    double x = AnchorClamp(a, target.X);
                    double y = Math.Max(target.Y, 1 + 1e-6);
                    a.Acceleration = AccelFromPoint(x, y, p, a.InputOffset);
                    return a;
                }
                case HandleIds.Bend:
                {
                    double refX, refY;
                    if (capped)
                    {
                        refX = CapStartX(a);
                        refY = a.CapY;
                    }
                    else
                    {
                        refX = AnchorX(a, ctx);
                        refY = HandleSets.Eval(a, refX);
                    }

                    double xb = BendX(a.InputOffset, refX, target.X);
                    var trial = a.Clone();

                    double Apply(double exp)
                    {
                        trial.ExponentClassic = exp;
                        trial.Acceleration = capped
                            ? KeepPositive(AccelFromCapPoint(a.Gain, refX, Math.Abs(refY - 1), exp, a.InputOffset), a.Acceleration)
                            : AccelFromPoint(refX, refY, exp, a.InputOffset);
                        return HandleSets.Eval(trial, xb);
                    }

                    double best = Solver.Bisect(Apply, target.Y, MinExponent, MaxExponent);
                    Apply(best);
                    return trial;
                }
                default:
                    return a;
            }
        }

        /// <summary> Bend point: the chosen speed kept strictly between offset and the reference (default halfway). </summary>
        private static double BendX(double off, double refX, double chosen)
        {
            double span = refX - off;
            if (double.IsNaN(chosen)) return off + span / 2;
            return HandleSets.Clamp(chosen, off + span * 0.02, refX - span * 0.02);
        }

        private static double AnchorClamp(CurveArgs a, double x) =>
            Math.Max(x, a.InputOffset + Math.Max(1, a.InputOffset * 0.1));

        private static double AnchorX(CurveArgs a, HandleContext ctx) =>
            AnchorClamp(a, ctx.X(HandleIds.Point, ctx.AnchorX));

        internal static double BaseFn(double x, double accel, double p, double off) =>
            Math.Pow(accel, p - 1) * Math.Pow(x - off, p) / x;

        /// <summary> Acceleration that makes the (uncapped) curve pass through (x, y). </summary>
        private static double AccelFromPoint(double x, double y, double p, double off) =>
            Math.Pow(x * (y - 1) * Math.Pow(x - off, -p), 1 / (p - 1));

        /// <summary> Acceleration that makes the cap (output mode, level 1 ± c) begin at speed x. </summary>
        /// <summary>
        /// A cap level of exactly 1 (c = 0) makes the curve flat whatever the acceleration, and the
        /// cap-point formula then returns 0, which the driver rejects. Keep the previous value then.
        /// </summary>
        private static double KeepPositive(double accel, double previous) =>
            accel > 0 && !double.IsInfinity(accel) ? accel : previous;

        private static double AccelFromCapPoint(bool gain, double x, double c, double p, double off) =>
            gain ? ClassicGain.GainAccel(x, c, p, off)
                 : Math.Pow(x * c * Math.Pow(x - off, -p), 1 / (p - 1));

        /// <summary> Speed where the cap takes effect for normalized (output cap mode) args. </summary>
        internal static double CapStartX(CurveArgs a)
        {
            double c = Math.Abs(a.CapY - 1);
            double p = a.ExponentClassic;
            double off = a.InputOffset;

            if (a.Gain)
            {
                return ClassicGain.GainInverse(c, a.Acceleration, p, off);
            }

            // legacy: base_fn is increasing for x > offset, solve base_fn(x) = c
            double lo = off + Math.Max(1e-9, off * 1e-12);
            return Solver.Bisect(x => BaseFn(x, a.Acceleration, p, off), c, lo, off + 1e9, logScale: true);
        }
    }

    /// <summary>
    /// Power: (scale·x)^n with an output offset, optionally capped. Stored in "output" cap mode
    /// so the scale field stays meaningful.
    /// </summary>
    internal sealed class PowerHandles : IHandleSet
    {
        private const double MinExponent = 0.001;
        private const double MaxExponent = 5;
        private const double MinScale = 1e-30;
        private const double MaxScale = 1e30;

        public CurveArgs Normalize(CurveArgs args)
        {
            var n = args.Clone();
            double exp = args.ExponentPower;

            switch (args.CapMode)
            {
                case CapType.InOut:
                    if (args.Gain)
                    {
                        n.Scale = PowerBase.ScaleFromGainPoint(args.CapX, args.CapY, exp);
                    }
                    else
                    {
                        // legacy io ignores the output offset
                        n.Scale = PowerBase.ScaleFromOutputPoint(args.CapX, args.CapY, exp, 0);
                        n.OutputOffset = 0;
                    }
                    break;
                case CapType.Input:
                    if (args.CapX > 0)
                    {
                        var uncapped = Uncapped(args);
                        n.CapY = args.Gain
                            ? Math.Max(PowerBase.GainFn(args.CapX, exp, args.Scale), args.OutputOffset)
                            : HandleSets.Eval(uncapped, args.CapX);
                    }
                    else
                    {
                        n.CapY = 0;
                    }
                    break;
            }

            n.CapMode = CapType.Output;
            return n;
        }

        public IReadOnlyList<Handle> GetHandles(CurveArgs args, HandleContext ctx)
        {
            var a = Normalize(args);
            var list = new List<Handle>
            {
                new Handle(HandleIds.Start, "Start", new Point2(0, a.OutputOffset), HandleConstraint.YOnly,
                    "Output offset: sensitivity at low speed", nameof(CurveArgs.OutputOffset)),
            };

            double refX;
            if (a.CapY > 0)
            {
                refX = CapStartX(a);
                list.Add(new Handle(HandleIds.Cap, "Cap", new Point2(refX, a.CapY), HandleConstraint.Free,
                    a.Gain ? "Cap: speed where gain stops growing, and the sensitivity it levels off at"
                           : "Cap: speed where sensitivity stops growing, and its maximum",
                    nameof(CurveArgs.CapY), nameof(CurveArgs.Scale)));
            }
            else
            {
                refX = PowerAnchorX(ctx.X(HandleIds.Point, ctx.AnchorX));
                list.Add(new Handle(HandleIds.Point, "Scale", new Point2(refX, HandleSets.Eval(a, refX)), HandleConstraint.Free,
                    "Scale: drag up or down to raise the curve, sideways to pick the speed it pins", nameof(CurveArgs.Scale)) { SlidesAlongCurve = true });
            }

            double xb = PowerBendX(refX, ctx.X(HandleIds.Bend, double.NaN));
            list.Add(new Handle(HandleIds.Bend, "Bend", new Point2(xb, HandleSets.Eval(a, xb)), HandleConstraint.Free,
                "Exponent: drag up or down to change the curvature, sideways to move the bend point", nameof(CurveArgs.ExponentPower), nameof(CurveArgs.Scale)) { SlidesAlongCurve = true });

            return list;
        }

        public CurveArgs Drag(CurveArgs args, string handleId, Point2 target, HandleContext ctx)
        {
            var a = Normalize(args);
            bool capped = a.CapY > 0;

            switch (handleId)
            {
                case HandleIds.Start:
                {
                    double max = capped ? a.CapY * (1 - 1e-6) : double.MaxValue;
                    a.OutputOffset = HandleSets.Clamp(target.Y, 0, max);
                    return a;
                }
                case HandleIds.Cap:
                {
                    double x = Math.Max(target.X, 1e-3);
                    double y = Math.Max(target.Y, Math.Max(a.OutputOffset * (1 + 1e-6), 1e-4));
                    a.CapY = y;
                    a.Scale = ScaleFromCapPoint(a, x, y);
                    return a;
                }
                case HandleIds.Point:
                {
                    double x = PowerAnchorX(target.X);
                    a.Scale = ScaleThrough(a, x, Math.Max(target.Y, 1e-4));
                    return a;
                }
                case HandleIds.Bend:
                {
                    double refX, refY;
                    if (capped)
                    {
                        refX = CapStartX(a);
                        refY = a.CapY;
                    }
                    else
                    {
                        refX = PowerAnchorX(ctx.X(HandleIds.Point, ctx.AnchorX));
                        refY = HandleSets.Eval(a, refX);
                    }

                    double xb = PowerBendX(refX, target.X);
                    var trial = a.Clone();

                    double Apply(double exp)
                    {
                        trial.ExponentPower = exp;
                        trial.Scale = capped ? ScaleFromCapPoint(trial, refX, refY, clamp: false) : ScaleThrough(trial, refX, refY, clamp: false);
                        // tiny exponents need scales beyond double range; tell the solver
                        if (!(trial.Scale > 0) || !HandleSets.Finite(trial.Scale)) return double.NaN;
                        return HandleSets.Eval(trial, xb);
                    }

                    double best = Solver.Bisect(Apply, target.Y, MinExponent, MaxExponent, logScale: true);
                    Apply(best);
                    return trial;
                }
                default:
                    return a;
            }
        }

        private static double PowerAnchorX(double x) => Math.Max(x, 1e-2);

        private static double PowerBendX(double refX, double chosen) =>
            double.IsNaN(chosen) ? refX / 2 : HandleSets.Clamp(chosen, refX * 0.02, refX * 0.98);

        private static CurveArgs Uncapped(CurveArgs a)
        {
            var u = a.Clone();
            u.CapMode = CapType.Output;
            u.CapY = 0;
            return u;
        }

        /// <summary> Scale that makes the uncapped curve pass through (x, y). </summary>
        private static double ScaleThrough(CurveArgs a, double x, double y, bool clamp = true)
        {
            var u = Uncapped(a);
            return Solver.Bisect(s =>
            {
                u.Scale = s;
                return HandleSets.Eval(u, x);
            }, y, MinScale, MaxScale, logScale: true, clamp: clamp);
        }

        /// <summary> Scale that makes the cap (output mode, level y) take effect at speed x. </summary>
        private static double ScaleFromCapPoint(CurveArgs a, double x, double y, bool clamp = true)
        {
            if (a.Gain)
            {
                return PowerBase.ScaleFromGainPoint(x, y, a.ExponentPower);
            }
            return ScaleThrough(a, x, y, clamp);
        }

        internal static double CapStartX(CurveArgs a)
        {
            if (a.Gain)
            {
                return PowerBase.GainInverse(a.CapY, a.ExponentPower, a.Scale);
            }

            var u = Uncapped(a);
            return Solver.Bisect(x => HandleSets.Eval(u, x), a.CapY, 1e-6, 1e9, logScale: true);
        }
    }

    /// <summary> Natural: approaches the limit exponentially after the offset. </summary>
    internal sealed class NaturalHandles : IHandleSet
    {
        public CurveArgs Normalize(CurveArgs args) => args.Clone();

        public IReadOnlyList<Handle> GetHandles(CurveArgs a, HandleContext ctx)
        {
            double off = a.InputOffset;
            double xk = KneeX(a);
            double xl = ctx.X(HandleIds.Limit, Math.Max(ctx.EdgeX, xk * 1.2));
            return new[]
            {
                new Handle(HandleIds.Offset, "Offset", new Point2(off, 1), HandleConstraint.XOnly,
                    "Input offset: speed where acceleration starts", nameof(CurveArgs.InputOffset)),
                new Handle(HandleIds.Knee, "Decay", new Point2(xk, HandleSets.Eval(a, xk)), HandleConstraint.XOnly,
                    "Decay rate: drag left to reach the limit sooner", nameof(CurveArgs.DecayRate)),
                new Handle(HandleIds.Limit, "Limit", new Point2(xl, a.Limit), HandleConstraint.Free,
                    "Limit: sensitivity the curve approaches", nameof(CurveArgs.Limit)) { Kind = HandleKind.Level, SlidesAlongCurve = true },
            };
        }

        public CurveArgs Drag(CurveArgs args, string handleId, Point2 target, HandleContext ctx)
        {
            var a = args.Clone();
            switch (handleId)
            {
                case HandleIds.Offset:
                    a.InputOffset = Math.Max(0, target.X);
                    return a;
                case HandleIds.Knee:
                {
                    double width = Math.Max(target.X - a.InputOffset, 1e-3);
                    a.DecayRate = Math.Abs(a.Limit - 1) / width;
                    return a;
                }
                case HandleIds.Limit:
                {
                    // keep the knee position (decay per unit of limit) while changing the limit
                    double width = KneeX(a) - a.InputOffset;
                    a.Limit = HandleSets.AwayFromOne(Math.Max(target.Y, 1e-4));
                    a.DecayRate = Math.Abs(a.Limit - 1) / width;
                    return a;
                }
                default:
                    return a;
            }
        }

        /// <summary> offset + 1/k, where k = decay / |limit - 1| is the exponential rate. </summary>
        internal static double KneeX(CurveArgs a) => a.InputOffset + Math.Abs(a.Limit - 1) / a.DecayRate;
    }

    /// <summary> Jump: a (smoothed) step to the cap level at the cap speed. </summary>
    internal sealed class JumpHandles : IHandleSet
    {
        public CurveArgs Normalize(CurveArgs args) => args.Clone();

        public IReadOnlyList<Handle> GetHandles(CurveArgs a, HandleContext ctx)
        {
            double xs = SmoothX(a);
            return new[]
            {
                new Handle(HandleIds.Jump, "Jump", new Point2(a.CapX, a.CapY), HandleConstraint.Free,
                    "Jump: speed of the step and the sensitivity after it", nameof(CurveArgs.CapX), nameof(CurveArgs.CapY)),
                new Handle(HandleIds.Smooth, "Smooth", new Point2(xs, HandleSets.Eval(a, xs)), HandleConstraint.XOnly,
                    "Smoothness: width of the transition", nameof(CurveArgs.Smooth)),
            };
        }

        public CurveArgs Drag(CurveArgs args, string handleId, Point2 target, HandleContext ctx)
        {
            var a = args.Clone();
            switch (handleId)
            {
                case HandleIds.Jump:
                    a.CapX = Math.Max(target.X, 1e-3);
                    a.CapY = Math.Max(target.Y, 1e-4);
                    return a;
                case HandleIds.Smooth:
                    a.Smooth = HandleSets.Clamp(2 * (target.X / a.CapX - 1), 0, 1);
                    return a;
                default:
                    return a;
            }
        }

        internal static double SmoothX(CurveArgs a) => a.CapX * (1 + a.Smooth / 2);
    }

    /// <summary> Synchronous: log-log sigmoid between 1/motivity and motivity around the sync speed. </summary>
    internal sealed class SynchronousHandles : IHandleSet
    {
        private const double MinMotivity = 1.001;

        public CurveArgs Normalize(CurveArgs args) => args.Clone();

        public IReadOnlyList<Handle> GetHandles(CurveArgs a, HandleContext ctx)
        {
            double xg = GammaX(a);
            double xs = SmoothHandleX(a, ctx.X(HandleIds.Smooth, double.NaN));
            double xm = ctx.X(HandleIds.Motivity, Math.Max(ctx.EdgeX, SmoothX(a) * 1.2));
            return new[]
            {
                new Handle(HandleIds.Sync, "Sync", new Point2(a.SyncSpeed, HandleSets.Eval(a, a.SyncSpeed)), HandleConstraint.XOnly,
                    "Sync speed: centre of the transition", nameof(CurveArgs.SyncSpeed)),
                new Handle(HandleIds.Gamma, "Gamma", new Point2(xg, HandleSets.Eval(a, xg)), HandleConstraint.XOnly,
                    "Gamma: drag toward the sync point for a faster transition", nameof(CurveArgs.Gamma)),
                new Handle(HandleIds.Smooth, "Smooth", new Point2(xs, HandleSets.Eval(a, xs)), HandleConstraint.Free,
                    "Smoothness: drag up or down for how gently the curve approaches motivity, sideways to move the point", nameof(CurveArgs.Smooth)) { SlidesAlongCurve = true },
                new Handle(HandleIds.Motivity, "Motivity", new Point2(xm, a.Motivity), HandleConstraint.Free,
                    "Motivity: maximum sensitivity (minimum is its reciprocal)", nameof(CurveArgs.Motivity)) { Kind = HandleKind.Level, SlidesAlongCurve = true },
            };
        }

        public CurveArgs Drag(CurveArgs args, string handleId, Point2 target, HandleContext ctx)
        {
            var a = args.Clone();
            switch (handleId)
            {
                case HandleIds.Sync:
                    a.SyncSpeed = Math.Max(target.X, 1e-3);
                    return a;
                case HandleIds.Gamma:
                {
                    double ratio = Math.Max(target.X / a.SyncSpeed, 1 + 1e-4);
                    a.Gamma = Math.Log(a.Motivity) / (2 * Math.Log(ratio));
                    return a;
                }
                case HandleIds.Smooth:
                {
                    double xs = SmoothHandleX(a, target.X);
                    var trial = a.Clone();
                    double best = Solver.Bisect(s =>
                    {
                        trial.Smooth = s;
                        return HandleSets.Eval(trial, xs);
                    }, target.Y, 0, 1);
                    a.Smooth = best;
                    return a;
                }
                case HandleIds.Motivity:
                    a.Motivity = Math.Max(target.Y, MinMotivity);
                    return a;
                default:
                    return a;
            }
        }

        /// <summary> Halfway (in log space) from sync speed to where the linear-clamp curve reaches motivity. </summary>
        internal static double GammaX(CurveArgs a) => a.SyncSpeed * Math.Pow(a.Motivity, 1 / (2 * a.Gamma));

        /// <summary> Smoothness handle speed: the chosen one (kept above the sync speed) or where the curve reaches motivity. </summary>
        internal static double SmoothHandleX(CurveArgs a, double chosen) =>
            double.IsNaN(chosen) ? SmoothX(a) : Math.Max(chosen, a.SyncSpeed * 1.05);

        /// <summary> Where the linear-clamp curve reaches motivity. </summary>
        internal static double SmoothX(CurveArgs a) => a.SyncSpeed * Math.Pow(a.Motivity, 1 / a.Gamma);
    }

    /// <summary> Custom curve: free-form control points written as a lookup table. </summary>
    public sealed class LookupHandles : IHandleSet
    {
        public const double MinY = 1e-4;

        public CurveArgs Normalize(CurveArgs args) => args.Clone();

        public IReadOnlyList<Handle> GetHandles(CurveArgs a, HandleContext ctx)
        {
            var list = new List<Handle>(a.ControlPoints.Count);
            for (int i = 0; i < a.ControlPoints.Count; i++)
            {
                list.Add(new Handle(HandleIds.LutPoint(i), "Point " + (i + 1), a.ControlPoints[i], HandleConstraint.Free,
                    "Drag to move; double-click the chart to add a point; right-click or Delete to remove",
                    nameof(CurveArgs.ControlPoints)));
            }
            return list;
        }

        public CurveArgs Drag(CurveArgs args, string handleId, Point2 target, HandleContext ctx)
        {
            var a = args.Clone();
            if (!HandleIds.TryParseLutPoint(handleId, out int i) || i < 0 || i >= a.ControlPoints.Count) return a;

            var pts = a.ControlPoints;
            double lo = i > 0 ? NextAbove(pts[i - 1].X) : 1e-3;
            double hi = i < pts.Count - 1 ? NextBelow(pts[i + 1].X) : double.MaxValue;
            double x = HandleSets.Clamp(target.X, lo, Math.Max(lo, hi));
            pts[i] = new Point2(x, Math.Max(target.Y, MinY));
            return a;
        }

        public static int MaxControlPoints(CurveArgs a) =>
            a.SmoothLut ? LutBuilder.MaxSmoothControlPoints : CurveArgs.MaxLutPoints;

        /// <summary> Inserts a point, keeping x sorted. Returns the new index, or -1 if it can't be added. </summary>
        public static int AddPoint(CurveArgs a, Point2 p)
        {
            if (a.ControlPoints.Count >= MaxControlPoints(a) || p.X <= 0) return -1;

            var pts = a.ControlPoints;
            int idx = pts.FindIndex(q => q.X >= p.X);
            if (idx < 0) idx = pts.Count;
            if (idx < pts.Count && (float)pts[idx].X == (float)p.X) return -1;
            if (idx > 0 && (float)pts[idx - 1].X == (float)p.X) return -1;

            pts.Insert(idx, new Point2(p.X, Math.Max(p.Y, MinY)));
            return idx;
        }

        public static bool RemovePoint(CurveArgs a, int index)
        {
            if (a.ControlPoints.Count <= 2 || index < 0 || index >= a.ControlPoints.Count) return false;
            a.ControlPoints.RemoveAt(index);
            return true;
        }

        /// <summary> Samples any curve into log-spaced control points so it can be edited free-form. </summary>
        public static List<Point2> SampleCurve(CurveArgs source, double maxX, int count = 12)
        {
            var curve = CurveMath.Create(source);
            var pts = new List<Point2>(count);
            double minX = Math.Max(maxX / 200, 0.05);
            for (int i = 0; i < count; i++)
            {
                double x = minX * Math.Pow(maxX / minX, i / (double)(count - 1));
                pts.Add(new Point2(x, Math.Max(curve.Sensitivity(x), MinY)));
            }
            return pts;
        }

        // keep neighbours distinct once converted to float by the driver
        private static double NextAbove(double x) => x + Math.Max(Math.Abs(x) * 1e-5, 1e-4);

        private static double NextBelow(double x) => x - Math.Max(Math.Abs(x) * 1e-5, 1e-4);
    }
}

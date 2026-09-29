using System;

namespace CurveEditor.Core.Curves
{
    using static System.Math;

    /// <summary> A sensitivity curve: input speed (counts/ms at 1000 DPI) to sensitivity multiplier. </summary>
    public interface ICurve
    {
        double Sensitivity(double x);
    }

    /// <summary>
    /// C# port of the accel implementations in common/accel-*.hpp. Each class mirrors
    /// its native counterpart line for line so the editor preview matches the driver;
    /// wrapper-tests/CurveMathParityTests.cs checks this against the native code.
    /// </summary>
    public static class CurveMath
    {
        public static ICurve Create(CurveArgs a)
        {
            switch (a.Mode)
            {
                case CurveMode.Classic: return a.Gain ? new ClassicGain(a) : (ICurve)new ClassicLegacy(a);
                case CurveMode.Jump: return a.Gain ? new JumpGain(a) : (ICurve)new JumpLegacy(a);
                case CurveMode.Natural: return a.Gain ? new NaturalGain(a) : (ICurve)new NaturalLegacy(a);
                case CurveMode.Synchronous: return a.Gain ? new SynchronousGain(a) : (ICurve)new SynchronousLegacy(a);
                case CurveMode.Power: return a.Gain ? new PowerGain(a) : (ICurve)new PowerLegacy(a);
                case CurveMode.Lookup: return new Lookup(a.BuildLut(), a.Gain);
                default: return NoAccel.Instance;
            }
        }

        public static double Sensitivity(CurveArgs a, double x) => Create(a).Sensitivity(x);

        public static double Velocity(this ICurve c, double x) => x * c.Sensitivity(x);

        /// <summary> Slope of output velocity (numerical derivative). </summary>
        public static double Gain(this ICurve c, double x)
        {
            double h = Max(x * 1e-4, 1e-6);
            double lo = Max(x - h, 1e-9);
            return (c.Velocity(x + h) - c.Velocity(lo)) / (x + h - lo);
        }

        internal static double Min(double a, double b) => (a < b) ? a : b;

        internal static double Max(double a, double b) => (b < a) ? a : b;

        /// <summary> ra::lerp — clamps to b for t in [0, 1], extrapolates otherwise. </summary>
        internal static double Lerp(double a, double b, double t)
        {
            double x = a + t * (b - a);
            if ((t > 1) == (a < b))
            {
                return Max(x, b);
            }
            return Min(x, b);
        }

        /// <summary> ra::ilogb — unbiased exponent of a normal double. </summary>
        internal static int ILogB(double x)
        {
            long bits = BitConverter.DoubleToInt64Bits(x);
            return (int)((bits >> 52) & 0x7ff) - 0x3ff;
        }

        internal static double ScalBn(double x, int n) => x * Pow(2, n);
    }

    internal sealed class NoAccel : ICurve
    {
        public static readonly NoAccel Instance = new NoAccel();

        public double Sensitivity(double x) => 1;
    }

    internal abstract class ClassicBase
    {
        protected readonly double Offset;
        protected readonly double Power;

        protected ClassicBase(CurveArgs a)
        {
            Offset = a.InputOffset;
            Power = a.ExponentClassic;
        }

        protected double BaseFn(double x, double accelRaised) => accelRaised * Pow(x - Offset, Power) / x;

        protected double BaseAccel(double x, double y) => Pow(x * y * Pow(x - Offset, -Power), 1 / (Power - 1));
    }

    internal sealed class ClassicLegacy : ClassicBase, ICurve
    {
        private readonly double accelRaised;
        private readonly double cap = double.MaxValue;
        private readonly double sign = 1;

        public ClassicLegacy(CurveArgs a) : base(a)
        {
            switch (a.CapMode)
            {
                case CapType.InOut:
                    cap = a.CapY - 1;
                    if (cap < 0)
                    {
                        cap = -cap;
                        sign = -sign;
                    }
                    accelRaised = Pow(BaseAccel(a.CapX, cap), Power - 1);
                    break;
                case CapType.Input:
                    accelRaised = Pow(a.Acceleration, Power - 1);
                    if (a.CapX > 0)
                    {
                        cap = BaseFn(a.CapX, accelRaised);
                    }
                    break;
                default:
                    accelRaised = Pow(a.Acceleration, Power - 1);
                    if (a.CapY > 0)
                    {
                        cap = a.CapY - 1;
                        if (cap < 0)
                        {
                            cap = -cap;
                            sign = -sign;
                        }
                    }
                    break;
            }
        }

        public double Sensitivity(double x)
        {
            if (x <= Offset) return 1;
            return sign * CurveMath.Min(BaseFn(x, accelRaised), cap) + 1;
        }
    }

    internal sealed class ClassicGain : ClassicBase, ICurve
    {
        private readonly double accelRaised;
        private readonly double capX = double.MaxValue;
        private readonly double capY = double.MaxValue;
        private readonly double constant;
        private readonly double sign = 1;

        public ClassicGain(CurveArgs a) : base(a)
        {
            switch (a.CapMode)
            {
                case CapType.InOut:
                    capX = a.CapX;
                    capY = a.CapY - 1;
                    if (capY < 0)
                    {
                        capY = -capY;
                        sign = -sign;
                    }
                    accelRaised = Pow(GainAccel(capX, capY, Power, Offset), Power - 1);
                    constant = (BaseFn(capX, accelRaised) - capY) * capX;
                    break;
                case CapType.Input:
                    accelRaised = Pow(a.Acceleration, Power - 1);
                    if (a.CapX > 0)
                    {
                        capX = a.CapX;
                        capY = GainFn(capX, a.Acceleration, Power, Offset);
                        constant = (BaseFn(capX, accelRaised) - capY) * capX;
                    }
                    break;
                default:
                    accelRaised = Pow(a.Acceleration, Power - 1);
                    if (a.CapY > 0)
                    {
                        capY = a.CapY - 1;
                        if (capY == 0)
                        {
                            capX = 0;
                        }
                        else
                        {
                            if (capY < 0)
                            {
                                capY = -capY;
                                sign = -sign;
                            }
                            capX = GainInverse(capY, a.Acceleration, Power, Offset);
                            constant = (BaseFn(capX, accelRaised) - capY) * capX;
                        }
                    }
                    break;
            }
        }

        public double Sensitivity(double x)
        {
            if (x <= Offset) return 1;
            double output = x < capX ? BaseFn(x, accelRaised) : constant / x + capY;
            return sign * output + 1;
        }

        public static double GainFn(double x, double accel, double power, double offset) =>
            power * Pow(accel * (x - offset), power - 1);

        public static double GainInverse(double y, double accel, double power, double offset) =>
            (accel * offset + Pow(y / power, 1 / (power - 1))) / accel;

        public static double GainAccel(double x, double y, double power, double offset) =>
            -Pow(y / power, 1 / (power - 1)) / (offset - x);
    }

    internal abstract class NaturalBase
    {
        protected readonly double Offset;
        protected readonly double Accel;
        protected readonly double LimitM1;

        protected NaturalBase(CurveArgs a)
        {
            Offset = a.InputOffset;
            LimitM1 = a.Limit - 1;
            Accel = a.DecayRate / Abs(LimitM1);
        }
    }

    internal sealed class NaturalLegacy : NaturalBase, ICurve
    {
        public NaturalLegacy(CurveArgs a) : base(a) { }

        public double Sensitivity(double x)
        {
            if (x <= Offset) return 1;
            double offsetX = Offset - x;
            double decay = Exp(Accel * offsetX);
            return LimitM1 * (1 - (Offset - decay * offsetX) / x) + 1;
        }
    }

    internal sealed class NaturalGain : NaturalBase, ICurve
    {
        private readonly double constant;

        public NaturalGain(CurveArgs a) : base(a)
        {
            constant = -LimitM1 / Accel;
        }

        public double Sensitivity(double x)
        {
            if (x <= Offset) return 1;
            double offsetX = Offset - x;
            double decay = Exp(Accel * offsetX);
            double output = LimitM1 * (decay / Accel - offsetX) + constant;
            return output / x + 1;
        }
    }

    internal abstract class JumpBase
    {
        private const double SmoothScale = 2 * PI;

        protected readonly double StepX;
        protected readonly double StepY;
        protected readonly double SmoothRate;

        protected JumpBase(CurveArgs a)
        {
            StepX = a.CapX;
            StepY = a.CapY - 1;
            double rateInverse = a.Smooth * StepX;
            SmoothRate = rateInverse < 1 ? 0 : SmoothScale / rateInverse;
        }

        protected bool IsSmooth => SmoothRate != 0;

        protected double Decay(double x) => Exp(SmoothRate * (StepX - x));

        protected double SmoothFn(double x) => StepY / (1 + Decay(x));

        protected double SmoothAntideriv(double x) => StepY * (x + Log(1 + Decay(x)) / SmoothRate);
    }

    internal sealed class JumpLegacy : JumpBase, ICurve
    {
        public JumpLegacy(CurveArgs a) : base(a) { }

        public double Sensitivity(double x)
        {
            if (IsSmooth) return SmoothFn(x) + 1;
            if (x < StepX) return 1;
            return 1 + StepY;
        }
    }

    internal sealed class JumpGain : JumpBase, ICurve
    {
        private readonly double c;

        public JumpGain(CurveArgs a) : base(a)
        {
            c = -SmoothAntideriv(0);
        }

        public double Sensitivity(double x)
        {
            if (x <= 0) return 1;
            if (IsSmooth) return 1 + (SmoothAntideriv(x) + c) / x;
            if (x < StepX) return 1;
            return 1 + StepY * (x - StepX) / x;
        }
    }

    internal abstract class PowerBase
    {
        protected readonly double N;
        protected readonly double ScaleValue;
        protected readonly double OffsetX;
        protected readonly double OffsetY;
        protected readonly double Constant;

        protected PowerBase(CurveArgs a)
        {
            N = a.ExponentPower;

            if (a.CapMode != CapType.InOut)
            {
                ScaleValue = a.Scale;
            }
            else if (a.Gain)
            {
                ScaleValue = ScaleFromGainPoint(a.CapX, a.CapY, N);
            }
            else
            {
                // legacy + io cap: offset is ignored (see accel-power.hpp)
                OffsetX = 0;
                OffsetY = 0;
                Constant = 0;
                ScaleValue = ScaleFromOutputPoint(a.CapX, a.CapY, N, Constant);
                return;
            }

            OffsetX = GainInverse(a.OutputOffset, N, ScaleValue);
            OffsetY = a.OutputOffset;
            Constant = OffsetX * OffsetY * N / (N + 1);
        }

        protected double BaseFn(double x)
        {
            if (x <= OffsetX) return OffsetY;
            return Pow(ScaleValue * x, N) + Constant / x;
        }

        public static double GainFn(double input, double power, double scale) => (power + 1) * Pow(input * scale, power);

        public static double GainInverse(double gain, double power, double scale) => Pow(gain / (power + 1), 1 / power) / scale;

        public static double ScaleFromGainPoint(double input, double gain, double power) => Pow(gain / (power + 1), 1 / power) / input;

        public static double ScaleFromOutputPoint(double input, double output, double power, double c) => Pow(output - c / input, 1 / power) / input;
    }

    internal sealed class PowerLegacy : PowerBase, ICurve
    {
        private readonly double cap = double.MaxValue;

        public PowerLegacy(CurveArgs a) : base(a)
        {
            switch (a.CapMode)
            {
                case CapType.InOut:
                    cap = a.CapY;
                    break;
                case CapType.Input:
                    if (a.CapX > 0) cap = BaseFn(a.CapX);
                    break;
                default:
                    if (a.CapY > 0) cap = a.CapY;
                    break;
            }
        }

        public double Sensitivity(double x) => CurveMath.Min(BaseFn(x), cap);
    }

    internal sealed class PowerGain : PowerBase, ICurve
    {
        private readonly double capX = double.MaxValue;
        private readonly double capY = double.MaxValue;
        private readonly double constantB;

        public PowerGain(CurveArgs a) : base(a)
        {
            switch (a.CapMode)
            {
                case CapType.InOut:
                    capX = a.CapX;
                    capY = a.CapY;
                    break;
                case CapType.Input:
                    if (a.CapX > 0)
                    {
                        if (a.CapX <= OffsetX)
                        {
                            capX = 0;
                            capY = OffsetY;
                            constantB = 0;
                            return;
                        }
                        capX = a.CapX;
                        capY = GainFn(a.CapX, N, ScaleValue);
                    }
                    break;
                default:
                    if (a.CapY > 0)
                    {
                        capX = GainInverse(a.CapY, N, ScaleValue);
                        capY = a.CapY;
                    }
                    break;
            }

            constantB = (BaseFn(capX) - capY) * capX;
        }

        public double Sensitivity(double speed)
        {
            if (speed < capX) return BaseFn(speed);
            return capY + constantB / speed;
        }
    }

    internal sealed class SynchronousLegacy : ICurve
    {
        private readonly double logMotivity;
        private readonly double gammaConst;
        private readonly double logSyncSpeed;
        private readonly double syncSpeed;
        private readonly double sharpness;
        private readonly double sharpnessRecip;
        private readonly bool useLinearClamp;
        private readonly double minimumSens;
        private readonly double maximumSens;

        public SynchronousLegacy(CurveArgs a)
        {
            logMotivity = Log(a.Motivity);
            gammaConst = a.Gamma / logMotivity;
            logSyncSpeed = Log(a.SyncSpeed);
            syncSpeed = a.SyncSpeed;
            sharpness = a.Smooth == 0 ? 16 : 0.5 / a.Smooth;
            sharpnessRecip = 1 / sharpness;
            useLinearClamp = sharpness >= 16;
            minimumSens = 1 / a.Motivity;
            maximumSens = a.Motivity;
        }

        public double Sensitivity(double x)
        {
            if (useLinearClamp)
            {
                double logSpace = gammaConst * (Log(x) - logSyncSpeed);
                if (logSpace < -1) return minimumSens;
                if (logSpace > 1) return maximumSens;
                return Exp(logSpace * logMotivity);
            }

            if (x == syncSpeed) return 1.0;

            double logDiff = Log(x) - logSyncSpeed;

            if (logDiff > 0)
            {
                double logSpace = gammaConst * logDiff;
                double exponent = Pow(Tanh(Pow(logSpace, sharpness)), sharpnessRecip);
                return Exp(exponent * logMotivity);
            }
            else
            {
                double logSpace = -gammaConst * logDiff;
                double exponent = -Pow(Tanh(Pow(logSpace, sharpness)), sharpnessRecip);
                return Exp(exponent * logMotivity);
            }
        }
    }

    /// <summary> Port of activation_framework&lt;GAIN&gt;: integrates the legacy curve into a log-linear velocity table. </summary>
    internal sealed class SynchronousGain : ICurve
    {
        private const int RangeStart = -3;
        private const int RangeStop = 9;
        private const int RangeNum = 8;
        private const int Capacity = 514;
        private const int Size = (RangeStop - RangeStart) * RangeNum + 1;

        private readonly float[] data = new float[Size];
        private readonly double xStart = CurveMath.ScalBn(1, RangeStart);

        public SynchronousGain(CurveArgs args)
        {
            var sig = new SynchronousLegacy(args);
            double sum = 0;
            double a = 0;
            int i = 0;

            void Fill(double b)
            {
                const int partitions = 2;
                double interval = (b - a) / partitions;
                for (int p = 1; p <= partitions; p++)
                {
                    sum += sig.Sensitivity(a + p * interval) * interval;
                }
                a = b;
                data[i++] = (float)sum;
            }

            for (int e = 0; e < RangeStop - RangeStart; e++)
            {
                double expScale = CurveMath.ScalBn(1, e + RangeStart) / RangeNum;
                for (int n = 0; n < RangeNum; n++)
                {
                    Fill((n + RangeNum) * expScale);
                }
            }

            Fill(CurveMath.ScalBn(1, RangeStop));
        }

        public double Sensitivity(double x)
        {
            int e = Math.Min(CurveMath.ILogB(x), RangeStop - 1);

            if (e >= RangeStart)
            {
                int idxIntLogPart = e - RangeStart;
                double idxFracLinPart = CurveMath.ScalBn(x, -e) - 1;
                double idxF = RangeNum * (idxIntLogPart + idxFracLinPart);

                int idx = Math.Min((int)idxF, Size - 2);

                if ((uint)idx < Capacity - 1)
                {
                    double y = CurveMath.Lerp(data[idx], data[idx + 1], idxF - idx);
                    return y / x;
                }
            }

            return data[0] / xStart;
        }
    }

    /// <summary> Port of ra::lookup. Points are stored as floats, as in the driver. </summary>
    internal sealed class Lookup : ICurve
    {
        private readonly float[] xs;
        private readonly float[] ys;
        private readonly bool velocity;

        public Lookup(Point2[] points, bool velocity)
        {
            xs = new float[points.Length];
            ys = new float[points.Length];
            for (int i = 0; i < points.Length; i++)
            {
                xs[i] = (float)points[i].X;
                ys[i] = (float)points[i].Y;
            }
            this.velocity = velocity;
        }

        public double Sensitivity(double x)
        {
            int size = xs.Length;
            if (size == 0) return 1;

            int lo = 0;
            int hi = size - 2;

            if (x <= 0) return 0;

            if (hi < CurveArgs.MaxLutPoints - 1)
            {
                while (lo <= hi)
                {
                    int mid = (lo + hi) / 2;

                    if (x < xs[mid])
                    {
                        hi = mid - 1;
                    }
                    else if (x > xs[mid])
                    {
                        lo = mid + 1;
                    }
                    else
                    {
                        double y = ys[mid];
                        if (velocity) y /= x;
                        return y;
                    }
                }

                if (lo > 0)
                {
                    double t = (x - xs[lo - 1]) / (xs[lo] - xs[lo - 1]);
                    double y = CurveMath.Lerp(ys[lo - 1], ys[lo], t);
                    if (velocity) y /= x;
                    return y;
                }
            }

            double y0 = ys[0];
            if (velocity) y0 /= xs[0];
            return y0;
        }
    }
}

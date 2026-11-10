using System;

namespace CurveEditor.Core.Curves
{
    public static class Solver
    {
        private static bool Finite(double v) => !double.IsNaN(v) && !double.IsInfinity(v);

        /// <summary>
        /// Finds p in [lo, hi] with f(p) ≈ target, assuming f is monotonic on the interval
        /// (either direction). f may return NaN where the parameter is unusable; such regions
        /// at either end of the interval are trimmed away first.
        /// When the target is out of reach, returns the nearer usable endpoint, or NaN if
        /// <paramref name="clamp"/> is false. When <paramref name="logScale"/> is set, bisects in
        /// log space (lo must be positive).
        /// </summary>
        public static double Bisect(Func<double, double> f, double target, double lo, double hi, bool logScale = false, bool clamp = true, int iterations = 100)
        {
            double Map(double t) => logScale ? Math.Exp(t) : t;

            double a = logScale ? Math.Log(lo) : lo;
            double b = logScale ? Math.Log(hi) : hi;

            double fa = f(Map(a));
            for (int i = 0; i < 200 && !Finite(fa); i++)
            {
                a += (b - a) * 0.1;
                fa = f(Map(a));
            }

            double fb = f(Map(b));
            for (int i = 0; i < 200 && !Finite(fb); i++)
            {
                b -= (b - a) * 0.1;
                fb = f(Map(b));
            }

            if (!Finite(fa) || !Finite(fb)) return double.NaN;

            bool increasing = fb >= fa;

            if (increasing ? target <= fa : target >= fa) return clamp || target == fa ? Map(a) : double.NaN;
            if (increasing ? target >= fb : target <= fb) return clamp || target == fb ? Map(b) : double.NaN;

            for (int i = 0; i < iterations; i++)
            {
                double m = a + (b - a) / 2;
                if (m <= a || m >= b) break;

                double fm = f(Map(m));
                if (!Finite(fm)) break;

                if ((fm < target) == increasing)
                {
                    a = m;
                }
                else
                {
                    b = m;
                }
            }

            return Map(a + (b - a) / 2);
        }
    }
}

using System.Collections.Generic;

namespace CurveEditor.Core
{
    /// <summary> Port of the accel_args checks in common/rawaccel-validate.hpp, plus the grapher's LUT rules. </summary>
    public static class Validation
    {
        /// <summary>
        /// The table rules, judged on the floats the driver stores: values beyond float range
        /// become infinite there and tiny ones become 0, and NaN would slip through comparisons.
        /// </summary>
        private static string PointsError(IReadOnlyList<Point2> points)
        {
            for (int i = 0; i < points.Count; i++)
            {
                float x = (float)points[i].X, y = (float)points[i].Y;
                if (float.IsNaN(x) || float.IsInfinity(x) || float.IsNaN(y) || float.IsInfinity(y))
                    return "lookup values must be finite numbers";
                if (x <= 0 || (i > 0 && x <= (float)points[i - 1].X))
                    return "lookup x values must be positive and strictly increasing";
                if (y <= 0)
                    return "lookup y values must be positive";
            }
            return null;
        }

        public static List<string> Validate(CurveArgs a)
        {
            var errors = new List<string>();
            void Error(string msg) => errors.Add(msg);

            if (a.Mode == CurveMode.Lookup)
            {
                // the control points first: smoothing points the driver can't hold can overflow
                // the spline, which then throws instead of giving a table to check
                string pointError = a.ControlPoints == null ? null : PointsError(a.ControlPoints);
                Point2[] lut = null;
                if (pointError == null)
                {
                    try
                    {
                        lut = a.BuildLut();
                    }
                    catch (System.ArithmeticException)
                    {
                        pointError = "lookup values are too extreme to smooth";
                    }
                }

                if (pointError != null)
                {
                    Error(pointError);
                }
                else
                {
                    if (lut.Length < 2)
                    {
                        Error("lookup mode requires at least 2 points");
                    }
                    else if (lut.Length > CurveArgs.MaxLutPoints)
                    {
                        Error("too many data points (max=257)");
                    }

                    // the table built from them (smoothed, or scaled to velocity) must hold too
                    string tableError = PointsError(lut);
                    if (tableError != null) Error(tableError);
                }
            }

            if (a.InputOffset < 0 || a.OutputOffset < 0)
            {
                Error("offset can not be negative");
            }

            bool jumpOrIoCap =
                a.Mode == CurveMode.Jump ||
                ((a.Mode == CurveMode.Classic || a.Mode == CurveMode.Power) && a.CapMode == CapType.InOut);

            if (a.CapX < 0) Error("cap (input) can not be negative");
            else if (a.CapX == 0 && jumpOrIoCap) Error("cap (input) can not be 0");

            if (a.CapY < 0) Error("cap (output) can not be negative");
            else if (a.CapY == 0 && jumpOrIoCap) Error("cap (output) can not be 0");

            if ((a.Mode == CurveMode.Classic && a.CapX > 0 && a.CapX < a.InputOffset && a.CapMode != CapType.Output) ||
                (a.Mode == CurveMode.Power && a.CapY > 0 && a.CapY < a.OutputOffset && a.CapMode != CapType.Input))
            {
                Error("cap < offset");
            }

            if (a.Acceleration <= 0) Error("acceleration must be positive");
            if (a.Scale <= 0) Error("scale must be positive");
            if (a.Gamma <= 0) Error("gamma must be positive");
            if (a.DecayRate <= 0) Error("decay rate must be positive");
            if (a.Motivity <= 1) Error("motivity must be greater than 1");
            if (a.ExponentClassic <= 1) Error("exponent must be greater than 1");
            if (a.ExponentPower <= 0) Error("exponent must be positive");
            if (a.Limit <= 0) Error("limit must be positive");
            if (a.SyncSpeed <= 0) Error("synchronous speed must be positive");
            if (a.Smooth < 0 || a.Smooth > 1) Error("smooth must be between 0 and 1");

            return errors;
        }
    }
}

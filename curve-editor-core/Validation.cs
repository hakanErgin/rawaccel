using System.Collections.Generic;

namespace CurveEditor.Core
{
    /// <summary> Port of the accel_args checks in common/rawaccel-validate.hpp, plus the grapher's LUT rules. </summary>
    public static class Validation
    {
        public static List<string> Validate(CurveArgs a)
        {
            var errors = new List<string>();
            void Error(string msg) => errors.Add(msg);

            if (a.Mode == CurveMode.Lookup)
            {
                var lut = a.BuildLut();

                if (lut.Length < 2)
                {
                    Error("lookup mode requires at least 2 points");
                }
                else if (lut.Length > CurveArgs.MaxLutPoints)
                {
                    Error("too many data points (max=257)");
                }

                for (int i = 0; i < lut.Length; i++)
                {
                    if (lut[i].X <= 0 || (i > 0 && (float)lut[i].X <= (float)lut[i - 1].X))
                    {
                        Error("lookup x values must be positive and strictly increasing");
                        break;
                    }
                    if (lut[i].Y <= 0)
                    {
                        Error("lookup y values must be positive");
                        break;
                    }
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

using System.Collections.Generic;
using System.Linq;
using CurveEditor.Core;
using CurveEditor.Core.Handles;

namespace CurveEditor.Services
{
    /// <summary> Converts between the wrapper's AccelArgs and the editor's CurveArgs. </summary>
    public static class ArgsMapper
    {
        public const int LutRawCapacity = 514;

        public static CurveArgs FromAccelArgs(AccelArgs a, CustomCurveSidecar.CurveEntry saved)
        {
            var c = new CurveArgs
            {
                Mode = (CurveMode)(int)a.mode,
                Gain = a.gain,
                InputOffset = a.inputOffset,
                OutputOffset = a.outputOffset,
                Acceleration = a.acceleration,
                DecayRate = a.decayRate,
                Gamma = a.gamma,
                Motivity = a.motivity,
                ExponentClassic = a.exponentClassic,
                Scale = a.scale,
                ExponentPower = a.exponentPower,
                Limit = a.limit,
                SyncSpeed = a.syncSpeed,
                Smooth = a.smooth,
                CapX = a.cap.x,
                CapY = a.cap.y,
                CapMode = (CapType)(int)a.capMode,
            };

            if (c.Mode == CurveMode.Lookup)
            {
                CustomCurveSidecar.Restore(c, ReadLut(a), saved);
            }

            // classic/power are edited in "output" cap mode; this rewrite keeps the curve identical
            return HandleSets.For(c.Mode).Normalize(c);
        }

        public static List<Point2> ReadLut(AccelArgs a)
        {
            var points = new List<Point2>();
            if (a.data == null) return points;
            int length = System.Math.Min(a.length, a.data.Length);
            for (int i = 0; i + 1 < length; i += 2)
            {
                points.Add(new Point2(a.data[i], a.data[i + 1]));
            }
            return points;
        }

        /// <summary> Writes the curve into a copy of <paramref name="a"/>, leaving nothing else changed. </summary>
        public static AccelArgs ToAccelArgs(CurveArgs c, AccelArgs a)
        {
            a.mode = (AccelMode)(int)c.Mode;
            a.gain = c.Gain;
            a.inputOffset = c.InputOffset;
            a.outputOffset = c.OutputOffset;
            a.acceleration = c.Acceleration;
            a.decayRate = c.DecayRate;
            a.gamma = c.Gamma;
            a.motivity = c.Motivity;
            a.exponentClassic = c.ExponentClassic;
            a.scale = c.Scale;
            a.exponentPower = c.ExponentPower;
            a.limit = c.Limit;
            a.syncSpeed = c.SyncSpeed;
            a.smooth = c.Smooth;
            a.cap = new Vec2<double> { x = c.CapX, y = c.CapY };
            a.capMode = (CapMode)(int)c.CapMode;

            // the array must be exactly LUT_RAW_DATA_CAPACITY long for marshalling
            var data = new float[LutRawCapacity];
            int length = 0;

            if (c.Mode == CurveMode.Lookup)
            {
                foreach (var p in c.BuildLut().Take(CurveArgs.MaxLutPoints))
                {
                    data[length++] = (float)p.X;
                    data[length++] = (float)p.Y;
                }
            }

            a.data = data;
            a.length = length;
            return a;
        }
    }
}

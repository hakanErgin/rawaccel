using System;
using System.Collections.Generic;
using System.Linq;
using CurveEditor.Core;
using CurveEditor.Core.Curves;
using CurveEditor.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace wrapper_tests
{
    /// <summary>
    /// The curve editor previews curves with a C# port of the accel code. This checks that
    /// preview against the real driver pipeline (ManagedAccel), going through the same
    /// ArgsMapper the editor uses to write settings.
    /// </summary>
    [TestClass]
    public class CurveEditorParityTests
    {
        private static IEnumerable<CurveArgs> Cases()
        {
            foreach (bool gain in new[] { true, false })
            {
                foreach (var cap in new[] { CapType.InOut, CapType.Input, CapType.Output })
                {
                    yield return new CurveArgs { Mode = CurveMode.Classic, Gain = gain, CapMode = cap, Acceleration = 0.02, InputOffset = 2, CapX = 20, CapY = 1.7 };
                    yield return new CurveArgs { Mode = CurveMode.Power, Gain = gain, CapMode = cap, Scale = 0.8, ExponentPower = 0.3, OutputOffset = 0.5, CapX = 30, CapY = 1.6 };
                }
                yield return new CurveArgs { Mode = CurveMode.Classic, Gain = gain, CapMode = CapType.Output, CapY = 0, Acceleration = 0.01, ExponentClassic = 2.5 };
                yield return new CurveArgs { Mode = CurveMode.Natural, Gain = gain, Limit = 1.8, DecayRate = 0.12, InputOffset = 3 };
                yield return new CurveArgs { Mode = CurveMode.Jump, Gain = gain, CapX = 12, CapY = 1.5, Smooth = 0.5 };
                yield return new CurveArgs { Mode = CurveMode.Synchronous, Gain = gain, SyncSpeed = 8, Motivity = 1.6, Gamma = 1, Smooth = 0.5 };
                foreach (bool smooth in new[] { false, true })
                {
                    yield return new CurveArgs
                    {
                        Mode = CurveMode.Lookup,
                        Gain = gain,
                        SmoothLut = smooth,
                        ControlPoints = new List<Point2> { new Point2(1, 1), new Point2(5, 1.1), new Point2(15, 1.4), new Point2(40, 1.8), new Point2(80, 1.9) },
                    };
                }
                yield return new CurveArgs { Mode = CurveMode.NoAccel, Gain = gain };
            }
        }

        [TestMethod]
        public void EditorPreviewMatchesDriver()
        {
            const int counts = 100;
            var speeds = Enumerable.Range(0, 40).Select(i => 0.2 * Math.Pow(10, i * 3.0 / 39)).ToArray();
            var failures = new List<string>();

            foreach (var args in Cases())
            {
                var profile = new Profile();
                profile.argsX = ArgsMapper.ToAccelArgs(args, profile.argsX);
                var accel = new ManagedAccel(profile).CreateStatelessCopy();
                var preview = CurveMath.Create(args);

                foreach (double speed in speeds)
                {
                    double time = counts / speed;
                    double driver = accel.Accelerate(counts, 0, 1, time).Item1 / counts;
                    double expected = preview.Sensitivity(speed);
                    if (!(Math.Abs(driver - expected) <= 1e-9 + 1e-5 * Math.Abs(expected)))
                    {
                        failures.Add($"{args.Mode} gain={args.Gain} cap={args.CapMode} speed={speed:G4}: driver {driver}, preview {expected}");
                    }
                }
            }

            Assert.AreEqual(0, failures.Count, string.Join(Environment.NewLine, failures.Take(20)));
        }

        [TestMethod]
        public void MapperRoundTripKeepsCurve()
        {
            foreach (var args in Cases())
            {
                var profile = new Profile();
                profile.argsX = ArgsMapper.ToAccelArgs(args, profile.argsX);
                var back = ArgsMapper.FromAccelArgs(profile.argsX, null);

                var a = CurveMath.Create(args);
                var b = CurveMath.Create(back);
                for (double x = 0.25; x < 200; x *= 1.3)
                {
                    Assert.AreEqual(a.Sensitivity(x), b.Sensitivity(x), 1e-6 * Math.Max(1, Math.Abs(a.Sensitivity(x))), $"{args.Mode} gain={args.Gain} x={x}");
                }
            }
        }
    }
}

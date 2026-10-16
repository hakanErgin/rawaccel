using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CurveEditor.Core.Curves;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;

namespace CurveEditor.Core.Tests
{
    /// <summary>
    /// Compares the C# port against values produced by the driver's own C++ accel
    /// implementations (see NativeReference/gen.cpp).
    /// </summary>
    [TestClass]
    public class CurveMathParityTests
    {
        private sealed class Case
        {
            public CurveArgs Args { get; set; }
            public List<double[]> Lut { get; set; }
            public List<double[]> Samples { get; set; }
        }

        private static List<Case> LoadCases()
        {
            var path = Path.Combine(AppContext.BaseDirectory, "NativeReference", "native-reference.json");
            return JsonConvert.DeserializeObject<List<Case>>(File.ReadAllText(path));
        }

        [TestMethod]
        public void MatchesNativeImplementation()
        {
            var cases = LoadCases();
            Assert.IsTrue(cases.Count > 100);

            var failures = new List<string>();

            foreach (var c in cases)
            {
                ICurve curve = c.Args.Mode == CurveMode.Lookup
                    ? new Lookup(c.Lut.Select(p => new Point2(p[0], p[1])).ToArray(), c.Args.Gain)
                    : CurveMath.Create(c.Args);

                foreach (var s in c.Samples)
                {
                    double expected = s[1];
                    double actual = curve.Sensitivity(s[0]);
                    double tol = 1e-9 + 1e-7 * Math.Abs(expected);
                    if (!(Math.Abs(actual - expected) <= tol))
                    {
                        failures.Add($"{c.Args.Mode} gain={c.Args.Gain} cap={c.Args.CapMode} capY={c.Args.CapY} x={s[0]}: native {expected}, C# {actual}");
                    }
                }
            }

            Assert.AreEqual(0, failures.Count, string.Join(Environment.NewLine, failures.Take(20)));
        }

        [TestMethod]
        public void LookupBuiltFromControlPointsMatchesNativeTable()
        {
            foreach (var c in LoadCases().Where(c => c.Args.Mode == CurveMode.Lookup))
            {
                var table = c.Lut.Select(p => new Point2(p[0], p[1])).ToList();
                var args = c.Args.Clone();
                args.ControlPoints = LutBuilder.ControlPointsFromLut(table, args.Gain);

                Assert.IsTrue(CustomCurveSidecar.SameTable(args.BuildLut(), table), "control point round trip changed the table");

                var curve = CurveMath.Create(args);
                foreach (var s in c.Samples)
                {
                    Assert.AreEqual(s[1], curve.Sensitivity(s[0]), 1e-9 + 1e-7 * Math.Abs(s[1]));
                }
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace CurveEditor.Core
{
    /// <summary>
    /// The driver only stores the sampled lookup table. This file keeps the editable control
    /// points (and the smoothing choice) per profile, so a smoothed custom curve reopens with
    /// its handles instead of 257 table points.
    /// </summary>
    public sealed class CustomCurveSidecar
    {
        public const string DefaultFileName = "curve-editor.json";

        [JsonProperty("profiles")]
        public Dictionary<string, ProfileEntry> Profiles { get; set; } = new Dictionary<string, ProfileEntry>();

        public sealed class CurveEntry
        {
            [JsonProperty("smooth")]
            public bool Smooth { get; set; }

            /// <summary> [speed, sensitivity] pairs. </summary>
            [JsonProperty("points")]
            public List<double[]> Points { get; set; } = new List<double[]>();
        }

        public sealed class ProfileEntry
        {
            [JsonProperty("x", NullValueHandling = NullValueHandling.Ignore)]
            public CurveEntry X { get; set; }

            [JsonProperty("y", NullValueHandling = NullValueHandling.Ignore)]
            public CurveEntry Y { get; set; }
        }

        public static CustomCurveSidecar Load(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    return JsonConvert.DeserializeObject<CustomCurveSidecar>(File.ReadAllText(path)) ?? new CustomCurveSidecar();
                }
            }
            catch (Exception e) when (e is JsonException || e is IOException || e is UnauthorizedAccessException)
            {
                // a broken sidecar only loses smoothing metadata; fall back to the raw table
            }
            return new CustomCurveSidecar();
        }

        public void Save(string path) => File.WriteAllText(path, JsonConvert.SerializeObject(this, Formatting.Indented));

        public static CurveEntry ToEntry(CurveArgs a) => new CurveEntry
        {
            Smooth = a.SmoothLut,
            Points = a.ControlPoints.Select(p => new[] { p.X, p.Y }).ToList(),
        };

        /// <summary>
        /// Fills <paramref name="args"/>.ControlPoints/SmoothLut from the driver table, using the
        /// saved control points only if they still reproduce that table.
        /// </summary>
        public static void Restore(CurveArgs args, IReadOnlyList<Point2> driverLut, CurveEntry saved)
        {
            if (saved != null && saved.Points.Count >= 2 && saved.Points.All(p => p != null && p.Length == 2))
            {
                var cps = saved.Points.Select(p => new Point2(p[0], p[1])).ToList();
                var rebuilt = LutBuilder.Build(cps, saved.Smooth, args.Gain);
                if (SameTable(rebuilt, driverLut))
                {
                    args.ControlPoints = cps;
                    args.SmoothLut = saved.Smooth;
                    return;
                }
            }

            args.ControlPoints = LutBuilder.ControlPointsFromLut(driverLut, args.Gain);
            args.SmoothLut = false;
        }

        /// <summary> Tables are equal as the driver sees them (float precision). </summary>
        public static bool SameTable(IReadOnlyList<Point2> a, IReadOnlyList<Point2> b)
        {
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++)
            {
                if ((float)a[i].X != (float)b[i].X || (float)a[i].Y != (float)b[i].Y) return false;
            }
            return true;
        }
    }
}

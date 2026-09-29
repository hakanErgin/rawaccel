using System;
using System.Collections.Generic;
using System.Linq;

namespace CurveEditor.Core
{
    /// <summary> Mirrors ra::accel_mode (common/rawaccel-base.hpp); order matters. </summary>
    public enum CurveMode
    {
        Classic,
        Jump,
        Natural,
        Synchronous,
        Power,
        Lookup,
        NoAccel,
    }

    /// <summary> Mirrors ra::cap_mode; order matters. </summary>
    public enum CapType
    {
        InOut,
        Input,
        Output,
    }

    public readonly struct Point2 : IEquatable<Point2>
    {
        public Point2(double x, double y)
        {
            X = x;
            Y = y;
        }

        public double X { get; }

        public double Y { get; }

        public bool Equals(Point2 other) => X.Equals(other.X) && Y.Equals(other.Y);

        public override bool Equals(object obj) => obj is Point2 p && Equals(p);

        public override int GetHashCode() => (X, Y).GetHashCode();

        public override string ToString() => FormattableString.Invariant($"({X:G6}, {Y:G6})");

        public static bool operator ==(Point2 a, Point2 b) => a.Equals(b);

        public static bool operator !=(Point2 a, Point2 b) => !a.Equals(b);
    }

    /// <summary>
    /// Managed mirror of ra::accel_args. Defaults match the native defaults.
    /// For lookup mode, the curve is described by <see cref="ControlPoints"/> in
    /// sensitivity space; <see cref="BuildLut"/> produces the table written to the driver.
    /// </summary>
    public sealed class CurveArgs : IEquatable<CurveArgs>
    {
        public const int MaxLutPoints = 257;

        public CurveMode Mode { get; set; } = CurveMode.NoAccel;

        /// <summary> Gain (true) or legacy (false). For lookup mode: velocity (true) or sensitivity (false) table. </summary>
        public bool Gain { get; set; } = true;

        public double InputOffset { get; set; } = 0;
        public double OutputOffset { get; set; } = 0;
        public double Acceleration { get; set; } = 0.005;
        public double DecayRate { get; set; } = 0.1;
        public double Gamma { get; set; } = 1;
        public double Motivity { get; set; } = 1.5;
        public double ExponentClassic { get; set; } = 2;
        public double Scale { get; set; } = 1;
        public double ExponentPower { get; set; } = 0.05;
        public double Limit { get; set; } = 1.5;
        public double SyncSpeed { get; set; } = 5;
        public double Smooth { get; set; } = 0.5;
        public double CapX { get; set; } = 15;
        public double CapY { get; set; } = 1.5;
        public CapType CapMode { get; set; } = CapType.Output;

        /// <summary> Lookup mode: editable points as (input speed, sensitivity). </summary>
        public List<Point2> ControlPoints { get; set; } = new List<Point2>();

        /// <summary> Lookup mode: sample a shape-preserving spline through the control points instead of joining them with straight lines. </summary>
        public bool SmoothLut { get; set; }

        public CurveArgs Clone()
        {
            var c = (CurveArgs)MemberwiseClone();
            c.ControlPoints = new List<Point2>(ControlPoints);
            return c;
        }

        /// <summary> Builds the lookup table (x, y) pairs in the driver's representation (velocity or sensitivity per <see cref="Gain"/>). </summary>
        public Point2[] BuildLut() => LutBuilder.Build(ControlPoints, SmoothLut, Gain);

        public bool Equals(CurveArgs o)
        {
            if (o is null) return false;
            if (ReferenceEquals(this, o)) return true;
            return Mode == o.Mode && Gain == o.Gain &&
                InputOffset == o.InputOffset && OutputOffset == o.OutputOffset &&
                Acceleration == o.Acceleration && DecayRate == o.DecayRate &&
                Gamma == o.Gamma && Motivity == o.Motivity &&
                ExponentClassic == o.ExponentClassic && Scale == o.Scale &&
                ExponentPower == o.ExponentPower && Limit == o.Limit &&
                SyncSpeed == o.SyncSpeed && Smooth == o.Smooth &&
                CapX == o.CapX && CapY == o.CapY && CapMode == o.CapMode &&
                SmoothLut == o.SmoothLut &&
                ControlPoints.SequenceEqual(o.ControlPoints);
        }

        public override bool Equals(object obj) => Equals(obj as CurveArgs);

        public override int GetHashCode() => (Mode, Gain, CapX, CapY, ControlPoints.Count).GetHashCode();
    }
}

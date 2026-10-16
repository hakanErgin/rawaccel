using CurveEditor.Core;
using CurveEditor.Services;

namespace CurveEditor.Models
{
    /// <summary>
    /// Everything the editor lets you change in one profile. Snapshots of this are the unit of
    /// undo/redo, so treat instances as immutable once handed out (use <see cref="Clone"/>).
    /// </summary>
    public sealed class ProfileState
    {
        public CurveArgs X { get; set; } = new CurveArgs();
        public CurveArgs Y { get; set; } = new CurveArgs();

        /// <summary> By-component mode: separate horizontal and vertical curves. </summary>
        public bool Separate { get; set; }

        public double Sensitivity { get; set; } = 1;
        public double YXRatio { get; set; } = 1;
        public double Rotation { get; set; }
        public double Snap { get; set; }
        public double SpeedCap { get; set; }
        public double LRRatio { get; set; } = 1;
        public double UDRatio { get; set; } = 1;
        public double DomainX { get; set; } = 1;
        public double DomainY { get; set; } = 1;
        public double RangeX { get; set; } = 1;
        public double RangeY { get; set; } = 1;
        public double LpNorm { get; set; } = 2;
        public double InputSmoothHalflife { get; set; }
        public double ScaleSmoothHalflife { get; set; }
        public double OutputSmoothHalflife { get; set; }

        public ProfileState Clone()
        {
            var c = (ProfileState)MemberwiseClone();
            c.X = X.Clone();
            c.Y = Y.Clone();
            return c;
        }

        public bool SameAs(ProfileState o) =>
            o != null && X.Equals(o.X) && Y.Equals(o.Y) && Separate == o.Separate &&
            Sensitivity == o.Sensitivity && YXRatio == o.YXRatio && Rotation == o.Rotation &&
            Snap == o.Snap && SpeedCap == o.SpeedCap && LRRatio == o.LRRatio && UDRatio == o.UDRatio &&
            DomainX == o.DomainX && DomainY == o.DomainY && RangeX == o.RangeX && RangeY == o.RangeY &&
            LpNorm == o.LpNorm && InputSmoothHalflife == o.InputSmoothHalflife &&
            ScaleSmoothHalflife == o.ScaleSmoothHalflife && OutputSmoothHalflife == o.OutputSmoothHalflife;

        public static ProfileState FromProfile(Profile p, CustomCurveSidecar.ProfileEntry saved) => new ProfileState
        {
            X = ArgsMapper.FromAccelArgs(p.argsX, saved?.X),
            Y = ArgsMapper.FromAccelArgs(p.argsY, saved?.Y),
            Separate = !p.inputSpeedArgs.combineMagnitudes,
            Sensitivity = p.outputDPI / 1000,
            YXRatio = p.yxOutputDPIRatio,
            Rotation = p.rotation,
            Snap = p.snap,
            SpeedCap = p.maximumSpeed,
            LRRatio = p.lrOutputDPIRatio,
            UDRatio = p.udOutputDPIRatio,
            DomainX = p.domainXY.x,
            DomainY = p.domainXY.y,
            RangeX = p.rangeXY.x,
            RangeY = p.rangeXY.y,
            LpNorm = p.inputSpeedArgs.lpNorm,
            InputSmoothHalflife = p.inputSpeedArgs.inputSmoothHalflife,
            ScaleSmoothHalflife = p.inputSpeedArgs.scaleSmoothHalflife,
            OutputSmoothHalflife = p.inputSpeedArgs.outputSmoothHalflife,
        };

        /// <summary> Writes into the profile in place; fields the editor doesn't know about are left alone. </summary>
        public void WriteTo(Profile p)
        {
            p.argsX = ArgsMapper.ToAccelArgs(X, p.argsX);
            p.argsY = ArgsMapper.ToAccelArgs(Y, p.argsY);
            p.inputSpeedArgs.combineMagnitudes = !Separate;
            p.outputDPI = Sensitivity * 1000;
            p.yxOutputDPIRatio = YXRatio;
            p.rotation = Rotation;
            p.snap = Snap;
            p.maximumSpeed = SpeedCap;
            p.lrOutputDPIRatio = LRRatio;
            p.udOutputDPIRatio = UDRatio;
            p.domainXY = new Vec2<double> { x = DomainX, y = DomainY };
            p.rangeXY = new Vec2<double> { x = RangeX, y = RangeY };
            p.inputSpeedArgs.lpNorm = LpNorm;
            p.inputSpeedArgs.inputSmoothHalflife = InputSmoothHalflife;
            p.inputSpeedArgs.scaleSmoothHalflife = ScaleSmoothHalflife;
            p.inputSpeedArgs.outputSmoothHalflife = OutputSmoothHalflife;
        }

        public CustomCurveSidecar.ProfileEntry ToSidecarEntry()
        {
            var entry = new CustomCurveSidecar.ProfileEntry();
            if (X.Mode == CurveMode.Lookup) entry.X = CustomCurveSidecar.ToEntry(X);
            if (Y.Mode == CurveMode.Lookup) entry.Y = CustomCurveSidecar.ToEntry(Y);
            return entry.X == null && entry.Y == null ? null : entry;
        }
    }
}

using System.Collections.Generic;

namespace CurveEditor.Core.Handles
{
    public enum HandleConstraint
    {
        /// <summary> Moves in both axes. </summary>
        Free,
        /// <summary> Only the x coordinate is meaningful; y follows the curve or stays fixed. </summary>
        XOnly,
        /// <summary> Only the y coordinate is meaningful. </summary>
        YOnly,
    }

    public enum HandleKind
    {
        Point,
        /// <summary> Represents a horizontal level (limit, cap); drawn with a dashed guide across the chart. </summary>
        Level,
    }

    public sealed class Handle
    {
        public Handle(string id, string label, Point2 position, HandleConstraint constraint, string description, params string[] affects)
        {
            Id = id;
            Label = label;
            Position = position;
            Constraint = constraint;
            Description = description;
            Affects = affects;
        }

        public string Id { get; }

        public string Label { get; }

        public Point2 Position { get; }

        public HandleConstraint Constraint { get; }

        public HandleKind Kind { get; set; } = HandleKind.Point;

        public string Description { get; }

        /// <summary> Names of <see cref="CurveArgs"/> properties this handle changes (for readouts). </summary>
        public IReadOnlyList<string> Affects { get; }
    }

    /// <summary>
    /// View-dependent placement hints. Handles that have no natural x position (for example
    /// the "point" handle of an uncapped curve or a limit level) are placed at these speeds,
    /// which the UI derives from the visible x range so the handles stay on screen.
    /// </summary>
    public sealed class HandleContext
    {
        public HandleContext(double anchorX, double edgeX)
        {
            AnchorX = anchorX;
            EdgeX = edgeX;
        }

        /// <summary> Speed used for on-curve handles without a natural position (about 60% across the view). </summary>
        public double AnchorX { get; }

        /// <summary> Speed used for level handles (near the right edge of the view). </summary>
        public double EdgeX { get; }

        public static HandleContext Default { get; } = new HandleContext(30, 70);
    }

    public interface IHandleSet
    {
        /// <summary>
        /// Rewrites args into the form this editor manipulates (for example converting cap modes)
        /// without changing the curve.
        /// </summary>
        CurveArgs Normalize(CurveArgs args);

        IReadOnlyList<Handle> GetHandles(CurveArgs args, HandleContext ctx);

        /// <summary> Returns new args with the handle moved as close to <paramref name="target"/> as the mode allows. Never mutates the input. </summary>
        CurveArgs Drag(CurveArgs args, string handleId, Point2 target, HandleContext ctx);
    }
}

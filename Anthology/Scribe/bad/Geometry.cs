using System;
using System.Collections.Generic;
using System.Numerics;

namespace Scribe.Internal
{
    struct FUnit
    {
        int value;

        public static explicit operator int(FUnit v) => v.value;
        public static explicit operator FUnit(int v) => new FUnit { value = v };

        public static FUnit operator -(FUnit lhs, FUnit rhs) => (FUnit)(lhs.value - rhs.value);
        public static FUnit operator +(FUnit lhs, FUnit rhs) => (FUnit)(lhs.value + rhs.value);
        public static float operator *(FUnit lhs, float rhs) => lhs.value * rhs;

        public static FUnit Max(FUnit a, FUnit b) => (FUnit)Math.Max(a.value, b.value);
        public static FUnit Min(FUnit a, FUnit b) => (FUnit)Math.Min(a.value, b.value);
    }

    struct Point
    {
        public FUnit X;
        public FUnit Y;
        public PointType Type;

        public Point(FUnit x, FUnit y)
        {
            X = x;
            Y = y;
            Type = PointType.OnCurve;
        }

        public static PointF operator *(Point lhs, float rhs) => new PointF(new Vector2(lhs.X * rhs, lhs.Y * rhs), lhs.Type);

        public static explicit operator Vector2(Point p) => new Vector2((int)p.X, (int)p.Y);
    }

    struct PointF
    {
        public Vector2 P;
        public PointType Type;

        public PointF(Vector2 position, PointType type)
        {
            P = position;
            Type = type;
        }

        public PointF Offset(Vector2 offset) => new PointF(P + offset, Type);

        public override string ToString() => $"{P} ({Type})";

        public static implicit operator Vector2(PointF p) => p.P;
    }

    enum PointType
    {
        OnCurve,
        Quadratic,
        Cubic
    }

    static class Geometry
    {
        public static void ComposeGlyphs(int glyphIndex, int startPoint, ref Matrix3x2 transform, List<PointF> basePoints, List<int> baseContours, BaseGlyph[] glyphTable)
        {
            var glyph = glyphTable[glyphIndex];
            var simple = glyph as SimpleGlyph;
            if (simple != null)
            {
                foreach (var endpoint in simple.ContourEndpoints)
                    baseContours.Add(endpoint + startPoint);
                foreach (var point in simple.Points)
                    basePoints.Add(new PointF(Vector2.TransformNormal((Vector2)point, transform), point.Type));
            }
            else
            {
                // otherwise, we have a composite glyph
                var composite = (CompositeGlyph)glyph;
                foreach (var subglyph in composite.Subglyphs)
                {
                    // if we have a scale, update the local transform
                    var local = transform;
                    bool haveScale = (subglyph.Flags & (CompositeGlyphFlags.HaveScale | CompositeGlyphFlags.HaveXYScale | CompositeGlyphFlags.HaveTransform)) != 0;
                    if (haveScale)
                        local = transform * subglyph.Transform;

                    // recursively compose the subglyph into our lists
                    int currentPoints = basePoints.Count;
                    ComposeGlyphs(subglyph.Index, currentPoints, ref local, basePoints, baseContours, glyphTable);

                    // calculate the offset for the subglyph. we have to do offsetting after composing all subglyphs,
                    // because we might need to find the offset based on previously composed points by index
                    Vector2 offset;
                    if ((subglyph.Flags & CompositeGlyphFlags.ArgsAreXYValues) != 0)
                    {
                        offset = (Vector2)new Point((FUnit)subglyph.Arg1, (FUnit)subglyph.Arg2);
                        if (haveScale && (subglyph.Flags & CompositeGlyphFlags.ScaledComponentOffset) != 0)
                            offset = Vector2.TransformNormal(offset, local);
                        else
                            offset = Vector2.TransformNormal(offset, transform);

                        // if the RoundXYToGrid flag is set, round the offset components
                        if ((subglyph.Flags & CompositeGlyphFlags.RoundXYToGrid) != 0)
                            offset = new Vector2((float)Math.Round(offset.X), (float)Math.Round(offset.Y));
                    }
                    else
                    {
                        // if the offsets are not given in FUnits, then they are point indices
                        // in the currently composed base glyph that we should match up
                        var p1 = basePoints[(int)((uint)subglyph.Arg1 + startPoint)];
                        var p2 = basePoints[(int)((uint)subglyph.Arg2 + currentPoints)];
                        offset = p1.P - p2.P;
                    }

                    // translate all child points
                    if (offset != Vector2.Zero)
                    {
                        for (int i = currentPoints; i < basePoints.Count; i++)
                            basePoints[i] = basePoints[i].Offset(offset);
                    }
                }
            }
        }

        public static void DecomposeContour(Renderer renderer, int firstIndex, int lastIndex, PointF[] points)
        {
            var pointIndex = firstIndex;
            var start = points[pointIndex];
            var end = points[lastIndex];
            var control = start;

            if (start.Type == PointType.Cubic)
                throw new InvalidFontException("Contours can't start with a cubic control point.");

            if (start.Type == PointType.Quadratic)
            {
                // if first point is a control point, try using the last point
                if (end.Type == PointType.OnCurve)
                {
                    start = end;
                    lastIndex--;
                }
                else
                {
                    // if they're both control points, start at the middle
                    start.P = (start.P + end.P) / 2;
                }
                pointIndex--;
            }

            // let's draw this contour
            renderer.MoveTo(start);

            var needClose = true;
            while (pointIndex < lastIndex)
            {
                var point = points[++pointIndex];
                switch (point.Type)
                {
                    case PointType.OnCurve:
                        renderer.LineTo(point);
                        break;

                    case PointType.Quadratic:
                        control = point;
                        var done = false;
                        while (pointIndex < lastIndex)
                        {
                            var next = points[++pointIndex];
                            if (next.Type == PointType.OnCurve)
                            {
                                renderer.QuadraticCurveTo(control, next);
                                done = true;
                                break;
                            }

                            if (next.Type != PointType.Quadratic)
                                throw new InvalidFontException("Bad outline data.");

                            renderer.QuadraticCurveTo(control, (control.P + next.P) / 2);
                            control = next;
                        }

                        if (!done)
                        {
                            // if we hit this point, we're ready to close out the contour
                            renderer.QuadraticCurveTo(control, start);
                            needClose = false;
                        }
                        break;

                    case PointType.Cubic:
                        throw new NotSupportedException();
                }
            }

            if (needClose)
                renderer.LineTo(start);
        }
    }
}
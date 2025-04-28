using Prowl.Vector;

namespace Prowl.Quill;

public enum JointStyle { Bevel, Miter, Round }

public enum EndCapStyle { Butt, Square, Round, Bevel  }

internal static class PolylineMesher
{
    [ThreadStatic]
    private static List<Triangle> _triangles = new List<Triangle>();
    [ThreadStatic]
    private static List<PolySegment> _polySegments = new List<PolySegment>();

    private static List<Triangle> TriangleCache => _triangles ??= new List<Triangle>();
    private static List<PolySegment> PolySegmentCache => _polySegments ??= new List<PolySegment>();

    private const double MiterMinAngle = 20.0f * Math.PI / 180.0f; // ~20 degrees
    private const double RoundMinAngle = 40.0f * Math.PI / 180.0f; // ~40 degrees
    private static readonly Vector2 HalfPixel = new Vector2(0.5f, 0.5f);

    /// <summary> A triangle defined by three vertices </summary>
    public struct Triangle
    {
        public Vector2 V1;
        public Vector2 V2;
        public Vector2 V3;
        public Vector2 UV1;
        public Vector2 UV2;
        public Vector2 UV3;
        public System.Drawing.Color Color;

        public Triangle(Vector2 v1, Vector2 v2, Vector2 v3, Vector2 uv1, Vector2 uv2, Vector2 uv3, System.Drawing.Color col)
        {
            V1 = v1;
            V2 = v2;
            V3 = v3;
            UV1 = uv1;
            UV2 = uv2;
            UV3 = uv3;
            Color = col;
        }
    }

    /// <summary>
    /// Creates a list of triangles describing a solid path through the input points.
    /// </summary>
    /// <param name="points">The points of the path</param>
    /// <param name="thickness">The path's thickness</param>
    /// <param name="color">The path's color</param>
    /// <param name="jointStyle">The path's joint style</param>
    /// <param name="miterLimit">The miter limit (used when jointStyle is Miter)</param>
    /// <param name="allowOverlap">Whether to allow overlapping vertices for better results with close points</param>
    /// <returns>A list of triangles describing the path</returns>
    public static IReadOnlyList<Triangle> Create(List<Vector2> points, double thickness, double pixelWidth, System.Drawing.Color color, JointStyle jointStyle = JointStyle.Miter, double miterLimit = 4.0f, bool allowOverlap = false, EndCapStyle startCap = EndCapStyle.Butt, EndCapStyle endCap = EndCapStyle.Butt)
    {
        // Reset caches
        PolySegmentCache.Clear();
        TriangleCache.Clear();

        // Early exit conditions
        if (points.Count < 2 || thickness <= 0 || color.A == 0)
            return TriangleCache;

        // Handle thin lines with alpha adjustment instead of thickness reduction
        if (thickness < 1.0)
        {
            color = System.Drawing.Color.FromArgb(
                (int)(color.A * (thickness / 1.0)),
                color.R,
                color.G,
                color.B
            );

            thickness = 1.0;
        }

        // Expand the thickness to account for Anti-Aliasing
        thickness += pixelWidth;

        // Half thickness for calculations
        double halfThickness = thickness / 2;

        // Check if path is closed
        bool isClosed = points[0] == points[^1];

        // Create line segments, skipping identical consecutive points
        for (int i = 0; i < points.Count - 1; i++)
        {
            Vector2 p1 = points[i];
            Vector2 p2 = points[i + 1];

            if (p1 != p2)
                PolySegmentCache.Add(new(new(p1 + HalfPixel, p2 + HalfPixel), halfThickness));
        }

        // Tracking variables for segment connections
        Vector2 nextStart1 = Vector2.zero, nextStart2 = Vector2.zero;
        Vector2 start1 = Vector2.zero, start2 = Vector2.zero;
        Vector2 end1 = Vector2.zero, end2 = Vector2.zero;
        Vector2 firstStart1 = Vector2.zero, firstStart2 = Vector2.zero;

        // UV tracking variables
        Vector2 nextStartUV1 = Vector2.zero, nextStartUV2 = Vector2.zero;
        Vector2 startUV1 = Vector2.zero, startUV2 = Vector2.zero;
        Vector2 endUV1 = Vector2.zero, endUV2 = Vector2.zero;
        Vector2 firstStartUV1 = Vector2.zero, firstStartUV2 = Vector2.zero;

        // Process each segment
        for (int i = 0; i < PolySegmentCache.Count; i++)
        {
            var segment = PolySegmentCache[i];

            // Determine UV coordinates based on caps and path closure
            double startU = 0.5;
            double endU = 0.5;
            if (!isClosed)
            {
                if (PolySegmentCache.Count == 1)
                {
                    startU = startCap != EndCapStyle.Butt ? 0.5 : 0.0;
                    endU = endCap != EndCapStyle.Butt ? 0.5 : 1.0;
                }
                else if (i == 0)
                    startU = startCap != EndCapStyle.Butt ? 0.5 : 0.0;
                else if (i == PolySegmentCache.Count - 1)
                    endU = endCap != EndCapStyle.Butt ? 0.5 : 0.0;
            }

            // Handle first segment
            if (i == 0)
            {
                start1 = segment.Edge1.A;
                start2 = segment.Edge2.A;
                startUV1 = new Vector2(startU, 0);
                startUV2 = new Vector2(startU, 1);

                // Store for closed paths
                firstStart1 = start1;
                firstStart2 = start2;
                firstStartUV1 = startUV1;
                firstStartUV2 = startUV2;
            }
            else
            {
                start1 = nextStart1;
                start2 = nextStart2;
                startUV1 = nextStartUV1;
                startUV2 = nextStartUV2;
            }

            // Handle last segment
            if (i + 1 == PolySegmentCache.Count)
            {
                if (isClosed)
                {
                    // Create joint between last and first segment
                    CreateJoint(TriangleCache, color, segment, PolySegmentCache[0], thickness, jointStyle, miterLimit,
                        ref end1, ref end2, ref nextStart1, ref nextStart2,
                        startU, endU,
                        ref endUV1, ref endUV2, ref nextStartUV1, ref nextStartUV2,
                        allowOverlap);

                    // Close the path by connecting back to start
                    TriangleCache.Add(new Triangle(end1, firstStart1, end2, endUV1, firstStartUV1, endUV2, color));
                    TriangleCache.Add(new Triangle(end2, firstStart1, firstStart2, endUV2, firstStartUV1, firstStartUV2, color));
                }
                else
                {
                    // For open paths, use segment end points
                    end1 = segment.Edge1.B;
                    end2 = segment.Edge2.B;
                    endUV1 = new Vector2(endU, 0);
                    endUV2 = new Vector2(endU, 1);
                }
            }
            else
            {
                // Create joint with next segment
                CreateJoint(TriangleCache, color, segment, PolySegmentCache[i + 1], thickness, jointStyle, miterLimit,
                    ref end1, ref end2, ref nextStart1, ref nextStart2,
                    startU, endU,
                    ref endUV1, ref endUV2, ref nextStartUV1, ref nextStartUV2,
                    allowOverlap);
            }

            // Add segment triangles
            TriangleCache.Add(new Triangle(start1, end1, start2, startUV1, endUV1, startUV2, color));
            TriangleCache.Add(new Triangle(start2, end1, end2, startUV2, endUV1, endUV2, color));
        }

        // Add end caps for open paths
        if (!isClosed)
        {
            AddEndCap(TriangleCache, color, PolySegmentCache[0], halfThickness, startCap, true);
            AddEndCap(TriangleCache, color, PolySegmentCache[^1], halfThickness, endCap, false);
        }

        return TriangleCache;
    }

    private static void CreateJoint(List<Triangle> triangles, System.Drawing.Color color, PolySegment segment1, PolySegment segment2, double thickness, JointStyle jointStyle, double miterLimit, ref Vector2 end1, ref Vector2 end2, ref Vector2 nextStart1, ref Vector2 nextStart2, double startU, double endU, ref Vector2 endUV1, ref Vector2 endUV2, ref Vector2 nextStartUV1, ref Vector2 nextStartUV2, bool allowOverlap)
    {
        // Calculate the angle between the two line segments
        Vector2 dir1 = segment1.Center.Direction;
        Vector2 dir2 = segment2.Center.Direction;

        double angle = Angle(dir1, dir2);

        // Wrap the angle around the 180° mark if it exceeds 90°
        double wrappedAngle = angle;
        //if (wrappedAngle > PI / 2)
        //    wrappedAngle = PI - wrappedAngle;

        // Check if we need to use bevel join instead of miter
        if (jointStyle == JointStyle.Miter && (wrappedAngle < MiterMinAngle || 1 / Math.Sin(wrappedAngle / 2) > miterLimit))
        {
            // If the angle is too small or the miter length would exceed the miter limit
            jointStyle = JointStyle.Bevel;
        }

        if (jointStyle == JointStyle.Miter)
        {
            // Calculate intersection points for miter joins
            Vector2? sec1 = LineSegment.Intersection(segment1.Edge1, segment2.Edge1, true);
            Vector2? sec2 = LineSegment.Intersection(segment1.Edge2, segment2.Edge2, true);

            end1 = sec1 ?? segment1.Edge1.B;
            end2 = sec2 ?? segment1.Edge2.B;

            nextStart1 = end1;
            nextStart2 = end2;

            // Set UV coordinates
            endUV1 = new Vector2(endU, 0);
            endUV2 = new Vector2(endU, 1);
            nextStartUV1 = endUV1;
            nextStartUV2 = endUV2;
        }
        else
        {
            // Joint style is BEVEL or ROUND

            // Determine inner and outer edges
            double crossProduct = dir1.x * dir2.y - dir2.x * dir1.y;
            bool clockwise = crossProduct < 0;

            LineSegment inner1, inner2, outer1, outer2;

            if (clockwise)
            {
                outer1 = segment1.Edge1;
                outer2 = segment2.Edge1;
                inner1 = segment1.Edge2;
                inner2 = segment2.Edge2;
            }
            else
            {
                outer1 = segment1.Edge2;
                outer2 = segment2.Edge2;
                inner1 = segment1.Edge1;
                inner2 = segment2.Edge1;
            }

            // Calculate the intersection point of the inner edges
            Vector2? innerSecOpt = LineSegment.Intersection(inner1, inner2, allowOverlap);
            Vector2 innerSec = innerSecOpt ?? inner1.B;

            // Determine the inner start position
            Vector2 innerStart;
            if (innerSecOpt.HasValue)
            {
                innerStart = innerSec;
            }
            else if (angle > Math.PI / 2)
            {
                innerStart = outer1.B;
            }
            else
            {
                innerStart = inner1.B;
            }

            // Determine UV coordinates for the join
            Vector2 outerUV, innerUV, nextOuterUV, nextInnerUV;

            if (clockwise)
            {
                // For clockwise turns, top edge is outer
                outerUV = new Vector2(endU, 0);
                innerUV = new Vector2(endU, 1);
                nextOuterUV = new Vector2(endU, 0);
                nextInnerUV = new Vector2(endU, 1);

                end1 = outer1.B;
                end2 = innerSec;
                endUV1 = outerUV;
                endUV2 = innerUV;

                nextStart1 = outer2.A;
                nextStart2 = innerStart;
                nextStartUV1 = nextOuterUV;
                nextStartUV2 = nextInnerUV;
            }
            else
            {
                // For counter-clockwise turns, bottom edge is outer
                outerUV = new Vector2(endU, 1);
                innerUV = new Vector2(endU, 0);
                nextOuterUV = new Vector2(endU, 1);
                nextInnerUV = new Vector2(endU, 0);

                end1 = innerSec;
                end2 = outer1.B;
                endUV1 = innerUV;
                endUV2 = outerUV;

                nextStart1 = innerStart;
                nextStart2 = outer2.A;
                nextStartUV1 = nextInnerUV;
                nextStartUV2 = nextOuterUV;
            }

            if (jointStyle == JointStyle.Bevel)
            {
                // For bevel join, add an additional triangle to connect the edges
                if (clockwise)
                {
                    triangles.Add(new Triangle(
                        outer1.B, outer2.A, innerSec,
                        outerUV, nextOuterUV, innerUV,
                        color
                    ));
                }
                else
                {
                    triangles.Add(new Triangle(
                        outer1.B, innerSec, outer2.A,
                        outerUV, innerUV, nextOuterUV,
                        color
                    ));
                }
            }
            else if (jointStyle == JointStyle.Round)
            {
                // For round join, create a triangle fan with UVs
                CreateTriangleFan(
                    triangles, thickness, color, innerSec, segment1.Center.B, outer1.B, outer2.A,
                    innerUV, outerUV, nextOuterUV,
                    clockwise
                );
            }
        }
    }

    private static void AddEndCap(List<Triangle> triangles, System.Drawing.Color color,
                        PolySegment segment, double thickness,
                        EndCapStyle capStyle, bool isStart)
    {
        // Get the cap position and direction
        Vector2 position = isStart ? segment.Center.A : segment.Center.B;
        Vector2 direction = segment.Center.Direction;

        // Flip direction for start cap
        if (isStart)
            direction = -direction;

        // Get perpendicular vector for the cap width
        Vector2 perpendicular = new Vector2(-direction.y, direction.x);

        // Calculate the cap corners based on the existing edges
        Vector2 edge1Point = isStart ? segment.Edge1.A : segment.Edge1.B;
        Vector2 edge2Point = isStart ? segment.Edge2.A : segment.Edge2.B;

        Vector2 edge1UV = isStart ? new Vector2(0, 0) : new Vector2(1, 0);
        Vector2 edge2UV = isStart ? new Vector2(0, 1) : new Vector2(1, 1);

        // Now add the appropriate cap based on the style
        switch (capStyle)
        {
            case EndCapStyle.Butt:
                // Butt cap is just the line ends, no extra triangles needed
                break;

            case EndCapStyle.Square:
            {
                // Extend the corners by half thickness in the direction of the segment
                Vector2 extension = direction * thickness;
                Vector2 corner1 = edge1Point + extension;
                Vector2 corner2 = edge2Point + extension;

                // UVs for extended corners
                Vector2 edgeUV1 = new Vector2(0.5, 0);
                Vector2 edgeUV2 = new Vector2(0.5, 1);
                Vector2 cornerUV1 = new Vector2(isStart ? 0 : 1, 0);
                Vector2 cornerUV2 = new Vector2(isStart ? 0 : 1, 1);

                if (isStart)
                {
                    triangles.Add(new Triangle(
                        edge1Point, edge2Point, corner1,
                        edgeUV1, edgeUV2, cornerUV1,
                        color
                    ));

                    triangles.Add(new Triangle(
                        edge2Point, corner2, corner1,
                        edgeUV2, cornerUV2, cornerUV1,
                        color
                    ));
                }
                else
                {
                    triangles.Add(new Triangle(
                        edge1Point, corner1, edge2Point,
                        edgeUV1, cornerUV1, edgeUV2,
                        color
                    ));

                    triangles.Add(new Triangle(
                        edge2Point, corner1, corner2,
                        edgeUV2, cornerUV1, cornerUV2,
                        color
                    ));
                }
            }
            break;

            case EndCapStyle.Round or EndCapStyle.Bevel:
            {
                // Create a semicircular cap using a triangle fan
                int numSegments = capStyle == EndCapStyle.Bevel ? 2 : 6;
                // More segments for round cap
                if (capStyle == EndCapStyle.Round)
                {
                    double distance = Math.PI * (thickness * 0.5);
                    numSegments  = Math.Min(Math.Max(6, (int)Math.Floor(distance / Canvas.RoundingMinDistance)), 16);

                    thickness += 0.5;
                }
                else
                { 
                    // Havent fully delved into why we need a differant + thickness for Rounded or Bevels.. or why we even need them at all
                    // Defintely something todo with how the UV's are and Anti Aliasing, but this seems to make them seamless sooo for now this works
                    thickness += 1.0;
                }

                // We'll generate points along the semicircle
                for (int i = 0; i < numSegments; i++)
                {
                    // Calculate the angles for this triangle segment
                    double startAngle = Math.PI / 2 - (i * Math.PI / numSegments);
                    double endAngle = Math.PI / 2 - ((i + 1) * Math.PI / numSegments);


                    // Calculate the points on the semicircle
                    Vector2 point1 = position + new Vector2(
                        (float)(Math.Cos(startAngle) * thickness * direction.x - Math.Sin(startAngle) * thickness * perpendicular.x),
                        (float)(Math.Cos(startAngle) * thickness * direction.y - Math.Sin(startAngle) * thickness * perpendicular.y)
                    );

                    Vector2 point2 = position + new Vector2(
                        (float)(Math.Cos(endAngle) * thickness * direction.x - Math.Sin(endAngle) * thickness * perpendicular.x),
                        (float)(Math.Cos(endAngle) * thickness * direction.y - Math.Sin(endAngle) * thickness * perpendicular.y)
                    );

                    // Move back by epsilon to help reduce holes
                    Vector2 offsetCenter = position - (direction * (thickness * 0.01));

                    // Create the triangle with proper UV coordinates
                    triangles.Add(new Triangle(
                        point1, offsetCenter, point2,
                        new(0.0f, 0.0f), new(0.5f, 0.5f), new(0.0f, 0.0f),
                        color
                    ));
                }
            }
            break;
        }
    }

    /// <summary>
    /// Creates a partial circle between two points as a triangle fan with UV coordinates
    /// </summary>
    private static void CreateTriangleFan(List<Triangle> triangles, double thickness, System.Drawing.Color color, Vector2 connectTo, Vector2 origin,
                                         Vector2 start, Vector2 end,
                                         Vector2 centerUV, Vector2 startUV, Vector2 endUV,
                                         bool clockwise)
    {
        Vector2 point1 = start - origin;
        Vector2 point2 = end - origin;

        // Calculate the angle between the two points
        double angle1 = Math.Atan2(point1.y, point1.x);
        double angle2 = Math.Atan2(point2.y, point2.x);

        // Ensure the outer angle is calculated
        if (clockwise)
        {
            if (angle2 > angle1)
                angle2 = angle2 - 2 * Math.PI;
        }
        else
        {
            if (angle1 > angle2)
                angle1 = angle1 - 2 * Math.PI;
        }

        double jointAngle = angle2 - angle1;

        // Calculate the amount of triangles to use for the joint
        int numTriangles = Math.Max(1, (int)Math.Floor(Math.Abs(jointAngle) / RoundMinAngle));

        // Calculate the angle of each triangle
        double triAngle = jointAngle / numTriangles;

        Vector2 startPoint = start;
        Vector2 startPointUV = startUV;
        Vector2 endPoint;
        Vector2 endPointUV;

        // Interpolate UV coordinates along the arc
        double interpolationStep = 1.0f / numTriangles;

        for (int t = 0; t < numTriangles; t++)
        {
            double interpolationFactor = (t + 1) * interpolationStep;

            if (t + 1 == numTriangles)
            {
                // Last triangle - ensure it perfectly connects to the next line
                endPoint = end;
                endPointUV = endUV;
            }
            else
            {
                double rot = angle1 + (t + 1) * triAngle;

                // Rotate around the origin
                endPoint = new Vector2(
                    Math.Cos(rot) * point1.magnitude,
                    Math.Sin(rot) * point1.magnitude
                );

                // Re-add the origin to the target point
                endPoint += origin;

                // Interpolate UV between start and end
                endPointUV = Vector2.Lerp(startUV, endUV, interpolationFactor);
            }

            // Add the triangle to our list with UV coordinates
            if (clockwise)
            {
                // One winding order
                triangles.Add(new Triangle(
                    startPoint, endPoint, connectTo,
                    startPointUV, endPointUV, centerUV,
                    color
                ));
            }
            else
            {
                // Reversed winding order
                triangles.Add(new Triangle(
                    startPoint, connectTo, endPoint,
                    startPointUV, centerUV, endPointUV,
                    color
                ));
            }

            startPoint = endPoint;
            startPointUV = endPointUV;
        }
    }

    #region Helper Classes and Methods

    private struct LineSegment
    {
        public Vector2 A { get; }
        public Vector2 B { get; }

        private Vector2? _cachedDirection;
        public Vector2 Direction => _cachedDirection ??= Vector2.Normalize(B - A);
        private Vector2? _cachedNormal;

        public LineSegment(Vector2 a, Vector2 b)
        {
            A = a;
            B = b;

            _cachedDirection = null;
            _cachedNormal = null;
        }

        public Vector2 Normal => _cachedNormal ??= new Vector2(-Direction.y, Direction.x);

        public static Vector2? Intersection(LineSegment a, LineSegment b, bool infiniteLines)
        {
            // Calculate unnormalized direction vectors
            Vector2 r = a.B - a.A;
            Vector2 s = b.B - b.A;

            Vector2 originDist = b.A - a.A;

            double uNumerator = Cross(originDist, r);
            double denominator = Cross(r, s);

            if (Math.Abs(denominator) < 0.0001f)
            {
                // The lines are parallel
                return null;
            }

            // Solve the intersection positions
            double u = uNumerator / denominator;
            double t = Cross(originDist, s) / denominator;

            if (!infiniteLines && (t < 0 || t > 1 || u < 0 || u > 1))
            {
                // The intersection lies outside of the line segments
                return null;
            }

            // Calculate the intersection point
            return a.A + r * t;
        }

        public static LineSegment operator +(LineSegment segment, Vector2 offset) => new LineSegment(segment.A + offset, segment.B + offset);

        public static LineSegment operator -(LineSegment segment, Vector2 offset) => new LineSegment(segment.A - offset, segment.B - offset);
    }

    private struct PolySegment
    {
        public LineSegment Center { get; }
        public LineSegment Edge1 { get; }
        public LineSegment Edge2 { get; }

        public PolySegment(LineSegment center, double thickness)
        {
            Center = center;

            // Calculate the segment's outer edges by offsetting
            // the central line by the normal vector multiplied with the thickness
            Vector2 normalOffset = center.Normal * thickness;
            Edge1 = center + normalOffset;
            Edge2 = center - normalOffset;
        }
    }

    private static double Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;

    private static double Dot(Vector2 a, Vector2 b) => a.x * b.x + a.y * b.y;

    private static double Angle(Vector2 a, Vector2 b) => Math.Acos(Math.Min(1.0f, Dot(a, b) / (a.magnitude * b.magnitude)));

    #endregion
}
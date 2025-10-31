using Prowl.Vector;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Prowl.Quill
{
    public enum JointStyle { Bevel, Miter, Round }
    public enum EndCapStyle { Butt, Square, Round, Bevel }

    internal static class PolylineMesher
    {
        [ThreadStatic]
        private static List<Triangle> _triangles;
        [ThreadStatic]
        private static List<PolySegment> _polySegments;

        private static List<Triangle> TriangleCache => _triangles ??= new List<Triangle>();
        private static List<PolySegment> PolySegmentCache => _polySegments ??= new List<PolySegment>();

        private const float MiterMinAngle = 20.0f * Maths.PI / 180.0f;
        private const float RoundMinAngle = 40.0f * Maths.PI / 180.0f;
        private const float Epsilon = 1e-6f;
        private const float EpsilonSqr = 1e-9f;
        private static readonly Float2 HalfPixel = new Float2(0.5f, 0.5f);

        public struct Triangle
        {
            public Float2 V1, V2, V3;
            public Float2 UV1, UV2, UV3;
            public Color32 Color;

            public Triangle(Float2 v1, Float2 v2, Float2 v3, Float2 uv1, Float2 uv2, Float2 uv3, Color32 color)
            {
                V1 = v1; V2 = v2; V3 = v3;
                UV1 = uv1; UV2 = uv2; UV3 = uv3;
                Color = color;
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
        public static IReadOnlyList<Triangle> Create(List<Float2> points, float thickness, float pixelWidth, Color32 color, JointStyle jointStyle = JointStyle.Miter, float miterLimit = 4.0f, bool allowOverlap = false, EndCapStyle startCap = EndCapStyle.Butt, EndCapStyle endCap = EndCapStyle.Butt, List<float> dashPattern = null, float dashOffset = 0.0f)
        {
            TriangleCache.Clear();

            if (points.Count < 2 || thickness <= 0 || color.A == 0)
                return TriangleCache;

            // Handle thin lines with alpha adjustment instead of thickness reduction
            if (thickness < 1.0f)
            {
                color = Color32.FromArgb((int)(color.A * thickness), color.R, color.G, color.B);
                thickness = 1.0f;
            }

            thickness += pixelWidth;
            float halfThickness = thickness / 2;

            var dashSegments = GenerateDashSegments(points, dashPattern, dashOffset, HalfPixel);
            if (dashSegments.Count == 0)
                return TriangleCache;

            foreach (var dashPoints in dashSegments)
            {
                if (dashPoints.Count < 2) continue;

                CreatePolySegments(dashPoints, halfThickness);
                if (PolySegmentCache.Count == 0) continue;

                // Check if this is a closed polyline
                bool isClosedPolyline = dashPoints.Count > 2 &&
                    Float2.LengthSquared(dashPoints[0] - dashPoints[^1]) < EpsilonSqr;

                GenerateTrianglesForPolyline(jointStyle, miterLimit, allowOverlap, startCap, endCap,
                                           color, isClosedPolyline);
            }

            return TriangleCache;
        }

        private static void CreatePolySegments(List<Float2> points, float halfThickness)
        {
            PolySegmentCache.Clear();

            // Create line segments, skipping identical consecutive points
            for (int i = 0; i < points.Count - 1; i++)
            {
                Float2 p1 = points[i];
                Float2 p2 = points[i + 1];

                if (Float2.LengthSquared(p1 - p2) > EpsilonSqr)
                    PolySegmentCache.Add(new PolySegment(new LineSegment(p1, p2), halfThickness));
            }
        }

        private static void GenerateTrianglesForPolyline(JointStyle jointStyle, float miterLimit, bool allowOverlap, EndCapStyle startCap, EndCapStyle endCap, Color32 color, bool isClosedPolyline)
        {
            var segmentData = new SegmentRenderData();

            for (int i = 0; i < PolySegmentCache.Count; i++)
            {
                var segment = PolySegmentCache[i];
                CalculateSegmentUVs(i, PolySegmentCache.Count, startCap, endCap, isClosedPolyline,
                                  out float startU, out float endU);

                if (i == 0)
                {
                    if (isClosedPolyline)
                    {
                        // For closed polylines, create joint with last segment
                        var lastSegment = PolySegmentCache[^1];
                        CreateJoint(PolySegmentCache, lastSegment, segment, i - 1, 0, jointStyle,
                                  miterLimit, allowOverlap, startU, color, ref segmentData);
                        segmentData.Start1 = segmentData.NextStart1;
                        segmentData.Start2 = segmentData.NextStart2;
                        segmentData.StartUV1 = segmentData.NextStartUV1;
                        segmentData.StartUV2 = segmentData.NextStartUV2;
                    }
                    else
                    {
                        InitializeFirstSegment(segment, startU, ref segmentData);
                    }
                }
                else
                {
                    segmentData.Start1 = segmentData.NextStart1;
                    segmentData.Start2 = segmentData.NextStart2;
                    segmentData.StartUV1 = segmentData.NextStartUV1;
                    segmentData.StartUV2 = segmentData.NextStartUV2;
                }

                if ((i + 1 == PolySegmentCache.Count) && !isClosedPolyline)
                {
                    FinalizeLastSegment(segment, endU, ref segmentData);
                }
                else
                {
                    var nextSegment = isClosedPolyline && (i + 1 == PolySegmentCache.Count) ?
                        PolySegmentCache[0] : PolySegmentCache[i + 1];
                    int nextIndex = isClosedPolyline && (i + 1 == PolySegmentCache.Count) ? 0 : i + 1;

                    CreateJoint(PolySegmentCache, segment, nextSegment, i, nextIndex, jointStyle,
                              miterLimit, allowOverlap, endU, color, ref segmentData);
                }

                // Add the main quad triangles
                TriangleCache.Add(new Triangle(segmentData.Start1, segmentData.End1, segmentData.Start2,
                                             segmentData.StartUV1, segmentData.EndUV1, segmentData.StartUV2, color));
                TriangleCache.Add(new Triangle(segmentData.Start2, segmentData.End1, segmentData.End2,
                                             segmentData.StartUV2, segmentData.EndUV1, segmentData.EndUV2, color));
            }

            // Add end caps only for open polylines
            if (PolySegmentCache.Count > 0 && !isClosedPolyline)
            {
                AddEndCap(PolySegmentCache[0], startCap, true, color);
                AddEndCap(PolySegmentCache[^1], endCap, false, color);
            }
        }

        private struct SegmentRenderData
        {
            public Float2 Start1, Start2, End1, End2;
            public Float2 NextStart1, NextStart2;
            public Float2 StartUV1, StartUV2, EndUV1, EndUV2;
            public Float2 NextStartUV1, NextStartUV2;
        }

        private static void CalculateSegmentUVs(int index, int totalCount, EndCapStyle startCap, EndCapStyle endCap, bool isClosedPolyline, out float startU, out float endU)
        {
            startU = 0.5f;
            endU = 0.5f;

            if (isClosedPolyline)
            {
                // For closed polylines, all segments use 0.5 for UV coordinates at joints
                return;
            }

            if (totalCount == 1)
            {
                startU = startCap != EndCapStyle.Butt ? 0.5f : 0.0f;
                endU = endCap != EndCapStyle.Butt ? 0.5f : 1.0f;
            }
            else if (index == 0)
            {
                startU = startCap != EndCapStyle.Butt ? 0.5f : 0.0f;
            }
            else if (index == totalCount - 1)
            {
                endU = endCap != EndCapStyle.Butt ? 0.5f : 1.0f;
            }
        }

        private static void InitializeFirstSegment(PolySegment segment, float startU, ref SegmentRenderData data)
        {
            data.Start1 = segment.Edge1.A;
            data.Start2 = segment.Edge2.A;
            data.StartUV1 = new Float2(startU, 0);
            data.StartUV2 = new Float2(startU, 1);
        }

        private static void FinalizeLastSegment(PolySegment segment, float endU, ref SegmentRenderData data)
        {
            data.End1 = segment.Edge1.B;
            data.End2 = segment.Edge2.B;
            data.EndUV1 = new Float2(endU, 0);
            data.EndUV2 = new Float2(endU, 1);
        }


        private static List<List<Float2>> GenerateDashSegments(List<Float2> points, List<float> dashPattern, float dashOffset, Float2 halfPixelOffset)
        {
            var allDashSegments = new List<List<Float2>>();

            if (points.Count < 2 || dashPattern == null || dashPattern.Count == 0 || dashPattern.Sum() <= Epsilon)
            {
                if (points.Count >= 2)
                {
                    var singleSegment = points.Select(p => p + halfPixelOffset).ToList();
                    allDashSegments.Add(singleSegment);
                }
                return allDashSegments;
            }

            var dashState = InitializeDashState(dashPattern, dashOffset);
            var currentDashPoints = new List<Float2>();

            ProcessLineSegments(points, halfPixelOffset, dashPattern, dashState, currentDashPoints, allDashSegments);

            // Add final dash segment if we're in dash state
            if (dashState.IsInDash && currentDashPoints.Count >= 2)
            {
                allDashSegments.Add(new List<Float2>(currentDashPoints));
            }

            allDashSegments.RemoveAll(dash => dash.Count < 2);
            return allDashSegments;
        }

        private struct DashState
        {
            public int PatternIndex;
            public float RemainingLength;
            public bool IsInDash;
        }

        private static DashState InitializeDashState(List<float> dashPattern, float dashOffset)
        {
            float totalPatternLength = dashPattern.Sum();
            float currentOffset = ((dashOffset % totalPatternLength) + totalPatternLength) % totalPatternLength;

            var state = new DashState { PatternIndex = 0, RemainingLength = 0, IsInDash = false };

            // Find starting position in pattern
            for (int k = 0; k < dashPattern.Count * 2; k++)
            {
                if (dashPattern.Count == 0) break;

                float currentPatternLength = dashPattern[state.PatternIndex % dashPattern.Count];
                if (currentOffset >= currentPatternLength)
                {
                    currentOffset -= currentPatternLength;
                    state.PatternIndex++;
                }
                else
                {
                    state.RemainingLength = currentPatternLength - currentOffset;
                    state.IsInDash = (state.PatternIndex % 2 == 0);
                    break;
                }
            }

            if (currentOffset > Epsilon || state.RemainingLength <= Epsilon)
            {
                state.PatternIndex++;
                if (dashPattern.Count > 0)
                {
                    state.RemainingLength = dashPattern[state.PatternIndex % dashPattern.Count];
                    state.IsInDash = (state.PatternIndex % 2 == 0);
                }
            }

            return state;
        }

        private static void ProcessLineSegments(List<Float2> points, Float2 halfPixelOffset, List<float> dashPattern, DashState dashState, List<Float2> currentDashPoints, List<List<Float2>> allDashSegments)
        {
            for (int i = 0; i < points.Count - 1; i++)
            {
                Float2 p1 = points[i];
                Float2 p2 = points[i + 1];

                if (p1 == p2) continue;

                Float2 segmentVector = p2 - p1;
                float segmentLength = Float2.Length(segmentVector);

                if (segmentLength <= Epsilon) continue;

                Float2 segmentDirection = Float2.Normalize(segmentVector);
                float distanceTraversed = 0;

                ProcessSingleSegment(p1, segmentDirection, segmentLength, halfPixelOffset, dashPattern,
                                   ref dashState, ref distanceTraversed, currentDashPoints, allDashSegments);
            }
        }

        private static void ProcessSingleSegment(Float2 segmentStart, Float2 segmentDirection, float segmentLength, Float2 halfPixelOffset, List<float> dashPattern, ref DashState dashState, ref float distanceTraversed, List<Float2> currentDashPoints, List<List<Float2>> allDashSegments)
        {
            while (distanceTraversed < segmentLength)
            {
                float lengthToProcess = Math.Min(dashState.RemainingLength, segmentLength - distanceTraversed);

                if (lengthToProcess <= Epsilon)
                {
                    if (dashState.RemainingLength <= Epsilon && dashState.RemainingLength > 0 &&
                        (segmentLength - distanceTraversed > Epsilon))
                    {
                        distanceTraversed += dashState.RemainingLength;
                        dashState.RemainingLength = 0;
                    }
                    else
                    {
                        break;
                    }
                }

                Float2 startPoint = segmentStart + segmentDirection * distanceTraversed;
                Float2 endPoint = segmentStart + segmentDirection * (distanceTraversed + lengthToProcess);

                if (dashState.IsInDash)
                {
                    AddDashPoint(currentDashPoints, startPoint, halfPixelOffset);
                    AddDashPoint(currentDashPoints, endPoint, halfPixelOffset);
                }

                distanceTraversed += lengthToProcess;
                dashState.RemainingLength -= lengthToProcess;

                if (dashState.RemainingLength <= Epsilon)
                {
                    AdvanceDashPattern(ref dashState, dashPattern, currentDashPoints, allDashSegments);
                }

                if (lengthToProcess <= Epsilon && distanceTraversed < segmentLength && dashState.RemainingLength > Epsilon)
                {
                    break;
                }
            }
        }

        private static void AddDashPoint(List<Float2> dashPoints, Float2 point, Float2 halfPixelOffset)
        {
            Float2 adjustedPoint = point + halfPixelOffset;

            if (dashPoints.Count == 0)
            {
                dashPoints.Add(adjustedPoint);
            }
            else if (Float2.LengthSquared(dashPoints[^1] - adjustedPoint) > EpsilonSqr)
            {
                dashPoints.Add(adjustedPoint);
            }
            else
            {
                dashPoints[^1] = adjustedPoint;
            }
        }

        private static void AdvanceDashPattern(ref DashState dashState, List<float> dashPattern, List<Float2> currentDashPoints, List<List<Float2>> allDashSegments)
        {
            bool wasDashState = dashState.IsInDash;
            dashState.PatternIndex++;

            if (dashPattern.Count == 0) return;

            dashState.IsInDash = (dashState.PatternIndex % 2 == 0);
            dashState.RemainingLength = dashPattern[dashState.PatternIndex % dashPattern.Count];

            if (wasDashState && !dashState.IsInDash)
            {
                // End of dash - save current segment
                if (currentDashPoints.Count >= 2)
                {
                    allDashSegments.Add(new List<Float2>(currentDashPoints));
                }
                currentDashPoints.Clear();
            }
            else if (!wasDashState && dashState.IsInDash)
            {
                // Start of dash - clear any stale points
                currentDashPoints.Clear();
            }
        }

        private static void CreateJoint(List<PolySegment> allSegments, PolySegment segment1, PolySegment segment2, int segment1Index, int segment2Index, JointStyle jointStyle, float miterLimit, bool allowOverlap, float uAtJoint, Color32 color, ref SegmentRenderData data)
        {
            Float2 dir1 = segment1.Center.Direction;
            Float2 dir2 = segment2.Center.Direction;
            float angle = CalculateAngle(dir1, dir2);

            // Check if miter joint should fall back to bevel
            if (jointStyle == JointStyle.Miter)
            {
                float sinHalfAngle = Maths.Sin(angle / 2);
                if (angle < MiterMinAngle || (Math.Abs(sinHalfAngle) > Epsilon && 1 / Math.Abs(sinHalfAngle) > miterLimit))
                {
                    jointStyle = JointStyle.Bevel;
                }
            }

            if (jointStyle == JointStyle.Miter)
            {
                CreateMiterJoint(segment1, segment2, uAtJoint, ref data);
            }
            else
            {
                CreateBevelOrRoundJoint(segment1, segment2, jointStyle, uAtJoint, allowOverlap, color, ref data);
            }
        }

        private static void CreateMiterJoint(PolySegment segment1, PolySegment segment2, float uAtJoint, ref SegmentRenderData data)
        {
            Float2? intersection1 = LineSegment.Intersection(segment1.Edge1, segment2.Edge1, true);
            Float2? intersection2 = LineSegment.Intersection(segment1.Edge2, segment2.Edge2, true);

            data.End1 = intersection1 ?? segment1.Edge1.B;
            data.End2 = intersection2 ?? segment1.Edge2.B;
            data.NextStart1 = data.End1;
            data.NextStart2 = data.End2;

            data.EndUV1 = data.NextStartUV1 = new Float2(uAtJoint, 0);
            data.EndUV2 = data.NextStartUV2 = new Float2(uAtJoint, 1);
        }

        private static void CreateBevelOrRoundJoint(PolySegment segment1, PolySegment segment2, JointStyle jointStyle, float uAtJoint, bool allowOverlap, Color32 color, ref SegmentRenderData data)
        {
            Float2 dir1 = segment1.Center.Direction;
            Float2 dir2 = segment2.Center.Direction;
            float crossProduct = Cross(dir1, dir2);
            bool isClockwise = crossProduct < 0;

            // Determine inner and outer edges
            LineSegment outer1, outer2, inner1, inner2;
            Float2 outerJointUV, innerJointUV;

            if (isClockwise)
            {
                outer1 = segment1.Edge1; outer2 = segment2.Edge1;
                inner1 = segment1.Edge2; inner2 = segment2.Edge2;
                outerJointUV = new Float2(uAtJoint, 0);
                innerJointUV = new Float2(uAtJoint, 1);
            }
            else
            {
                outer1 = segment1.Edge2; outer2 = segment2.Edge2;
                inner1 = segment1.Edge1; inner2 = segment2.Edge1;
                outerJointUV = new Float2(uAtJoint, 1);
                innerJointUV = new Float2(uAtJoint, 0);
            }

            Float2? innerIntersection = LineSegment.Intersection(inner1, inner2, allowOverlap);
            Float2 innerPoint = innerIntersection ?? inner1.B;
            float angle = CalculateAngle(dir1, dir2);
            Float2 innerStart = innerIntersection.HasValue ? innerPoint : (angle > Math.PI / 2 ? outer1.B : inner1.B);

            // Set segment data
            if (isClockwise)
            {
                data.End1 = outer1.B; data.End2 = innerPoint;
                data.NextStart1 = outer2.A; data.NextStart2 = innerStart;
                data.EndUV1 = outerJointUV; data.EndUV2 = innerJointUV;
            }
            else
            {
                data.End1 = innerPoint; data.End2 = outer1.B;
                data.NextStart1 = innerStart; data.NextStart2 = outer2.A;
                data.EndUV1 = innerJointUV; data.EndUV2 = outerJointUV;
            }

            data.NextStartUV1 = data.EndUV1;
            data.NextStartUV2 = data.EndUV2;

            // Add joint triangles
            if (jointStyle == JointStyle.Bevel)
            {
                if (isClockwise)
                    TriangleCache.Add(new Triangle(outer1.B, outer2.A, innerPoint, outerJointUV, outerJointUV, innerJointUV, color));
                else
                    TriangleCache.Add(new Triangle(outer1.B, innerPoint, outer2.A, outerJointUV, innerJointUV, outerJointUV, color));
            }
            else if (jointStyle == JointStyle.Round)
            {
                CreateTriangleFan(innerPoint, segment1.Center.B, outer1.B, outer2.A,
                                innerJointUV, outerJointUV, outerJointUV, isClockwise, color);
            }
        }

        private static void AddEndCap(PolySegment segment, EndCapStyle capStyle, bool isStart, Color32 color)
        {
            Float2 position = isStart ? segment.Center.A : segment.Center.B;
            Float2 direction = segment.Center.Direction;
            if (isStart) direction = -direction;

            if (Float2.LengthSquared(direction) < EpsilonSqr) return;

            Float2 edge1Point = isStart ? segment.Edge1.A : segment.Edge1.B;
            Float2 edge2Point = isStart ? segment.Edge2.A : segment.Edge2.B;
            float halfThickness = Float2.Length(segment.Edge1.A - segment.Edge2.A) / 2; // Fixed: use segment thickness

            float lineConnectU = (capStyle == EndCapStyle.Butt) ? (isStart ? 0.0f : 1.0f) : 0.5f;

            switch (capStyle)
            {
                case EndCapStyle.Butt:
                    break;

                case EndCapStyle.Square:
                    AddSquareCap(edge1Point, edge2Point, direction, halfThickness, isStart, color, lineConnectU);
                    break;

                case EndCapStyle.Bevel:
                case EndCapStyle.Round:
                    AddRoundOrBevelCap(position, direction, halfThickness, capStyle, isStart, color, lineConnectU);
                    break;
            }
        }

        private static void AddSquareCap(Float2 edge1Point, Float2 edge2Point, Float2 direction, float halfThickness, bool isStart, Color32 color, float lineConnectU)
        {
            Float2 extension = direction * halfThickness;
            Float2 corner1 = edge1Point + extension;
            Float2 corner2 = edge2Point + extension;

            float extremityU = isStart ? 0.0f : 1.0f;

            Float2 baseUV1 = new Float2(lineConnectU, 0);
            Float2 baseUV2 = new Float2(lineConnectU, 1);
            Float2 extremityUV1 = new Float2(extremityU, 0);
            Float2 extremityUV2 = new Float2(extremityU, 1);

            if (isStart)
            {
                TriangleCache.Add(new Triangle(edge1Point, edge2Point, corner1, baseUV1, baseUV2, extremityUV1, color));
                TriangleCache.Add(new Triangle(edge2Point, corner2, corner1, baseUV2, extremityUV2, extremityUV1, color));
            }
            else
            {
                TriangleCache.Add(new Triangle(edge1Point, corner1, edge2Point, baseUV1, extremityUV1, baseUV2, color));
                TriangleCache.Add(new Triangle(edge2Point, corner1, corner2, baseUV2, extremityUV1, extremityUV2, color));
            }
        }

        private static void AddRoundOrBevelCap(Float2 position, Float2 direction, float radius, EndCapStyle capStyle, bool isStart, Color32 color, float lineConnectU)
        {
            int numSegments = capStyle == EndCapStyle.Bevel ? 2 : CalculateRoundSegments(radius);
            Float2 perpendicular = new Float2(-direction.Y, direction.X);
            Float2 fanConnectUV = new Float2(lineConnectU, 0.5f);

            float totalAngle = Maths.PI;
            float angleStep = totalAngle / numSegments;
            float startAngle = -Maths.PI / 2.0f;

            for (int i = 0; i < numSegments; i++)
            {
                float currentAngle = startAngle + i * angleStep;
                float nextAngle = startAngle + (i + 1) * angleStep;

                Float2 currentPoint = position + direction * (Maths.Cos(currentAngle) * radius) +
                                     perpendicular * (Maths.Sin(currentAngle) * radius);
                Float2 nextPoint = position + direction * (Maths.Cos(nextAngle) * radius) +
                                  perpendicular * (Maths.Sin(nextAngle) * radius);

                Float2 currentUV = new Float2(isStart ? 0.0f : 1.0f, 0.5f + (Maths.Sin(currentAngle) * 0.5f));
                Float2 nextUV = new Float2(isStart ? 0.0f : 1.0f, 0.5f + (Maths.Sin(nextAngle) * 0.5f));

                if (isStart)
                    TriangleCache.Add(new Triangle(currentPoint, position, nextPoint, currentUV, fanConnectUV, nextUV, color));
                else
                    TriangleCache.Add(new Triangle(nextPoint, currentPoint, position, nextUV, currentUV, fanConnectUV, color));
            }
        }

        private static int CalculateRoundSegments(float radius)
        {
            const float minDistance = 3.0f; // Minimum screen space distance for arc segments
            float arcLength = Maths.PI * radius; // Semicircle arc length
            int segments = Math.Max(6, (int)Math.Floor(arcLength / minDistance));
            return Math.Min(segments, 16);
        }

        private static void CreateTriangleFan(Float2 connectTo, Float2 origin, Float2 start, Float2 end, Float2 centerUV, Float2 startUV, Float2 endUV, bool clockwise, Color32 color)
        {
            Float2 point1 = start - origin;
            Float2 point2 = end - origin;
            float angle1 = Maths.Atan2(point1.Y, point1.X);
            float angle2 = Maths.Atan2(point2.Y, point2.X);

            if (clockwise && angle2 > angle1) angle2 -= 2 * Maths.PI;
            else if (!clockwise && angle1 > angle2) angle1 -= 2 * Maths.PI;

            float jointAngle = angle2 - angle1;
            if (Math.Abs(jointAngle) < Epsilon) return;

            int numTriangles = Maths.Max(1, (int)Maths.Floor(Maths.Abs(jointAngle) / RoundMinAngle));
            float triangleAngle = jointAngle / numTriangles;
            float interpolationStep = 1.0f / numTriangles;

            Float2 currentPoint = start;
            Float2 currentUV = startUV;

            for (int i = 0; i < numTriangles; i++)
            {
                Float2 nextPoint, nextUV;

                if (i + 1 == numTriangles)
                {
                    nextPoint = end;
                    nextUV = endUV;
                }
                else
                {
                    float rotation = angle1 + (i + 1) * triangleAngle;
                    float magnitude = Float2.Length(point1);

                    if (magnitude < Epsilon)
                    {
                        nextPoint = origin;
                    }
                    else
                    {
                        nextPoint = origin + new Float2(Maths.Cos(rotation) * magnitude,
                                                       Maths.Sin(rotation) * magnitude);
                    }
                    nextUV = Maths.Lerp(startUV, endUV, (float)((i + 1) * interpolationStep));
                }

                if (clockwise)
                    TriangleCache.Add(new Triangle(currentPoint, nextPoint, connectTo, currentUV, nextUV, centerUV, color));
                else
                    TriangleCache.Add(new Triangle(currentPoint, connectTo, nextPoint, currentUV, centerUV, nextUV, color));

                currentPoint = nextPoint;
                currentUV = nextUV;
            }
        }

        private struct LineSegment
        {
            public Float2 A { get; }
            public Float2 B { get; }

            private Float2? _cachedDirection;
            private Float2? _cachedNormal;

            public Float2 Direction => _cachedDirection ??= CalculateDirection();
            public Float2 Normal => _cachedNormal ??= CalculateNormal();

            public LineSegment(Float2 a, Float2 b)
            {
                A = a;
                B = b;
                _cachedDirection = null;
                _cachedNormal = null;
            }

            private Float2 CalculateDirection()
            {
                Float2 dir = B - A;
                float magSq = Float2.LengthSquared(dir);
                return magSq <= 1e-12 ? Float2.Zero : dir / Maths.Sqrt(magSq);
            }

            private Float2 CalculateNormal()
            {
                Float2 dir = Direction;
                return new Float2(-dir.Y, dir.X);
            }

            public static Float2? Intersection(LineSegment a, LineSegment b, bool infiniteLines)
            {
                Float2 r = a.B - a.A;
                Float2 s = b.B - b.A;
                Float2 originDist = b.A - a.A;

                float uNumerator = Cross(originDist, r);
                float denominator = Cross(r, s);

                if (Math.Abs(denominator) < Epsilon) return null;

                float u = uNumerator / denominator;
                float t = Cross(originDist, s) / denominator;

                if (!infiniteLines && (t < -Epsilon || t > 1.0 + Epsilon || u < -Epsilon || u > 1.0 + Epsilon))
                    return null;

                return a.A + r * t;
            }

            public static LineSegment operator +(LineSegment segment, Float2 offset) =>
                new LineSegment(segment.A + offset, segment.B + offset);

            public static LineSegment operator -(LineSegment segment, Float2 offset) =>
                new LineSegment(segment.A - offset, segment.B - offset);
        }

        private struct PolySegment
        {
            public LineSegment Center { get; }
            public LineSegment Edge1 { get; }
            public LineSegment Edge2 { get; }

            public PolySegment(LineSegment center, float thickness)
            {
                Center = center;

                if (Float2.LengthSquared(center.Direction) < EpsilonSqr)
                {
                    Edge1 = new LineSegment(center.A, center.A);
                    Edge2 = new LineSegment(center.A, center.A);
                }
                else
                {
                    Float2 normalOffset = center.Normal * thickness;
                    Edge1 = center + normalOffset;
                    Edge2 = center - normalOffset;
                }
            }
        }

        // Utility methods
        private static float Cross(Float2 a, Float2 b) => a.X * b.Y - a.Y * b.X;

        private static float Dot(Float2 a, Float2 b) => a.X * b.X + a.Y * b.Y;

        private static float CalculateAngle(Float2 a, Float2 b)
        {
            float magASq = Float2.LengthSquared(a);
            float magBSq = Float2.LengthSquared(b);

            if (magASq * magBSq <= 1e-12) return 0;

            float dot = Dot(a, b);
            float cosAngle = dot / Maths.Sqrt(magASq * magBSq);
            return Maths.Acos(Maths.Max(-1.0f, Maths.Min(1.0f, cosAngle)));
        }
    }
}
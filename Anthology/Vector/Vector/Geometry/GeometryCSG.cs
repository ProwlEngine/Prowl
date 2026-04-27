// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Prowl.Vector.Geometry
{
    /// <summary>
    /// CSG (Constructive Solid Geometry) operations for GeometryData.
    /// Complete production implementation supporting Union, Intersection, and Subtraction.
    ///
    /// IMPORTANT: All input geometry MUST be fully triangulated before CSG operations.
    /// Operations will throw InvalidOperationException if any face is not a triangle.
    /// Use GeometryOperators.Triangulate() or ensure geometry generators produce triangles only.
    /// </summary>
    public static class GeometryCSG
    {
        public enum Operation
        {
            Union,
            Intersection,
            Subtraction
        }

        #region Public API

        /// <summary>
        /// Performs a CSG operation on two geometries.
        /// Both geometries MUST be triangulated (all faces must have exactly 3 vertices).
        /// </summary>
        /// <param name="operation">The CSG operation to perform</param>
        /// <param name="meshA">First geometry (must be triangulated)</param>
        /// <param name="meshB">Second geometry (must be triangulated)</param>
        /// <returns>Result geometry</returns>
        /// <exception cref="InvalidOperationException">Thrown if either geometry contains non-triangle faces</exception>
        public static GeometryData PerformOperation(Operation operation, GeometryData meshA, GeometryData meshB)
        {
            var brushA = GeometryDataToBrush(meshA);
            var brushB = GeometryDataToBrush(meshB);
            var resultBrush = new CSGBrush();

            var brushOp = new CSGBrushOperation();
            brushOp.MergeBrushes(operation, brushA, brushB, ref resultBrush);

            return BrushToGeometryData(resultBrush);
        }

        /// <summary>
        /// Computes the union of two triangulated geometries.
        /// </summary>
        /// <exception cref="InvalidOperationException">Thrown if either geometry contains non-triangle faces</exception>
        public static GeometryData Union(GeometryData meshA, GeometryData meshB)
            => PerformOperation(Operation.Union, meshA, meshB);

        /// <summary>
        /// Computes the intersection of two triangulated geometries.
        /// </summary>
        /// <exception cref="InvalidOperationException">Thrown if either geometry contains non-triangle faces</exception>
        public static GeometryData Intersect(GeometryData meshA, GeometryData meshB)
            => PerformOperation(Operation.Intersection, meshA, meshB);

        /// <summary>
        /// Subtracts meshB from meshA (meshA - meshB) for triangulated geometries.
        /// </summary>
        /// <exception cref="InvalidOperationException">Thrown if either geometry contains non-triangle faces</exception>
        public static GeometryData Subtraction(GeometryData meshA, GeometryData meshB)
            => PerformOperation(Operation.Subtraction, meshA, meshB);

        #endregion

        #region CSGBrush

        private class CSGBrush
        {
            private static readonly float EPSILON_SQUARED = Intersection.INTERSECTION_EPSILON * Intersection.INTERSECTION_EPSILON;

            public struct Face
            {
                public List<Float3> Vertices;
                public Float2[] UVs;
            }

            public Face[] Faces = Array.Empty<Face>();

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static bool IsEqualApprox(Float3 vec1, Float3 vec2)
            {
                Float3 vec3 = vec1 - vec2;
                return vec3.X * vec3.X + vec3.Y * vec3.Y + vec3.Z * vec3.Z < EPSILON_SQUARED;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static bool IsEqualApprox(Float2 vec1, Float2 vec2)
            {
                Float2 vec3 = vec1 - vec2;
                return vec3.X * vec3.X + vec3.Y * vec3.Y < EPSILON_SQUARED;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static Float2 InterpolateSegmentUV(Float2[] segmentPoints, Float2[] uvs, Float2 interpolation)
            {
                if (IsEqualApprox(segmentPoints[0], segmentPoints[1]))
                    return uvs[0];

                float segmentLength = Float2.Length(segmentPoints[1] - segmentPoints[0]);
                float distance = Float2.Length(interpolation - segmentPoints[0]);
                float fraction = distance / segmentLength;

                return Maths.Lerp(uvs[0], uvs[1], fraction);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static Float2 InterpolateTriangleUV(Float2[] vertices, Float2[] uvs, Float2 interpolationPoint)
            {
                if (IsEqualApprox(interpolationPoint, vertices[0])) return uvs[0];
                if (IsEqualApprox(interpolationPoint, vertices[1])) return uvs[1];
                if (IsEqualApprox(interpolationPoint, vertices[2])) return uvs[2];

                Float2 edge1 = vertices[1] - vertices[0];
                Float2 edge2 = vertices[2] - vertices[0];
                Float2 interpolation = interpolationPoint - vertices[0];

                float edge1OnEdge1 = Float2.Dot(edge1, edge1);
                float edge1OnEdge2 = Float2.Dot(edge1, edge2);
                float edge2OnEdge2 = Float2.Dot(edge2, edge2);
                float interOnEdge1 = Float2.Dot(interpolation, edge1);
                float interOnEdge2 = Float2.Dot(interpolation, edge2);
                float scale = (edge1OnEdge1 * edge2OnEdge2 - edge1OnEdge2 * edge1OnEdge2);
                if (Maths.Abs(scale) < Intersection.INTERSECTION_EPSILON)
                    return uvs[0];

                float v = (edge2OnEdge2 * interOnEdge1 - edge1OnEdge2 * interOnEdge2) / scale;
                float w = (edge1OnEdge1 * interOnEdge2 - edge1OnEdge2 * interOnEdge1) / scale;
                float u = 1.0f - v - w;

                return uvs[0] * u + uvs[1] * v + uvs[2] * w;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static bool RayIntersectsTriangle(Float3 from, Float3 dir, Float3[] vertices, float tolerance, ref Float3 intersectionPoint)
            {
                if (Prowl.Vector.Geometry.Intersection.RayTriangle(from, dir, vertices[0], vertices[1], vertices[2], out float distance, out _, out _))
                {
                    intersectionPoint = from + dir * distance;
                    return true;
                }
                return false;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static bool IsPointInTriangle(Float3 point, Float3[] vertices, int shifted = 0)
            {
                float det = Float3.Dot(vertices[0], Float3.Cross(vertices[1], vertices[2]));

                if (Maths.Abs(det) < Intersection.INTERSECTION_EPSILON)
                {
                    if (shifted > 2)
                        return false;
                    Float3 shiftBy = Float3.Zero;
                    shiftBy[shifted] = 1;
                    Float3 shiftedPoint = point + shiftBy;
                    Float3[] shiftedVertices = { vertices[0] + shiftBy, vertices[1] + shiftBy, vertices[2] + shiftBy };
                    return IsPointInTriangle(shiftedPoint, shiftedVertices, shifted + 1);
                }

                float[] lambda = new float[3];
                lambda[0] = Float3.Dot(point, Float3.Cross(vertices[1], vertices[2])) / det;
                lambda[1] = Float3.Dot(point, Float3.Cross(vertices[2], vertices[0])) / det;
                lambda[2] = Float3.Dot(point, Float3.Cross(vertices[0], vertices[1])) / det;

                if (Maths.Abs((lambda[0] + lambda[1] + lambda[2]) - 1) >= Intersection.INTERSECTION_EPSILON)
                    return false;

                if (lambda[0] < 0 || lambda[1] < 0 || lambda[2] < 0)
                    return false;

                return true;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static bool IsTriangleDegenerate(Float2[] vertices, float tolerance)
            {
                float det = vertices[0].X * vertices[1].Y - vertices[0].X * vertices[2].Y +
                        vertices[0].Y * vertices[2].X - vertices[0].Y * vertices[1].X +
                        vertices[1].X * vertices[2].Y - vertices[1].Y * vertices[2].X;

                return Maths.Abs(det) < tolerance;
            }

            public static bool AreSegmentsParallel(Float2[] segment1Points, Float2[] segment2Points, float tolerance)
            {

                Float2 segment1 = segment1Points[1] - segment1Points[0];
                Float2 segment2 = segment2Points[1] - segment2Points[0];
                float segment1Length2 = Float2.Dot(segment1, segment1);
                float segment2Length2 = Float2.Dot(segment2, segment2);
                float segmentOntoSegment = Float2.Dot(segment2, segment1);

                if (segment1Length2 < tolerance || segment2Length2 < tolerance)
                    return true;

                float maxSeparation2;
                if (segment1Length2 > segment2Length2)
                    maxSeparation2 = segment2Length2 - segmentOntoSegment * segmentOntoSegment / segment1Length2;
                else
                    maxSeparation2 = segment1Length2 - segmentOntoSegment * segmentOntoSegment / segment2Length2;

                return maxSeparation2 < tolerance;
            }
        }

        #endregion

        #region Transform2DFace

        private class Transform2DFace
        {
            private Float3 basisL1 = new Float3(1, 0, 0);
            private Float3 basisL2 = new Float3(0, 1, 0);
            private Float3 basisL3 = new Float3(0, 0, 1);
            private Float3 position = Float3.Zero;

            public void SetBasisColumn(int col, Float3 value)
            {
                basisL1[col] = value[0];
                basisL2[col] = value[1];
                basisL3[col] = value[2];
            }

            public Float3 GetBasisColumn(int col)
            {
                return new Float3(basisL1[col], basisL2[col], basisL3[col]);
            }

            public void SetPosition(Float3 pos)
            {
                position = pos;
            }

            public Float3 Xform(Float3 vector)
            {
                return new Float3(
                    Float3.Dot(basisL1, vector) + position.X,
                    Float3.Dot(basisL2, vector) + position.Y,
                    Float3.Dot(basisL3, vector) + position.Z);
            }

            private Float3 BasisXform(Float3 vector)
            {
                return new Float3(
                    Float3.Dot(basisL1, vector),
                    Float3.Dot(basisL2, vector),
                    Float3.Dot(basisL3, vector));
            }

            private float Cofac(ref Float3 row1, int col1, ref Float3 row2, int col2)
            {
                return row1[col1] * row2[col2] - row1[col2] * row2[col1];
            }

            private void BasisInvert()
            {
                float[] co = new float[3];
                co[0] = Cofac(ref basisL2, 1, ref basisL3, 2);
                co[1] = Cofac(ref basisL2, 2, ref basisL3, 0);
                co[2] = Cofac(ref basisL2, 0, ref basisL3, 1);

                float det = basisL1[0] * co[0] + basisL1[1] * co[1] + basisL1[2] * co[2];
                if (Maths.Abs(det) < float.Epsilon)
                    return;

                float s = 1.0f / det;

                SetBasis(
                    co[0] * s, Cofac(ref basisL1, 2, ref basisL3, 1) * s, Cofac(ref basisL1, 1, ref basisL2, 2) * s,
                    co[1] * s, Cofac(ref basisL1, 0, ref basisL3, 2) * s, Cofac(ref basisL1, 2, ref basisL2, 0) * s,
                    co[2] * s, Cofac(ref basisL1, 1, ref basisL3, 0) * s, Cofac(ref basisL1, 0, ref basisL2, 1) * s);
            }

            public void SetBasis(float xx, float xy, float xz, float yx, float yy, float yz, float zx, float zy, float zz)
            {
                basisL1 = new Float3(xx, xy, xz);
                basisL2 = new Float3(yx, yy, yz);
                basisL3 = new Float3(zx, zy, zz);
            }

            public void AffineInvert()
            {
                BasisInvert();
                position = BasisXform(-position);
            }

            public Transform2DFace AffineInverse()
            {
                Transform2DFace result = new Transform2DFace();
                result.basisL1 = basisL1;
                result.basisL2 = basisL2;
                result.basisL3 = basisL3;
                result.position = position;
                result.AffineInvert();
                return result;
            }
        }

        #endregion

        #region Build2DFaces

        private class Build2DFaces
        {
            private struct Vertex2D
            {
                public Float2 Point;
                public Float2 UV;
            }

            private struct Face2D
            {
                public int[] VertexIdx;
            }

            private List<Vertex2D> vertices = new List<Vertex2D>();
            private List<Face2D> faces = new List<Face2D>();
            private const float vertexTolerance = 1e-10f;
            private Transform2DFace to3D;
            private Transform2DFace to2D;
            private Plane plane;

            public Build2DFaces() { }

            public Build2DFaces(CSGBrush brush, int faceIdx)
            {
                Float3[] points3D = new Float3[3];
                for (int i = 0; i < 3; i++)
                    points3D[i] = brush.Faces[faceIdx].Vertices[i];

                plane = new Plane(points3D[0], points3D[1], points3D[2]);
                to3D = new Transform2DFace();
                to3D.SetPosition(points3D[0]);
                to3D.SetBasisColumn(2, plane.Normal);
                Float3 temp = points3D[1] - points3D[2];
                temp = Float3.Normalize(temp);
                to3D.SetBasisColumn(0, temp);
                temp = Float3.Cross(to3D.GetBasisColumn(0), to3D.GetBasisColumn(2));
                temp = Float3.Normalize(temp);
                to3D.SetBasisColumn(1, temp);
                to2D = to3D.AffineInverse();

                Face2D face = new Face2D { VertexIdx = new int[3] };
                for (int i = 0; i < 3; i++)
                {
                    Vertex2D vertex = new Vertex2D();
                    Float3 point2D = to2D.Xform(points3D[i]);
                    vertex.Point = new Float2(point2D.X, point2D.Y);
                    vertex.UV = brush.Faces[faceIdx].UVs[i];
                    vertices.Add(vertex);
                    face.VertexIdx[i] = i;
                }
                faces.Add(face);
            }

            private static Float2 GetClosestPointToSegment(Float2 point, Float2[] segment)
            {
                Float2 p = point - segment[0];
                Float2 n = segment[1] - segment[0];
                float l2 = Float2.LengthSquared(n);
                if (l2 < 1e-20)
                    return segment[0];

                float d = Float2.Dot(n, p) / l2;

                if (d <= 0.0)
                    return segment[0];
                else if (d >= 1.0)
                    return segment[1];
                else
                    return segment[0] + n * d;
            }

            private static bool SegmentIntersectsSegment(Float2 fromA, Float2 toA, Float2 fromB, Float2 toB, ref Float2 result)
            {
                Float2 AB = toA - fromA;
                Float2 AC = fromB - fromA;
                Float2 AD = toB - fromA;

                float ABlen = Float2.Dot(AB, AB);
                if (ABlen <= 0)
                    return false;

                Float2 ABNorm = AB / ABlen;
                AC = new Float2(AC.X * ABNorm.X + AC.Y * ABNorm.Y, AC.Y * ABNorm.X - AC.X * ABNorm.Y);
                AD = new Float2(AD.X * ABNorm.X + AD.Y * ABNorm.Y, AD.Y * ABNorm.X - AD.X * ABNorm.Y);

                if ((AC.Y < -Intersection.INTERSECTION_EPSILON && AD.Y < -Intersection.INTERSECTION_EPSILON) ||
                    (AC.Y > Intersection.INTERSECTION_EPSILON && AD.Y > Intersection.INTERSECTION_EPSILON))
                    return false;

                if (Maths.Abs(AD.Y - AC.Y) < Intersection.INTERSECTION_EPSILON)
                    return false;

                float ABpos = AD.X + (AC.X - AD.X) * AD.Y / (AD.Y - AC.Y);

                if ((ABpos < 0) || (ABpos > 1))
                    return false;

                result = fromA + AB * ABpos;
                return true;
            }

            private static bool IsPointInTriangle(Float2 point, Float2 a, Float2 b, Float2 c)
            {
                Float2 an = a - point;
                Float2 bn = b - point;
                Float2 cn = c - point;

                bool orientation = (an.X * bn.Y - an.Y * bn.X) > 0;

                if (((bn.X * cn.Y - bn.Y * cn.X) > 0) != orientation)
                    return false;

                return ((cn.X * an.Y - cn.Y * an.X) > 0) == orientation;
            }

            private static bool PlaneIntersectsSegment(Plane plane, Float3 begin, Float3 end, ref Float3 result)
            {
                return Prowl.Vector.Geometry.Intersection.LineSegmentPlane(begin, end, plane.Normal, plane.D, out result);
            }

            private int GetPointIdx(Float2 point)
            {
                for (int vertexIdx = 0; vertexIdx < vertices.Count; ++vertexIdx)
                {
                    if (Float2.LengthSquared(vertices[vertexIdx].Point - point) < vertexTolerance)
                        return vertexIdx;
                }
                return -1;
            }

            private int AddVertex(Vertex2D vertex)
            {
                int vertexId = GetPointIdx(vertex.Point);
                if (vertexId != -1)
                    return vertexId;

                vertices.Add(vertex);
                return vertices.Count - 1;
            }

            private void AddVertexIdxSorted(List<int> vertexIndices, int newVertexIndex)
            {
                if (newVertexIndex >= 0 && vertexIndices.IndexOf(newVertexIndex) == -1)
                {
                    if (vertexIndices.Count == 0)
                    {
                        vertexIndices.Add(newVertexIndex);
                        return;
                    }

                    Float2 firstPoint;
                    Float2 newPoint;
                    int axis;
                    if (vertexIndices.Count == 1)
                    {
                        firstPoint = vertices[vertexIndices[0]].Point;
                        newPoint = vertices[newVertexIndex].Point;

                        axis = 0;
                        if (Maths.Abs(newPoint.X - firstPoint.X) < Maths.Abs(newPoint.Y - firstPoint.Y))
                            axis = 1;

                        if (newPoint[axis] < firstPoint[axis])
                            vertexIndices.Insert(0, newVertexIndex);
                        else
                            vertexIndices.Add(newVertexIndex);

                        return;
                    }

                    firstPoint = vertices[vertexIndices[0]].Point;
                    Float2 lastPoint = vertices[vertexIndices[vertexIndices.Count - 1]].Point;
                    newPoint = vertices[newVertexIndex].Point;

                    axis = 0;
                    if (Maths.Abs(lastPoint.X - firstPoint.X) < Maths.Abs(lastPoint.Y - firstPoint.Y))
                        axis = 1;

                    for (int insertIdx = 0; insertIdx < vertexIndices.Count; ++insertIdx)
                    {
                        Float2 insertPoint = vertices[vertexIndices[insertIdx]].Point;
                        if (newPoint[axis] < insertPoint[axis])
                        {
                            vertexIndices.Insert(insertIdx, newVertexIndex);
                            return;
                        }
                    }
                    vertexIndices.Add(newVertexIndex);
                }
            }

            private void MergeFaces(List<int> segmentIndices)
            {
                int segments = segmentIndices.Count - 1;
                if (segments < 2)
                    return;

                // Faces around an inner vertex are merged by moving the inner vertex to the first vertex
                for (int sortedIdx = 1; sortedIdx < segments; ++sortedIdx)
                {
                    int closestIdx = 0;
                    int innerIdx = segmentIndices[sortedIdx];

                    if (sortedIdx > segments / 2)
                    {
                        closestIdx = segments;
                        innerIdx = segmentIndices[segments + segments / 2 - sortedIdx];
                    }

                    List<int> mergeFacesIdx = new List<int>();
                    List<Face2D> mergeFaces = new List<Face2D>();
                    List<int> mergeFacesInnerVertexIdx = new List<int>();
                    for (int faceIdx = 0; faceIdx < faces.Count; ++faceIdx)
                    {
                        for (int faceVertexIdx = 0; faceVertexIdx < 3; ++faceVertexIdx)
                        {
                            if (faces[faceIdx].VertexIdx[faceVertexIdx] == innerIdx)
                            {
                                mergeFacesIdx.Add(faceIdx);
                                mergeFaces.Add(faces[faceIdx]);
                                mergeFacesInnerVertexIdx.Add(faceVertexIdx);
                            }
                        }
                    }

                    List<int> degeneratePoints = new List<int>();

                    for (int mergeIdx = 0; mergeIdx < mergeFaces.Count; ++mergeIdx)
                    {
                        int[] outerEdgeIdx = new int[2];
                        outerEdgeIdx[0] = mergeFaces[mergeIdx].VertexIdx[(mergeFacesInnerVertexIdx[mergeIdx] + 1) % 3];
                        outerEdgeIdx[1] = mergeFaces[mergeIdx].VertexIdx[(mergeFacesInnerVertexIdx[mergeIdx] + 2) % 3];

                        if (outerEdgeIdx[0] == segmentIndices[closestIdx] || outerEdgeIdx[1] == segmentIndices[closestIdx])
                            continue;

                        Float2[] edge1 = { vertices[outerEdgeIdx[0]].Point, vertices[segmentIndices[closestIdx]].Point };
                        Float2[] edge2 = { vertices[outerEdgeIdx[1]].Point, vertices[segmentIndices[closestIdx]].Point };
                        if (CSGBrush.AreSegmentsParallel(edge1, edge2, vertexTolerance))
                        {
                            if (!degeneratePoints.Contains(outerEdgeIdx[0]))
                                degeneratePoints.Add(outerEdgeIdx[0]);
                            if (!degeneratePoints.Contains(outerEdgeIdx[1]))
                                degeneratePoints.Add(outerEdgeIdx[1]);
                            continue;
                        }

                        Face2D newFace = new Face2D { VertexIdx = new int[3] };
                        newFace.VertexIdx[0] = segmentIndices[closestIdx];
                        newFace.VertexIdx[1] = outerEdgeIdx[0];
                        newFace.VertexIdx[2] = outerEdgeIdx[1];
                        faces.Add(newFace);
                    }

                    mergeFacesIdx.Sort();
                    mergeFacesIdx.Reverse();
                    for (int i = 0; i < mergeFacesIdx.Count; ++i)
                        faces.RemoveAt(mergeFacesIdx[i]);

                    if (degeneratePoints.Count == 0)
                        continue;

                    // Split faces using degenerate points
                    for (int faceIdx = 0; faceIdx < faces.Count; ++faceIdx)
                    {
                        Face2D face = faces[faceIdx];
                        Vertex2D[] faceVertices = {
                            vertices[face.VertexIdx[0]],
                            vertices[face.VertexIdx[1]],
                            vertices[face.VertexIdx[2]]
                        };
                        Float2[] facePoints = { faceVertices[0].Point, faceVertices[1].Point, faceVertices[2].Point };

                        for (int pointIdx = 0; pointIdx < degeneratePoints.Count; ++pointIdx)
                        {
                            int degenerateIdx = degeneratePoints[pointIdx];
                            Float2 point2D = vertices[degenerateIdx].Point;

                            bool existing = false;
                            for (int i = 0; i < 3; ++i)
                            {
                                if (Float2.LengthSquared(faceVertices[i].Point - point2D) < vertexTolerance)
                                {
                                    existing = true;
                                    break;
                                }
                            }
                            if (existing)
                                continue;

                            for (int faceEdgeIdx = 0; faceEdgeIdx < 3; ++faceEdgeIdx)
                            {
                                Float2[] edgePoints = { facePoints[faceEdgeIdx], facePoints[(faceEdgeIdx + 1) % 3] };
                                Float2 closestPoint = GetClosestPointToSegment(point2D, edgePoints);

                                if (Float2.LengthSquared(point2D - closestPoint) < vertexTolerance)
                                {
                                    int oppositeVertexIdx = face.VertexIdx[(faceEdgeIdx + 2) % 3];

                                    if (degenerateIdx == oppositeVertexIdx)
                                    {
                                        faces.RemoveAt(faceIdx);
                                        --faceIdx;
                                        break;
                                    }

                                    Face2D leftFace = new Face2D { VertexIdx = new int[3] };
                                    leftFace.VertexIdx[0] = degenerateIdx;
                                    leftFace.VertexIdx[1] = face.VertexIdx[(faceEdgeIdx + 1) % 3];
                                    leftFace.VertexIdx[2] = oppositeVertexIdx;
                                    Face2D rightFace = new Face2D { VertexIdx = new int[3] };
                                    rightFace.VertexIdx[0] = oppositeVertexIdx;
                                    rightFace.VertexIdx[1] = face.VertexIdx[faceEdgeIdx];
                                    rightFace.VertexIdx[2] = degenerateIdx;
                                    faces.RemoveAt(faceIdx);
                                    faces.Insert(faceIdx, rightFace);
                                    faces.Insert(faceIdx, leftFace);

                                    ++faceIdx;
                                    break;
                                }
                            }
                        }
                    }
                }
            }

            private void FindEdgeIntersections(Float2[] segmentPoints, ref List<int> segmentIndices)
            {
                for (int faceIdx = 0; faceIdx < faces.Count; ++faceIdx)
                {
                    Face2D face = faces[faceIdx];
                    Vertex2D[] faceVertices = {
                        vertices[face.VertexIdx[0]],
                        vertices[face.VertexIdx[1]],
                        vertices[face.VertexIdx[2]]
                    };

                    for (int faceEdgeIdx = 0; faceEdgeIdx < 3; ++faceEdgeIdx)
                    {
                        Float2[] edgePoints = {
                            faceVertices[faceEdgeIdx].Point,
                            faceVertices[(faceEdgeIdx + 1) % 3].Point
                        };
                        Float2[] edgeUVs = {
                            faceVertices[faceEdgeIdx].UV,
                            faceVertices[(faceEdgeIdx + 1) % 3].UV
                        };
                        Float2 intersectionPoint = Float2.Zero;

                        bool onEdge = false;
                        for (int edgePointIdx = 0; edgePointIdx < 2; ++edgePointIdx)
                        {
                            intersectionPoint = GetClosestPointToSegment(segmentPoints[edgePointIdx], edgePoints);
                            if (Float2.LengthSquared(segmentPoints[edgePointIdx] - intersectionPoint) < vertexTolerance)
                            {
                                onEdge = true;
                                break;
                            }
                        }

                        if (onEdge || SegmentIntersectsSegment(segmentPoints[0], segmentPoints[1], edgePoints[0], edgePoints[1], ref intersectionPoint))
                        {
                            if ((Float2.LengthSquared(edgePoints[0] - intersectionPoint) < vertexTolerance) ||
                                (Float2.LengthSquared(edgePoints[1] - intersectionPoint) < vertexTolerance))
                                continue;

                            if (CSGBrush.AreSegmentsParallel(segmentPoints, edgePoints, vertexTolerance))
                                continue;

                            Vertex2D newVertex = new Vertex2D
                            {
                                Point = intersectionPoint,
                                UV = CSGBrush.InterpolateSegmentUV(edgePoints, edgeUVs, intersectionPoint)
                            };
                            int newVertexIdx = AddVertex(newVertex);
                            int oppositeVertexIdx = face.VertexIdx[(faceEdgeIdx + 2) % 3];
                            AddVertexIdxSorted(segmentIndices, newVertexIdx);

                            if (newVertexIdx == oppositeVertexIdx)
                            {
                                faces.RemoveAt(faceIdx);
                                --faceIdx;
                                break;
                            }

                            Float2 closestPoint = GetClosestPointToSegment(vertices[oppositeVertexIdx].Point, segmentPoints);
                            if (Float2.LengthSquared(vertices[oppositeVertexIdx].Point - closestPoint) < vertexTolerance)
                                AddVertexIdxSorted(segmentIndices, oppositeVertexIdx);

                            Face2D leftFace = new Face2D { VertexIdx = new int[3] };
                            leftFace.VertexIdx[0] = newVertexIdx;
                            leftFace.VertexIdx[1] = face.VertexIdx[(faceEdgeIdx + 1) % 3];
                            leftFace.VertexIdx[2] = oppositeVertexIdx;
                            Face2D rightFace = new Face2D { VertexIdx = new int[3] };
                            rightFace.VertexIdx[0] = oppositeVertexIdx;
                            rightFace.VertexIdx[1] = face.VertexIdx[faceEdgeIdx];
                            rightFace.VertexIdx[2] = newVertexIdx;
                            faces.RemoveAt(faceIdx);
                            faces.Insert(faceIdx, rightFace);
                            faces.Insert(faceIdx, leftFace);

                            --faceIdx;
                            break;
                        }
                    }
                }
            }

            private int InsertPoint(Float2 point)
            {
                int newVertexIdx = -1;

                for (int faceIdx = 0; faceIdx < faces.Count; ++faceIdx)
                {
                    Face2D face = faces[faceIdx];
                    Vertex2D[] faceVertices = {
                        vertices[face.VertexIdx[0]],
                        vertices[face.VertexIdx[1]],
                        vertices[face.VertexIdx[2]]
                    };
                    Float2[] points = { faceVertices[0].Point, faceVertices[1].Point, faceVertices[2].Point };
                    Float2[] uvs = { faceVertices[0].UV, faceVertices[1].UV, faceVertices[2].UV };

                    if (CSGBrush.IsTriangleDegenerate(points, vertexTolerance))
                        continue;

                    for (int i = 0; i < 3; ++i)
                    {
                        if (Float2.LengthSquared(faceVertices[i].Point - point) < vertexTolerance)
                            return face.VertexIdx[i];
                    }

                    bool onEdge = false;
                    for (int faceEdgeIdx = 0; faceEdgeIdx < 3; ++faceEdgeIdx)
                    {
                        Float2[] edgePoints = { points[faceEdgeIdx], points[(faceEdgeIdx + 1) % 3] };
                        Float2[] edgeUVs = { uvs[faceEdgeIdx], uvs[(faceEdgeIdx + 1) % 3] };

                        Float2 closestPoint = GetClosestPointToSegment(point, edgePoints);
                        if (Float2.LengthSquared(point - closestPoint) < vertexTolerance)
                        {
                            onEdge = true;

                            Vertex2D newVertex = new Vertex2D
                            {
                                Point = point,
                                UV = CSGBrush.InterpolateSegmentUV(edgePoints, edgeUVs, point)
                            };
                            newVertexIdx = AddVertex(newVertex);
                            int oppositeVertexIdx = face.VertexIdx[(faceEdgeIdx + 2) % 3];

                            if (newVertexIdx == oppositeVertexIdx)
                            {
                                faces.RemoveAt(faceIdx);
                                --faceIdx;
                                break;
                            }

                            Float2[] splitEdge1 = { vertices[newVertexIdx].Point, edgePoints[0] };
                            Float2[] splitEdge2 = { vertices[newVertexIdx].Point, edgePoints[1] };
                            Float2[] newEdge = { vertices[newVertexIdx].Point, vertices[oppositeVertexIdx].Point };
                            if (CSGBrush.AreSegmentsParallel(splitEdge1, newEdge, vertexTolerance) &&
                                CSGBrush.AreSegmentsParallel(splitEdge2, newEdge, vertexTolerance))
                                break;

                            Face2D leftFace = new Face2D { VertexIdx = new int[3] };
                            leftFace.VertexIdx[0] = newVertexIdx;
                            leftFace.VertexIdx[1] = face.VertexIdx[(faceEdgeIdx + 1) % 3];
                            leftFace.VertexIdx[2] = oppositeVertexIdx;
                            Face2D rightFace = new Face2D { VertexIdx = new int[3] };
                            rightFace.VertexIdx[0] = oppositeVertexIdx;
                            rightFace.VertexIdx[1] = face.VertexIdx[faceEdgeIdx];
                            rightFace.VertexIdx[2] = newVertexIdx;
                            faces.RemoveAt(faceIdx);
                            faces.Insert(faceIdx, rightFace);
                            faces.Insert(faceIdx, leftFace);

                            ++faceIdx;
                            break;
                        }
                    }

                    if (!onEdge && IsPointInTriangle(point, faceVertices[0].Point, faceVertices[1].Point, faceVertices[2].Point))
                    {
                        Vertex2D newVertex = new Vertex2D
                        {
                            Point = point,
                            UV = CSGBrush.InterpolateTriangleUV(points, uvs, point)
                        };
                        newVertexIdx = AddVertex(newVertex);

                        for (int i = 0; i < 3; ++i)
                        {
                            Float2[] newPoints = { points[i], points[(i + 1) % 3], vertices[newVertexIdx].Point };
                            if (CSGBrush.IsTriangleDegenerate(newPoints, vertexTolerance))
                                continue;

                            Face2D newFace = new Face2D { VertexIdx = new int[3] };
                            newFace.VertexIdx[0] = face.VertexIdx[i];
                            newFace.VertexIdx[1] = face.VertexIdx[(i + 1) % 3];
                            newFace.VertexIdx[2] = newVertexIdx;
                            faces.Add(newFace);
                        }
                        faces.RemoveAt(faceIdx);
                        break;
                    }
                }

                return newVertexIdx;
            }

            public void Insert(CSGBrush brush, int faceIdx)
            {
                Float2[] points2D = new Float2[3];
                int pointsCount = 0;

                for (int i = 0; i < 3; i++)
                {
                    Float3 point3D = brush.Faces[faceIdx].Vertices[i];

                    if (plane.GetDistanceToPoint(point3D) < Intersection.INTERSECTION_EPSILON)
                    {
                        Float3 point2D = to2D.Xform(point3D);
                        points2D[pointsCount++] = new Float2(point2D.X, point2D.Y);
                    }
                    else
                    {
                        Float3 nextPoint3D = brush.Faces[faceIdx].Vertices[(i + 1) % 3];

                        if (plane.GetDistanceToPoint(nextPoint3D) >= Intersection.INTERSECTION_EPSILON)
                        {
                            bool side1 = plane.GetSide(point3D);
                            bool side2 = plane.GetSide(nextPoint3D);

                            if (side1 != side2)
                            {
                                Float3 point2D = Float3.Zero;
                                if (PlaneIntersectsSegment(plane, point3D, nextPoint3D, ref point2D))
                                {
                                    point2D = to2D.Xform(point2D);
                                    points2D[pointsCount++] = new Float2(point2D.X, point2D.Y);
                                }
                            }
                        }
                    }
                }

                List<int> segmentIndices = new List<int>();
                Float2[] segment = new Float2[2];
                int[] insertedIndex = { -1, -1, -1 };

                for (int i = 0; i < pointsCount; ++i)
                    insertedIndex[i] = InsertPoint(points2D[i]);

                if (pointsCount == 2)
                {
                    segment[0] = points2D[0];
                    segment[1] = points2D[1];
                    FindEdgeIntersections(segment, ref segmentIndices);
                    for (int i = 0; i < 2; ++i)
                        AddVertexIdxSorted(segmentIndices, insertedIndex[i]);
                    MergeFaces(segmentIndices);
                }

                if (pointsCount == 3)
                {
                    for (int edgeIdx = 0; edgeIdx < 3; ++edgeIdx)
                    {
                        segment[0] = points2D[edgeIdx];
                        segment[1] = points2D[(edgeIdx + 1) % 3];
                        FindEdgeIntersections(segment, ref segmentIndices);
                        for (int i = 0; i < 2; ++i)
                            AddVertexIdxSorted(segmentIndices, insertedIndex[(edgeIdx + i) % 3]);
                        MergeFaces(segmentIndices);
                        segmentIndices.Clear();
                    }
                }
            }

            public void AddFacesToMesh(ref MeshMerge meshMerge, bool fromB)
            {
                for (int faceIdx = 0; faceIdx < faces.Count; ++faceIdx)
                {
                    Face2D face = faces[faceIdx];
                    Vertex2D[] fv = {
                        vertices[face.VertexIdx[0]],
                        vertices[face.VertexIdx[1]],
                        vertices[face.VertexIdx[2]]
                    };

                    Float3[] points3D = new Float3[3];
                    Float2[] uvs = new Float2[3];
                    for (int i = 0; i < 3; ++i)
                    {
                        Float3 point2D = new Float3(fv[i].Point.X, fv[i].Point.Y, 0);
                        points3D[i] = to3D.Xform(point2D);
                        uvs[i] = fv[i].UV;
                    }

                    meshMerge.AddFace(points3D, uvs, fromB);
                }
            }
        }

        #endregion

        #region MeshMerge

        private class MeshMerge
        {
            private const int BVH_LIMIT = 10;

            private enum VISIT
            {
                TEST_AABB_BIT = 0,
                VISIT_LEFT_BIT = 1,
                VISIT_RIGHT_BIT = 2,
                VISIT_DONE_BIT = 3,
                VISITED_BIT_SHIFT = 29,
                NODE_IDX_MASK = (1 << 29) - 1,
            }

            public struct FaceBVH
            {
                public int Face;
                public int Left;
                public int Right;
                public int Next;
                public Float3 Center;
                public AABB Aabb;
            }

            public struct Face
            {
                public bool FromB;
                public int[] Points;
                public Float2[] UVs;
            }

            public List<Float3> Points = new List<Float3>();
            public List<Face> FacesA = new List<Face>();
            public List<Face> FacesB = new List<Face>();
            public float VertexSnap = 0.0f;
            private Dictionary<SnapKey, int> snapCache = new Dictionary<SnapKey, int>();

            private struct SnapKey : IEquatable<SnapKey>
            {
                public int X, Y, Z;

                public SnapKey(Float3 point, float snap)
                {
                    X = (int)Math.Round(((point.X + snap) * 0.31234) / snap);
                    Y = (int)Math.Round(((point.Y + snap) * 0.31234) / snap);
                    Z = (int)Math.Round(((point.Z + snap) * 0.31234) / snap);
                }

                public bool Equals(SnapKey other) => X == other.X && Y == other.Y && Z == other.Z;
                public override int GetHashCode() => HashCode.Combine(X, Y, Z);
            }

            public void AddFace(Float3[] points, Float2[] uvs, bool fromB)
            {
                int[] indices = new int[3];
                for (int i = 0; i < 3; i++)
                {
                    var key = new SnapKey(points[i], VertexSnap);
                    if (snapCache.TryGetValue(key, out int existing))
                    {
                        indices[i] = existing;
                    }
                    else
                    {
                        indices[i] = Points.Count;
                        Points.Add(points[i]);
                        snapCache[key] = indices[i];
                    }
                }

                if (indices[0] == indices[1] || indices[0] == indices[2] || indices[1] == indices[2])
                    return;

                var face = new Face
                {
                    FromB = fromB,
                    Points = indices,
                    UVs = (Float2[])uvs.Clone()
                };

                if (fromB)
                    FacesB.Add(face);
                else
                    FacesA.Add(face);
            }

            private class FaceBVHCmpX : IComparer
            {
                int IComparer.Compare(object? obj1, object? obj2)
                {
                    var p_left = ((int, FaceBVH))obj1!;
                    var p_right = ((int, FaceBVH))obj2!;
                    if (p_left.Item2.Center.X == p_right.Item2.Center.X) return 0;
                    return p_left.Item2.Center.X < p_right.Item2.Center.X ? -1 : 1;
                }
            }

            private class FaceBVHCmpY : IComparer
            {
                int IComparer.Compare(object? obj1, object? obj2)
                {
                    var p_left = ((int, FaceBVH))obj1!;
                    var p_right = ((int, FaceBVH))obj2!;
                    if (p_left.Item2.Center.Y == p_right.Item2.Center.Y) return 0;
                    return p_left.Item2.Center.Y < p_right.Item2.Center.Y ? -1 : 1;
                }
            }

            private class FaceBVHCmpZ : IComparer
            {
                int IComparer.Compare(object? obj1, object? obj2)
                {
                    var p_left = ((int, FaceBVH))obj1!;
                    var p_right = ((int, FaceBVH))obj2!;
                    if (p_left.Item2.Center.Z == p_right.Item2.Center.Z) return 0;
                    return p_left.Item2.Center.Z < p_right.Item2.Center.Z ? -1 : 1;
                }
            }

            private int CreateBVH(ref FaceBVH[] facebvh, ref (int, FaceBVH)[] idFacebvh, int from, int size, int depth, ref int maxDepth)
            {
                if (depth > maxDepth)
                    maxDepth = depth;

                if (size == 0)
                    return -1;

                if (size <= BVH_LIMIT)
                {
                    for (int i = 0; i < size - 1; i++)
                        facebvh[idFacebvh[from + i].Item1].Next = idFacebvh[from + i + 1].Item1;
                    return idFacebvh[from].Item1;
                }

                AABB aabb = facebvh[idFacebvh[from].Item1].Aabb;
                for (int i = 1; i < size; i++)
                    aabb.Encapsulate(idFacebvh[from + i].Item2.Aabb);

                int li = aabb.Size.X > aabb.Size.Y ? (aabb.Size.X > aabb.Size.Z ? 0 : 2) : (aabb.Size.Y > aabb.Size.Z ? 1 : 2);

                switch (li)
                {
                    case 0:
                        {
                            SortArray temp = new SortArray(new FaceBVHCmpX());
                            temp.NthElement(from, from + size, from + size / 2, ref idFacebvh);
                        }
                        break;
                    case 1:
                        {
                            SortArray temp = new SortArray(new FaceBVHCmpY());
                            temp.NthElement(from, from + size, from + size / 2, ref idFacebvh);
                        }
                        break;
                    case 2:
                        {
                            SortArray temp = new SortArray(new FaceBVHCmpZ());
                            temp.NthElement(from, from + size, from + size / 2, ref idFacebvh);
                        }
                        break;
                }

                int left = CreateBVH(ref facebvh, ref idFacebvh, from, size / 2, depth + 1, ref maxDepth);
                int right = CreateBVH(ref facebvh, ref idFacebvh, from + size / 2, size - size / 2, depth + 1, ref maxDepth);

                Array.Resize(ref facebvh, facebvh.Length + 1);
                facebvh[facebvh.Length - 1].Aabb = aabb;
                facebvh[facebvh.Length - 1].Center = aabb.Center;
                facebvh[facebvh.Length - 1].Face = -1;
                facebvh[facebvh.Length - 1].Left = left;
                facebvh[facebvh.Length - 1].Right = right;
                facebvh[facebvh.Length - 1].Next = -1;

                return facebvh.Length - 1;
            }

            private void AddDistance(ref List<float> intersectionsA, ref List<float> intersectionsB, bool fromB, float distance)
            {
                List<float> intersections = fromB ? intersectionsB : intersectionsA;

                foreach (float E in intersections)
                {
                    if (Maths.Abs(E - distance) < Intersection.INTERSECTION_EPSILON)
                        return;
                }

                intersections.Add(distance);
            }

            private bool BVHInside(ref FaceBVH[] facebvh, int maxDepth, int bvhFirst, int faceIdx, bool fromFacesA)
            {
                Face face = fromFacesA ? FacesA[faceIdx] : FacesB[faceIdx];

                Float3[] facePoints = {
                    Points[face.Points[0]],
                    Points[face.Points[1]],
                    Points[face.Points[2]]
                };
                Float3 faceCenter = (facePoints[0] + facePoints[1] + facePoints[2]) / 3.0f;
                Plane facePlane = new Plane(facePoints[0], facePoints[1], facePoints[2]);
                Float3 faceNormal = facePlane.Normal;

                int[] stack = new int[maxDepth];

                List<float> intersectionsA = new List<float>();
                List<float> intersectionsB = new List<float>();

                int level = 0;
                stack[0] = bvhFirst;

                while (true)
                {
                    int node = stack[level] & (int)VISIT.NODE_IDX_MASK;
                    FaceBVH? currentFacebvh = facebvh[node];
                    bool done = false;

                    switch (stack[level] >> (int)VISIT.VISITED_BIT_SHIFT)
                    {
                        case (int)VISIT.TEST_AABB_BIT:
                            {
                                if (((FaceBVH)currentFacebvh).Face >= 0)
                                {
                                    while (currentFacebvh != null)
                                    {
                                        FaceBVH current = (FaceBVH)currentFacebvh;
                                        // Check ray-AABB intersection
                                        if (RayIntersectsAABB(faceCenter, faceNormal, current.Aabb))
                                        {
                                            Face currentFace = fromFacesA ? FacesB[current.Face] : FacesA[current.Face];
                                            Float3[] currentPoints = {
                                                Points[currentFace.Points[0]],
                                                Points[currentFace.Points[1]],
                                                Points[currentFace.Points[2]]
                                            };
                                            Plane currentPlane = new Plane(currentPoints[0], currentPoints[1], currentPoints[2]);
                                            Float3 currentNormal = currentPlane.Normal;
                                            Float3 intersectionPoint = Float3.Zero;

                                            if (CSGBrush.IsEqualApprox(currentNormal, faceNormal) &&
                                                CSGBrush.IsPointInTriangle(faceCenter, currentPoints))
                                            {
                                                if (!face.FromB)
                                                    AddDistance(ref intersectionsA, ref intersectionsB, currentFace.FromB, 0);
                                            }
                                            else if (CSGBrush.RayIntersectsTriangle(faceCenter, faceNormal, currentPoints, Intersection.INTERSECTION_EPSILON, ref intersectionPoint))
                                            {
                                                float distance = Float3.Length(faceCenter - intersectionPoint);
                                                AddDistance(ref intersectionsA, ref intersectionsB, currentFace.FromB, distance);
                                            }
                                        }

                                        if (current.Next != -1)
                                            currentFacebvh = facebvh[current.Next];
                                        else
                                            currentFacebvh = null;
                                    }

                                    stack[level] = ((int)VISIT.VISIT_DONE_BIT << (int)VISIT.VISITED_BIT_SHIFT) | node;
                                }
                                else
                                {
                                    bool valid = RayIntersectsAABB(faceCenter, faceNormal, ((FaceBVH)currentFacebvh).Aabb);

                                    if (!valid)
                                        stack[level] = ((int)VISIT.VISIT_DONE_BIT << (int)VISIT.VISITED_BIT_SHIFT) | node;
                                    else
                                        stack[level] = ((int)VISIT.VISIT_LEFT_BIT << (int)VISIT.VISITED_BIT_SHIFT) | node;
                                }
                                continue;
                            }

                        case (int)VISIT.VISIT_LEFT_BIT:
                            {
                                stack[level] = ((int)VISIT.VISIT_RIGHT_BIT << (int)VISIT.VISITED_BIT_SHIFT) | node;
                                stack[level + 1] = ((FaceBVH)currentFacebvh).Left | ((int)VISIT.TEST_AABB_BIT << (int)VISIT.VISITED_BIT_SHIFT);
                                level++;
                                continue;
                            }

                        case (int)VISIT.VISIT_RIGHT_BIT:
                            {
                                stack[level] = ((int)VISIT.VISIT_DONE_BIT << (int)VISIT.VISITED_BIT_SHIFT) | node;
                                stack[level + 1] = ((FaceBVH)currentFacebvh).Right | ((int)VISIT.TEST_AABB_BIT << (int)VISIT.VISITED_BIT_SHIFT);
                                level++;
                                continue;
                            }

                        case (int)VISIT.VISIT_DONE_BIT:
                            {
                                if (level == 0)
                                {
                                    done = true;
                                    break;
                                }
                                else
                                {
                                    level--;
                                }
                                continue;
                            }
                    }

                    if (done)
                        break;
                }

                int res = (intersectionsA.Count + intersectionsB.Count) & 1;
                return res != 0;
            }

            private bool RayIntersectsAABB(Float3 origin, Float3 direction, AABB aabb)
            {
                return Prowl.Vector.Geometry.Intersection.RayAABB(origin, direction, aabb.Min, aabb.Max, out _, out _);
            }

            public void DoOperation(Operation operation, ref CSGBrush mergedBrush)
            {
                FaceBVH[] bvhvecA = new FaceBVH[FacesA.Count];
                FaceBVH[] facebvhA = bvhvecA;

                FaceBVH[] bvhvecB = new FaceBVH[FacesB.Count];
                FaceBVH[] facebvhB = bvhvecB;

                AABB aabbA = new AABB();
                AABB aabbB = new AABB();

                bool firstA = true;
                bool firstB = true;

                for (int i = 0; i < FacesA.Count; i++)
                {
                    facebvhA[i] = new FaceBVH();
                    facebvhA[i].Left = -1;
                    facebvhA[i].Right = -1;
                    facebvhA[i].Face = i;
                    facebvhA[i].Aabb = AABB.FromPoints(new[] {
                        Points[FacesA[i].Points[0]],
                        Points[FacesA[i].Points[1]],
                        Points[FacesA[i].Points[2]]
                    });
                    facebvhA[i].Center = facebvhA[i].Aabb.Center;
                    facebvhA[i].Aabb = facebvhA[i].Aabb.Expanded(VertexSnap);
                    facebvhA[i].Next = -1;
                    if (firstA)
                    {
                        aabbA = facebvhA[i].Aabb;
                        firstA = false;
                    }
                    else
                    {
                        aabbA.Encapsulate(facebvhA[i].Aabb);
                    }
                }

                for (int i = 0; i < FacesB.Count; i++)
                {
                    facebvhB[i] = new FaceBVH();
                    facebvhB[i].Left = -1;
                    facebvhB[i].Right = -1;
                    facebvhB[i].Face = i;
                    facebvhB[i].Aabb = AABB.FromPoints(new[] {
                        Points[FacesB[i].Points[0]],
                        Points[FacesB[i].Points[1]],
                        Points[FacesB[i].Points[2]]
                    });
                    facebvhB[i].Center = facebvhB[i].Aabb.Center;
                    facebvhB[i].Aabb = facebvhB[i].Aabb.Expanded(VertexSnap);
                    facebvhB[i].Next = -1;
                    if (firstB)
                    {
                        aabbB = facebvhB[i].Aabb;
                        firstB = false;
                    }
                    else
                    {
                        aabbB.Encapsulate(facebvhB[i].Aabb);
                    }
                }

                AABB intersectionAabb = aabbA.ClippedBy(aabbB);

                (int, FaceBVH)[] bvhtrvecA = new (int, FaceBVH)[FacesA.Count];
                for (int i = 0; i < FacesA.Count; i++)
                    bvhtrvecA[i] = (i, facebvhA[i]);

                (int, FaceBVH)[] bvhtrvecB = new (int, FaceBVH)[FacesB.Count];
                for (int i = 0; i < FacesB.Count; i++)
                    bvhtrvecB[i] = (i, facebvhB[i]);

                int maxDepthA = 0;
                int bvhRootA = CreateBVH(ref facebvhA, ref bvhtrvecA, 0, FacesA.Count, 1, ref maxDepthA);
                int maxAllocA = facebvhA.Length;

                int maxDepthB = 0;
                int bvhRootB = CreateBVH(ref facebvhB, ref bvhtrvecB, 0, FacesB.Count, 1, ref maxDepthB);
                int maxAllocB = facebvhB.Length;

                switch (operation)
                {
                    case Operation.Union:
                        {
                            int facesCount = 0;
                            Array.Resize(ref mergedBrush.Faces, FacesA.Count + FacesB.Count);

                            for (int i = 0; i < FacesA.Count; i++)
                            {
                                if (!intersectionAabb.Intersects(facebvhA[i].Aabb))
                                {
                                    mergedBrush.Faces[facesCount].Vertices = new List<Float3>(3);
                                    mergedBrush.Faces[facesCount].Vertices.Add(Points[FacesA[i].Points[0]]);
                                    mergedBrush.Faces[facesCount].Vertices.Add(Points[FacesA[i].Points[1]]);
                                    mergedBrush.Faces[facesCount].Vertices.Add(Points[FacesA[i].Points[2]]);
                                    mergedBrush.Faces[facesCount].UVs = new Float2[3] { FacesA[i].UVs[0], FacesA[i].UVs[1], FacesA[i].UVs[2] };
                                    facesCount++;
                                    continue;
                                }

                                if (!BVHInside(ref facebvhB, maxDepthB, maxAllocB - 1, i, true))
                                {
                                    mergedBrush.Faces[facesCount].Vertices = new List<Float3>(3);
                                    mergedBrush.Faces[facesCount].Vertices.Add(Points[FacesA[i].Points[0]]);
                                    mergedBrush.Faces[facesCount].Vertices.Add(Points[FacesA[i].Points[1]]);
                                    mergedBrush.Faces[facesCount].Vertices.Add(Points[FacesA[i].Points[2]]);
                                    mergedBrush.Faces[facesCount].UVs = new Float2[3] { FacesA[i].UVs[0], FacesA[i].UVs[1], FacesA[i].UVs[2] };
                                    facesCount++;
                                }
                            }

                            for (int i = 0; i < FacesB.Count; i++)
                            {
                                if (!intersectionAabb.Intersects(facebvhB[i].Aabb))
                                {
                                    mergedBrush.Faces[facesCount].Vertices = new List<Float3>(3);
                                    mergedBrush.Faces[facesCount].Vertices.Add(Points[FacesB[i].Points[0]]);
                                    mergedBrush.Faces[facesCount].Vertices.Add(Points[FacesB[i].Points[1]]);
                                    mergedBrush.Faces[facesCount].Vertices.Add(Points[FacesB[i].Points[2]]);
                                    mergedBrush.Faces[facesCount].UVs = new Float2[3] { FacesB[i].UVs[0], FacesB[i].UVs[1], FacesB[i].UVs[2] };
                                    facesCount++;
                                    continue;
                                }

                                if (!BVHInside(ref facebvhA, maxDepthA, maxAllocA - 1, i, false))
                                {
                                    mergedBrush.Faces[facesCount].Vertices = new List<Float3>(3);
                                    mergedBrush.Faces[facesCount].Vertices.Add(Points[FacesB[i].Points[0]]);
                                    mergedBrush.Faces[facesCount].Vertices.Add(Points[FacesB[i].Points[1]]);
                                    mergedBrush.Faces[facesCount].Vertices.Add(Points[FacesB[i].Points[2]]);
                                    mergedBrush.Faces[facesCount].UVs = new Float2[3] { FacesB[i].UVs[0], FacesB[i].UVs[1], FacesB[i].UVs[2] };
                                    facesCount++;
                                }
                            }
                            Array.Resize(ref mergedBrush.Faces, facesCount);
                        }
                        break;

                    case Operation.Intersection:
                        {
                            int facesCount = 0;
                            Array.Resize(ref mergedBrush.Faces, FacesA.Count + FacesB.Count);

                            for (int i = 0; i < FacesA.Count; i++)
                            {
                                if (!intersectionAabb.Intersects(facebvhA[i].Aabb))
                                    continue;

                                if (BVHInside(ref facebvhB, maxDepthB, maxAllocB - 1, i, true))
                                {
                                    mergedBrush.Faces[facesCount].Vertices = new List<Float3>(3);
                                    mergedBrush.Faces[facesCount].Vertices.Add(Points[FacesA[i].Points[0]]);
                                    mergedBrush.Faces[facesCount].Vertices.Add(Points[FacesA[i].Points[1]]);
                                    mergedBrush.Faces[facesCount].Vertices.Add(Points[FacesA[i].Points[2]]);
                                    mergedBrush.Faces[facesCount].UVs = new Float2[3] { FacesA[i].UVs[0], FacesA[i].UVs[1], FacesA[i].UVs[2] };
                                    facesCount++;
                                }
                            }

                            for (int i = 0; i < FacesB.Count; i++)
                            {
                                if (!intersectionAabb.Intersects(facebvhB[i].Aabb))
                                    continue;

                                if (BVHInside(ref facebvhA, maxDepthA, maxAllocA - 1, i, false))
                                {
                                    mergedBrush.Faces[facesCount].Vertices = new List<Float3>(3);
                                    mergedBrush.Faces[facesCount].Vertices.Add(Points[FacesB[i].Points[0]]);
                                    mergedBrush.Faces[facesCount].Vertices.Add(Points[FacesB[i].Points[1]]);
                                    mergedBrush.Faces[facesCount].Vertices.Add(Points[FacesB[i].Points[2]]);
                                    mergedBrush.Faces[facesCount].UVs = new Float2[3] { FacesB[i].UVs[0], FacesB[i].UVs[1], FacesB[i].UVs[2] };
                                    facesCount++;
                                }
                            }
                            Array.Resize(ref mergedBrush.Faces, facesCount);
                        }
                        break;

                    case Operation.Subtraction:
                        {
                            int facesCount = 0;
                            Array.Resize(ref mergedBrush.Faces, FacesA.Count + FacesB.Count);

                            for (int i = 0; i < FacesA.Count; i++)
                            {
                                if (!intersectionAabb.Intersects(facebvhA[i].Aabb))
                                {
                                    mergedBrush.Faces[facesCount].Vertices = new List<Float3>(3);
                                    mergedBrush.Faces[facesCount].Vertices.Add(Points[FacesA[i].Points[0]]);
                                    mergedBrush.Faces[facesCount].Vertices.Add(Points[FacesA[i].Points[1]]);
                                    mergedBrush.Faces[facesCount].Vertices.Add(Points[FacesA[i].Points[2]]);
                                    mergedBrush.Faces[facesCount].UVs = new Float2[3] { FacesA[i].UVs[0], FacesA[i].UVs[1], FacesA[i].UVs[2] };
                                    facesCount++;
                                    continue;
                                }

                                if (!BVHInside(ref facebvhB, maxDepthB, maxAllocB - 1, i, true))
                                {
                                    mergedBrush.Faces[facesCount].Vertices = new List<Float3>(3);
                                    mergedBrush.Faces[facesCount].Vertices.Add(Points[FacesA[i].Points[0]]);
                                    mergedBrush.Faces[facesCount].Vertices.Add(Points[FacesA[i].Points[1]]);
                                    mergedBrush.Faces[facesCount].Vertices.Add(Points[FacesA[i].Points[2]]);
                                    mergedBrush.Faces[facesCount].UVs = new Float2[3] { FacesA[i].UVs[0], FacesA[i].UVs[1], FacesA[i].UVs[2] };
                                    facesCount++;
                                }
                            }

                            for (int i = 0; i < FacesB.Count; i++)
                            {
                                if (!intersectionAabb.Intersects(facebvhB[i].Aabb))
                                    continue;

                                if (BVHInside(ref facebvhA, maxDepthA, maxAllocA - 1, i, false))
                                {
                                    mergedBrush.Faces[facesCount].Vertices = new List<Float3>(3);
                                    // Flip winding order for subtraction
                                    mergedBrush.Faces[facesCount].Vertices.Add(Points[FacesB[i].Points[1]]);
                                    mergedBrush.Faces[facesCount].Vertices.Add(Points[FacesB[i].Points[0]]);
                                    mergedBrush.Faces[facesCount].Vertices.Add(Points[FacesB[i].Points[2]]);
                                    mergedBrush.Faces[facesCount].UVs = new Float2[3] { FacesB[i].UVs[0], FacesB[i].UVs[1], FacesB[i].UVs[2] };
                                    facesCount++;
                                }
                            }
                            Array.Resize(ref mergedBrush.Faces, facesCount);
                        }
                        break;
                }
            }
        }

        #endregion

        #region SortArray

        private class SortArray
        {
            private IComparer compare;

            public SortArray(IComparer comp)
            {
                this.compare = comp;
            }

            private int Bitlog(int n)
            {
                int k;
                for (k = 0; n != 1; n >>= 1)
                    ++k;
                return k;
            }

            public void NthElement(int first, int last, int nth, ref (int, MeshMerge.FaceBVH)[] array)
            {
                if (first == last || nth == last)
                    return;
                Introselect(first, nth, last, ref array, Bitlog(last - first) * 2);
            }

            private void Introselect(int first, int nth, int last, ref (int, MeshMerge.FaceBVH)[] array, int maxDepth)
            {
                while (last - first > 3)
                {
                    if (maxDepth == 0)
                    {
                        PartialSelect(first, nth + 1, last, ref array);
                        var temps = array[first];
                        array[first] = array[nth];
                        array[nth] = temps;
                        return;
                    }

                    maxDepth--;

                    int cut = Partitioner(
                            first,
                            last,
                            MedianOf3(
                                    array[first],
                                    array[first + (last - first) / 2],
                                    array[last - 1]),
                            ref array);

                    if (cut <= nth)
                        first = cut;
                    else
                        last = cut;
                }
                InsertionSort(first, last, ref array);
            }

            private void InsertionSort(int first, int last, ref (int, MeshMerge.FaceBVH)[] array)
            {
                if (first == last)
                    return;
                for (int i = first + 1; i != last; i++)
                    LinearInsert(first, i, ref array);
            }

            private void LinearInsert(int first, int last, ref (int, MeshMerge.FaceBVH)[] array)
            {
                var val = array[last];
                if (compare.Compare(val, array[first]) < 0)
                {
                    for (int i = last; i > first; i--)
                        array[i] = array[i - 1];
                    array[first] = val;
                }
                else
                {
                    UnguardedLinearInsert(last, val, ref array);
                }
            }

            private void UnguardedLinearInsert(int last, (int, MeshMerge.FaceBVH) value, ref (int, MeshMerge.FaceBVH)[] array)
            {
                int next = last - 1;
                while (compare.Compare(value, array[next]) < 0)
                {
                    array[last] = array[next];
                    last = next;
                    next--;
                }
                array[last] = value;
            }

            private (int, MeshMerge.FaceBVH) MedianOf3((int, MeshMerge.FaceBVH) a, (int, MeshMerge.FaceBVH) b, (int, MeshMerge.FaceBVH) c)
            {
                if (compare.Compare(a, b) < 0)
                {
                    if (compare.Compare(b, c) < 0)
                        return b;
                    else if (compare.Compare(a, c) < 0)
                        return c;
                    else
                        return a;
                }
                else if (compare.Compare(a, c) < 0)
                {
                    return a;
                }
                else if (compare.Compare(b, c) < 0)
                {
                    return c;
                }
                else
                {
                    return b;
                }
            }

            private void PartialSelect(int first, int last, int middle, ref (int, MeshMerge.FaceBVH)[] array)
            {
                MakeHeap(first, middle, ref array);
                for (int i = middle; i < last; i++)
                {
                    if (compare.Compare(array[i], array[first]) < 0)
                        PopHeap(first, middle, i, array[i], ref array);
                }
            }

            private void PopHeap(int first, int last, int result, (int, MeshMerge.FaceBVH) value, ref (int, MeshMerge.FaceBVH)[] array)
            {
                array[result] = array[first];
                AdjustHeap(first, 0, last - first, value, ref array);
            }

            private void MakeHeap(int first, int last, ref (int, MeshMerge.FaceBVH)[] array)
            {
                if (last - first < 2)
                    return;
                int len = last - first;
                int parent = (len - 2) / 2;
                while (true)
                {
                    AdjustHeap(first, parent, len, array[first + parent], ref array);
                    if (parent == 0)
                        return;
                    parent--;
                }
            }

            private void AdjustHeap(int first, int holeIdx, int len, (int, MeshMerge.FaceBVH) value, ref (int, MeshMerge.FaceBVH)[] array)
            {
                int topIndex = holeIdx;
                int secondChild = 2 * holeIdx + 2;

                while (secondChild < len)
                {
                    if (compare.Compare(array[first + secondChild], array[first + (secondChild - 1)]) < 0)
                        secondChild--;
                    array[first + holeIdx] = array[first + secondChild];
                    holeIdx = secondChild;
                    secondChild = 2 * (secondChild + 1);
                }

                if (secondChild == len)
                {
                    array[first + holeIdx] = array[first + (secondChild - 1)];
                    holeIdx = secondChild - 1;
                }
                PushHeap(first, holeIdx, topIndex, value, ref array);
            }

            private void PushHeap(int first, int holeIdx, int topIndex, (int, MeshMerge.FaceBVH) value, ref (int, MeshMerge.FaceBVH)[] array)
            {
                int parent = (holeIdx - 1) / 2;
                while (holeIdx > topIndex && compare.Compare(array[first + parent], value) < 0)
                {
                    array[first + holeIdx] = array[first + parent];
                    holeIdx = parent;
                    parent = (holeIdx - 1) / 2;
                }
                array[first + holeIdx] = value;
            }

            private int Partitioner(int first, int last, (int, MeshMerge.FaceBVH) pivot, ref (int, MeshMerge.FaceBVH)[] array)
            {
                while (true)
                {
                    while (compare.Compare(array[first], pivot) < 0)
                        first++;
                    last--;
                    while (compare.Compare(pivot, array[last]) < 0)
                        last--;
                    if (!(first < last))
                        return first;
                    var temps = array[first];
                    array[first] = array[last];
                    array[last] = temps;
                    first++;
                }
            }
        }

        #endregion

        #region CSGBrushOperation

        private class CSGBrushOperation
        {
            public struct Build2DFaceCollection
            {
                public Dictionary<int, Build2DFaces> Build2DFacesA;
                public Dictionary<int, Build2DFaces> Build2DFacesB;
            }

            public void MergeBrushes(Operation operation, CSGBrush brushA, CSGBrush brushB, ref CSGBrush mergedBrush)
            {
                Build2DFaceCollection build2DFaceCollection;
                build2DFaceCollection.Build2DFacesA = new Dictionary<int, Build2DFaces>();
                build2DFaceCollection.Build2DFacesB = new Dictionary<int, Build2DFaces>();

                for (int i = 0; i < brushA.Faces.Length; i++)
                {
                    for (int j = 0; j < brushB.Faces.Length; j++)
                    {
                        UpdateFaces(ref brushA, i, ref brushB, j, ref build2DFaceCollection, Intersection.INTERSECTION_EPSILON);
                    }
                }

                MeshMerge meshMerge = new MeshMerge { VertexSnap = Intersection.INTERSECTION_EPSILON };

                for (int i = 0; i < brushA.Faces.Length; i++)
                {
                    if (build2DFaceCollection.Build2DFacesA.TryGetValue(i, out Build2DFaces? value))
                    {
                        value.AddFacesToMesh(ref meshMerge, false);
                    }
                    else
                    {
                        Float3[] points = new Float3[3];
                        Float2[] uvs = new Float2[3];
                        for (int j = 0; j < 3; j++)
                        {
                            points[j] = brushA.Faces[i].Vertices[j];
                            uvs[j] = brushA.Faces[i].UVs[j];
                        }
                        meshMerge.AddFace(points, uvs, false);
                    }
                }

                for (int i = 0; i < brushB.Faces.Length; i++)
                {
                    if (build2DFaceCollection.Build2DFacesB.TryGetValue(i, out Build2DFaces? value))
                    {
                        value.AddFacesToMesh(ref meshMerge, true);
                    }
                    else
                    {
                        Float3[] points = new Float3[3];
                        Float2[] uvs = new Float2[3];
                        for (int j = 0; j < 3; j++)
                        {
                            points[j] = brushB.Faces[i].Vertices[j];
                            uvs[j] = brushB.Faces[i].UVs[j];
                        }
                        meshMerge.AddFace(points, uvs, true);
                    }
                }

                meshMerge.DoOperation(operation, ref mergedBrush);
            }

            private void UpdateFaces(ref CSGBrush brushA, int fIdxA, ref CSGBrush brushB, int fIdxB, ref Build2DFaceCollection collection, float vertexSnap)
            {
                var vA = brushA.Faces[fIdxA].Vertices;
                var vB = brushB.Faces[fIdxB].Vertices;

                // Check if triangle A is degenerate
                float snapSq = vertexSnap * vertexSnap; // Compute once instead of 6 times
                if (Float3.LengthSquared(vA[0] - vA[1]) < snapSq ||
                    Float3.LengthSquared(vA[0] - vA[2]) < snapSq ||
                    Float3.LengthSquared(vA[1] - vA[2]) < snapSq)
                {
                    collection.Build2DFacesA[fIdxA] = new Build2DFaces();
                    return;
                }

                // Check if triangle B is degenerate
                if (Float3.LengthSquared(vB[0] - vB[1]) < snapSq ||
                    Float3.LengthSquared(vB[0] - vB[2]) < snapSq ||
                    Float3.LengthSquared(vB[1] - vB[2]) < snapSq)
                {
                    collection.Build2DFacesB[fIdxB] = new Build2DFaces();
                    return;
                }

                if (!Intersection.TriangleTriangle(vA[0], vA[1], vA[2], vB[0], vB[1], vB[2]))
                {
                    return; // No Collision, dont need to process these two triangles
                }

                if (!collection.Build2DFacesA.ContainsKey(fIdxA))
                    collection.Build2DFacesA.Add(fIdxA, new Build2DFaces(brushA, fIdxA));
                collection.Build2DFacesA[fIdxA].Insert(brushB, fIdxB);

                if (!collection.Build2DFacesB.ContainsKey(fIdxB))
                    collection.Build2DFacesB.Add(fIdxB, new Build2DFaces(brushB, fIdxB));
                collection.Build2DFacesB[fIdxB].Insert(brushA, fIdxA);
            }
        }

        #endregion

        #region Conversion Methods

        private static CSGBrush GeometryDataToBrush(GeometryData geom)
        {
            var brush = new CSGBrush();
            var facesList = new List<CSGBrush.Face>();

            // All faces are already validated as triangles
            foreach (var face in geom.Faces)
            {
                var triangleData = GetTriangleData(face);

                CSGBrush.Face brushFace = new CSGBrush.Face();
                brushFace.Vertices = new List<Float3>(triangleData.Vertices);
                brushFace.UVs = triangleData.UVs;
                facesList.Add(brushFace);
            }

            brush.Faces = facesList.ToArray();
            return brush;
        }



        /// <summary>
        /// Gets triangle data for a face, including vertex positions and UVs.
        /// Assumes the face is a triangle - use ValidateTriangulated() first.
        /// </summary>
        public struct TriangleData
        {
            public Float3[] Vertices; // Always 3 vertices
            public Float2[] UVs;      // Always 3 UVs
            public GeometryData.Face Face;

            public TriangleData(Float3[] vertices, Float2[] uvs, GeometryData.Face face)
            {
                Vertices = vertices;
                UVs = uvs;
                Face = face;
            }
        }

        /// <summary>
        /// Extracts triangle data from a face. Throws if face is not a triangle.
        /// </summary>
        public static TriangleData GetTriangleData(GeometryData.Face face)
        {
            if (face.VertCount != 3)
                throw new InvalidOperationException("Face must be a triangle");

            var verts = face.NeighborVertices();
            var vertices = new Float3[3];
            var uvs = new Float2[3];

            for (int i = 0; i < 3; i++)
            {
                vertices[i] = verts[i].Point;

                var loop = face.GetLoop(verts[i]);
                if (loop != null && loop.Attributes.TryGetValue("uv", out var uvAttr))
                {
                    var floatAttr = uvAttr.AsFloat();
                    if (floatAttr != null && floatAttr.Data.Length >= 2)
                    {
                        uvs[i] = new Float2(floatAttr.Data[0], floatAttr.Data[1]);
                    }
                }
                else
                {
                    uvs[i] = Float2.Zero;
                }
            }

            return new TriangleData(vertices, uvs, face);
        }

        private static GeometryData BrushToGeometryData(CSGBrush brush)
        {
            var geom = new GeometryData();
            geom.AddLoopAttribute("uv", GeometryData.AttributeBaseType.Float, 2);

            var vertexMap = new Dictionary<Float3, GeometryData.Vertex>();

            foreach (var face in brush.Faces)
            {
                if (face.Vertices == null || face.Vertices.Count < 3) continue;

                var geomVerts = new GeometryData.Vertex[face.Vertices.Count];
                for (int i = 0; i < face.Vertices.Count; i++)
                {
                    if (!vertexMap.TryGetValue(face.Vertices[i], out var vert))
                    {
                        vert = geom.AddVertex(face.Vertices[i]);
                        vertexMap[face.Vertices[i]] = vert;
                    }
                    geomVerts[i] = vert;
                }

                var geomFace = geom.AddFace(geomVerts);
                if (geomFace != null && geomFace.Loop != null)
                {
                    var loop = geomFace.Loop;
                    for (int i = 0; i < face.Vertices.Count; i++)
                    {
                        if (loop != null && i < face.UVs.Length)
                        {
                            loop.Attributes["uv"] = new GeometryData.FloatAttributeValue(
                                face.UVs[i].X, face.UVs[i].Y
                            );
                        }
                        loop = loop.Next;
                    }
                }
            }

            return geom;
        }

        #endregion
    }
}

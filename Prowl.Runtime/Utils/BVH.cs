// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Generic;
using System.Linq;

using Veldrid;

namespace Prowl.Runtime.Utils;

public class BVHTriangle
{
    public Vector3 V0, V1, V2;
    public Vector3 Normal;
    public int MeshIndex;
    public int TriangleIndex;

    public BVHTriangle() { }

    public BVHTriangle(Vector3 v0, Vector3 v1, Vector3 v2, int meshIndex, int triIndex)
    {
        V0 = v0;
        V1 = v1;
        V2 = v2;
        MeshIndex = meshIndex;
        TriangleIndex = triIndex;

        // Calculate face normal
        Vector3 edge1 = V1 - V0;
        Vector3 edge2 = V2 - V0;
        Normal = Vector3.Normalize(Vector3.Cross(edge1, edge2));
    }

    public Bounds GetBounds()
    {
        var min = Vector3.Min(Vector3.Min(V0, V1), V2);
        var max = Vector3.Max(Vector3.Max(V0, V1), V2);
        return Bounds.CreateFromMinMax(min, max);
    }
}

public class BVHNode
{
    public Bounds Bounds;
    public BVHNode Left;
    public BVHNode Right;
    public List<BVHTriangle> Triangles;
    public bool IsLeaf => Triangles != null;

    public BVHNode() { }

    public struct RayHit
    {
        public double Distance;
        public Vector3 Normal;
        public int MeshIndex;
        public int TriangleIndex;
        public Vector3 Point;
        public bool HasHit;
    }

    /// <summary>
    /// Intersects the BVH with a ray and returns the closest hit.
    /// </summary>
    public RayHit Intersect(Ray ray, double currentMin = double.MaxValue)
    {
        // First check if we hit the node's bounding box
        double? boxHit = ray.Intersects(Bounds);
        if (!boxHit.HasValue || boxHit.Value > currentMin)
            return new RayHit { HasHit = false };

        RayHit closestHit = new()
        {
            HasHit = false,
            Distance = double.MaxValue
        };

        if (IsLeaf)
        {
            // Test all triangles in this leaf node
            foreach (BVHTriangle triangle in Triangles)
            {
                double? hit = ray.Intersects(triangle.V0, triangle.V1, triangle.V2);
                if (hit.HasValue && hit.Value < currentMin && hit.Value < closestHit.Distance)
                {
                    closestHit.HasHit = true;
                    closestHit.Distance = hit.Value;
                    closestHit.MeshIndex = triangle.MeshIndex;
                    closestHit.TriangleIndex = triangle.TriangleIndex;
                    closestHit.Normal = triangle.Normal;
                    closestHit.Point = ray.Position(hit.Value);
                }
            }
        }
        else
        {
            // Not a leaf node, so recursively check children
            // Check the closest child first for better performance
            BVHNode firstNode, secondNode;

            // Determine which child is closer to the ray origin
            Vector3 dirToLeftCenter = Left.Bounds.center - ray.origin;
            Vector3 dirToRightCenter = Right.Bounds.center - ray.origin;
            double distToLeft = Vector3.Dot(dirToLeftCenter, dirToLeftCenter);
            double distToRight = Vector3.Dot(dirToRightCenter, dirToRightCenter);

            if (distToLeft < distToRight)
            {
                firstNode = Left;
                secondNode = Right;
            }
            else
            {
                firstNode = Right;
                secondNode = Left;
            }

            // Check closer node first
            RayHit hit1 = firstNode.Intersect(ray, currentMin);
            if (hit1.HasHit)
            {
                closestHit = hit1;
                currentMin = hit1.Distance; // Update currentMin for early exit
            }

            // Only check second node if we could potentially find a closer hit
            RayHit hit2 = secondNode.Intersect(ray, currentMin);
            if (hit2.HasHit && hit2.Distance < closestHit.Distance)
            {
                closestHit = hit2;
            }
        }

        return closestHit;
    }

    /// <summary>
    /// Returns true if the ray intersects any triangle in the BVH.
    /// Great for shadow rays.
    /// </summary>
    public bool IntersectAny(Ray ray, double maxDistance = double.MaxValue)
    {
        double? boxHit = ray.Intersects(Bounds);
        if (!boxHit.HasValue || boxHit.Value > maxDistance)
            return false;

        if (IsLeaf)
        {
            foreach (BVHTriangle triangle in Triangles)
            {
                double? hit = ray.Intersects(triangle.V0, triangle.V1, triangle.V2);
                if (hit.HasValue && hit.Value < maxDistance)
                    return true;
            }
            return false;
        }

        // Check children
        return Left.IntersectAny(ray, maxDistance) || Right.IntersectAny(ray, maxDistance);
    }
}

public static class BVHConstructor
{

    //// Find all Static Meshes
    //List<Mesh> meshes = [];
    //List<Matrix4x4> matrices = [];
    //foreach (GameObject g in StaticObjects)
    //{
    //    MeshRenderer? renderer = g.GetComponent<MeshRenderer>();
    //    if (renderer != null && renderer.Mesh.IsAvailable)
    //    {
    //        meshes.Add(renderer.Mesh.Res);
    //        matrices.Add(g.Transform.localToWorldMatrix);
    //    }
    //}
    //
    //_bvh = BVHConstructor.BuildBVH(meshes, matrices);

    /// <summary>
    /// Builds a BVH from a list of meshes.
    /// Optionally, you can provide a list of matrices to transform the vertices of each mesh.
    ///
    /// This is mostly untested and may contain bugs.
    /// </summary>
    public static BVHNode BuildBVH(IEnumerable<Mesh> meshes, IEnumerable<Matrix4x4>? matrices = null, int maxTrianglePerLeaf = 4)
    {
        // Convert all meshes to triangles
        List<BVHTriangle> allTriangles = [];
        int meshIndex = 0;
        foreach (Mesh mesh in meshes)
        {
            System.Numerics.Vector3[] vertices = mesh.Vertices;
            int[] indices = mesh.IndexFormat == IndexFormat.UInt16 ?
                mesh.Indices16.Select(x => (int)x).ToArray() :
                mesh.Indices32.Select(x => (int)x).ToArray();

            for (int i = 0; i < indices.Length; i += 3)
            {
                Vector3 a = (Vector3)vertices[indices[i]];
                Vector3 b = (Vector3)vertices[indices[i + 1]];
                Vector3 c = (Vector3)vertices[indices[i + 2]];

                // Transform vertices by mesh matrix
                if (matrices != null)
                {
                    Matrix4x4 matrix = matrices.ElementAt(meshIndex);
                    a = Vector3.Transform(a, matrix);
                    b = Vector3.Transform(b, matrix);
                    c = Vector3.Transform(c, matrix);
                }

                var triangle = new BVHTriangle(
                    vertices[indices[i]],
                    vertices[indices[i + 1]],
                    vertices[indices[i + 2]],
                    meshIndex,
                    i / 3
                );
                allTriangles.Add(triangle);
            }

            meshIndex++;
        }

        return BuildBVHRecursive(allTriangles, maxTrianglePerLeaf);
    }

    private static BVHNode BuildBVHRecursive(List<BVHTriangle> triangles, int maxTrianglePerLeaf, int depth = 0)
    {
        var node = new BVHNode
        {
            // Calculate bounds for all triangles
            Bounds = GetBoundsForTriangles(triangles)
        };

        // If we have few enough triangles, make this a leaf node
        if (triangles.Count <= maxTrianglePerLeaf)
        {
            node.Triangles = triangles;
            return node;
        }

        // Find the axis with the largest extent
        Vector3 extent = node.Bounds.max - node.Bounds.min;
        int axis = 0;
        if (extent.y > extent.x) axis = 1;
        if (extent.z > extent[axis]) axis = 2;

        // Sort triangles based on their centroid along the chosen axis
        List<BVHTriangle> sortedTriangles = triangles.OrderBy(t =>
        {
            Vector3 centroid = (t.V0 + t.V1 + t.V2) * (1.0f / 3.0f);
            return centroid[axis];
        }).ToList();

        // Split triangles into two groups
        int mid = sortedTriangles.Count / 2;
        List<BVHTriangle> leftTriangles = sortedTriangles.GetRange(0, mid);
        List<BVHTriangle> rightTriangles = sortedTriangles.GetRange(mid, sortedTriangles.Count - mid);

        // Recursively build children
        node.Left = BuildBVHRecursive(leftTriangles, maxTrianglePerLeaf, depth + 1);
        node.Right = BuildBVHRecursive(rightTriangles, maxTrianglePerLeaf, depth + 1);

        return node;
    }

    private static Bounds GetBoundsForTriangles(List<BVHTriangle> triangles)
    {
        if (triangles.Count == 0)
            return new Bounds();

        Bounds bounds = triangles[0].GetBounds();
        for (int i = 1; i < triangles.Count; i++)
        {
            Bounds triBounds = triangles[i].GetBounds();
            bounds.min = Vector3.Min(bounds.min, triBounds.min);
            bounds.max = Vector3.Max(bounds.max, triBounds.max);
        }
        return bounds;
    }
}

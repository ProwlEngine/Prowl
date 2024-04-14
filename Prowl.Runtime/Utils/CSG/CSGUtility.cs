using System;
using System.Collections.Generic;

namespace Prowl.Runtime.CSG
{
    public enum CSGOperation { Union, Intersection, Subtraction, };

    public static class CSGUtility
    {
        struct Build2DFaceCollection
        {
            public Dictionary<int, Build2DFaces> build2DFacesA;
            public Dictionary<int, Build2DFaces> build2DFacesB;
        };

        public static void MergeBrushes(ref CSGBrush merged_brush, CSGOperation operation, CSGBrush brush_a, CSGBrush brush_b, double tolerance = 0.00001f)
        {
            Build2DFaceCollection build2DFaceCollection;
            build2DFaceCollection.build2DFacesA = new Dictionary<int, Build2DFaces>(brush_a.faces.Length);
            build2DFaceCollection.build2DFacesB = new Dictionary<int, Build2DFaces>(brush_b.faces.Length);
            brush_a.RegenerateFaceAABBs();
            brush_b.RegenerateFaceAABBs();

            for (int i = 0; i < brush_a.faces.Length; i++)
                for (int j = 0; j < brush_b.faces.Length; j++)
                    if (brush_a.faces[i].aabb.IntersectInclusive(brush_b.faces[j].aabb))
                        UpdateFace(ref brush_a, i, ref brush_b, j, ref build2DFaceCollection, tolerance);

            // Add faces to MeshMerge.
            MeshMerge mesh_merge = new MeshMerge(brush_a.faces.Length + build2DFaceCollection.build2DFacesA.Count, brush_b.faces.Length + build2DFaceCollection.build2DFacesB.Count);
            mesh_merge.vertex_snap = tolerance;
            mesh_merge.scale_a = brush_a.obj.Transform.localScale;

            for (int i = 0; i < brush_a.faces.Length; i++)
            {
                if (build2DFaceCollection.build2DFacesA.ContainsKey(i))
                {
                    build2DFaceCollection.build2DFacesA[i].AddFacesToMesh(ref mesh_merge, false);
                }
                else
                {
                    Vector3[] points = new Vector3[3];
                    Vector2[] uvs = new Vector2[3];
                    for (int j = 0; j < 3; j++)
                    {
                        points[j] = brush_a.faces[i].vertices[j];
                        uvs[j] = brush_a.faces[i].uvs[j];
                    }
                    mesh_merge.AddFace(points, uvs, false);
                }
            }

            for (int i = 0; i < brush_b.faces.Length; i++)
            {
                if (build2DFaceCollection.build2DFacesB.ContainsKey(i))
                {
                    build2DFaceCollection.build2DFacesB[i].AddFacesToMesh(ref mesh_merge, true);
                }
                else
                {
                    Vector3[] points = new Vector3[3];
                    Vector2[] uvs = new Vector2[3];
                    for (int j = 0; j < 3; j++)
                    {
                        points[j] = brush_a.obj.Transform.InverseTransformPoint(brush_b.obj.Transform.TransformPoint(brush_b.faces[i].vertices[j]));
                        uvs[j] = brush_b.faces[i].uvs[j];
                    }
                    mesh_merge.AddFace(points, uvs, true);
                }
            }

            Array.Clear(merged_brush.faces, 0, merged_brush.faces.Length);
            mesh_merge.PerformOperation(operation, ref merged_brush);
            mesh_merge = null;
            System.GC.Collect();
        }

        private static void UpdateFace(ref CSGBrush brush_a, int face_idx_a, ref CSGBrush brush_b, int face_idx_b, ref Build2DFaceCollection collection, double vertex_snap)
        {
            Vector3[] vertices_a = {
                brush_a.faces[face_idx_a].vertices[0],
                brush_a.faces[face_idx_a].vertices[1],
                brush_a.faces[face_idx_a].vertices[2]
            };

            Vector3[] vertices_b = {
                brush_a.obj.Transform.InverseTransformPoint(brush_b.obj.Transform.TransformPoint(brush_b.faces[face_idx_b].vertices[0])),
                brush_a.obj.Transform.InverseTransformPoint(brush_b.obj.Transform.TransformPoint(brush_b.faces[face_idx_b].vertices[1])),
                brush_a.obj.Transform.InverseTransformPoint(brush_b.obj.Transform.TransformPoint(brush_b.faces[face_idx_b].vertices[2]))
            };

            // Don't use degenerate faces.
            bool has_degenerate = false;
            if (IsSame(vertices_a[0], vertices_a[1], vertex_snap) ||
                IsSame(vertices_a[0], vertices_a[2], vertex_snap) ||
                IsSame(vertices_a[1], vertices_a[2], vertex_snap))
            {
                collection.build2DFacesA[face_idx_a] = new Build2DFaces();
                has_degenerate = true;
            }

            if (IsSame(vertices_b[0], vertices_b[1], vertex_snap) ||
                IsSame(vertices_b[0], vertices_b[2], vertex_snap) ||
                IsSame(vertices_b[1], vertices_b[2], vertex_snap))
            {
                collection.build2DFacesB[face_idx_b] = new Build2DFaces();
                has_degenerate = true;
            }

            if (has_degenerate) return;

            // Ensure B has points either side of or in the plane of A.
            int over_count = 0, under_count = 0;
            Plane plane_a = new Plane(vertices_a[0], vertices_a[1], vertices_a[2]);
            double distance_tolerance = 0.3f;

            for (int i = 0; i < 3; i++)
            {
                if (plane_a.GetDistanceToPoint(vertices_b[i]) >= distance_tolerance)
                {
                    if (Vector3.Dot(plane_a.normal, vertices_b[i]) > plane_a.distance)
                        over_count++;
                    else
                        under_count++;
                }
                // else In plane.
            }
            // If all points under or over the plane, there is no intersection.
            if (over_count == 3 || under_count == 3) return;

            // Ensure A has points either side of or in the plane of B.
            over_count = 0;
            under_count = 0;
            Plane plane_b = new Plane(vertices_b[0], vertices_b[1], vertices_b[2]);

            for (int i = 0; i < 3; i++)
            {
                if (plane_b.GetDistanceToPoint(vertices_a[i]) >= distance_tolerance)
                {
                    if (Vector3.Dot(plane_b.normal, vertices_a[i]) > plane_b.distance)
                        over_count++;
                    else
                        under_count++;
                }
                // else In plane.
            }
            // If all points under or over the plane, there is no intersection.
            if (over_count == 3 || under_count == 3) return;

            // Check for intersection using the SAT theorem.
            {
                // Edge pair cross product combinations.
                for (int i = 0; i < 3; i++)
                {
                    Vector3 axis_a = (vertices_a[i] - vertices_a[(i + 1) % 3]);
                    axis_a /= axis_a.magnitude;

                    for (int j = 0; j < 3; j++)
                    {
                        Vector3 axis_b = (vertices_b[j] - vertices_b[(j + 1) % 3]);
                        axis_b /= axis_b.magnitude;

                        Vector3 sep_axis = Vector3.Cross(axis_a, axis_b);
                        if (sep_axis == Vector3.zero) continue; //colineal
                        sep_axis /= sep_axis.magnitude;

                        double min_a = 1e20f, max_a = -1e20f;
                        double min_b = 1e20f, max_b = -1e20f;

                        for (int k = 0; k < 3; k++)
                        {
                            double d = Vector3.Dot(sep_axis, vertices_a[k]);
                            min_a = (min_a < d) ? min_a : d;
                            max_a = (max_a > d) ? max_a : d;
                            d = Vector3.Dot(sep_axis, vertices_b[k]);
                            min_b = (min_b < d) ? min_b : d;
                            max_b = (max_b > d) ? max_b : d;
                        }

                        min_b -= (max_a - min_a) * 0.5f;
                        max_b += (max_a - min_a) * 0.5f;

                        double dmin = min_b - (min_a + max_a) * 0.5f;
                        double dmax = max_b - (min_a + max_a) * 0.5f;

                        if (dmin > Mathf.Small || dmax < -Mathf.Small)
                            return; // Does not contain zero, so they don't overlap.
                    }
                }
            }

            // If we're still here, the faces probably intersect, so add new faces.
            if (!collection.build2DFacesA.ContainsKey(face_idx_a))
                collection.build2DFacesA.Add(face_idx_a, new Build2DFaces(brush_a, face_idx_a));
            collection.build2DFacesA[face_idx_a].Insert(brush_b, face_idx_b, brush_a);

            if (!collection.build2DFacesB.ContainsKey(face_idx_b))
                collection.build2DFacesB.Add(face_idx_b, new Build2DFaces(brush_b, face_idx_b, brush_a));
            collection.build2DFacesB[face_idx_b].Insert(brush_a, face_idx_a);
        }

        static bool IsSame(Vector3 point1, Vector3 point2, double distance)
            => (point1 - point2).sqrMagnitude < distance * distance;

    }
}
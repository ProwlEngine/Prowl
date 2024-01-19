using System;
using System.Collections.Generic;

namespace Prowl.Runtime.CSG
{
    // Implementation based on Godot's CSG library: https://github.com/godotengine/godot/blob/master/modules/csg

    /// <summary> A CSG operation Type. </summary>
    public enum OperationType { Union, Intersection, Subtraction, };

    /// <summary> Handle/Perform operations between 2 Brush. </summary>
    public static class CSGUtility
    {
        struct Build2DFaceCollection
        {
            public Dictionary<int, Build2DFaces> build2DFacesA;
            public Dictionary<int, Build2DFaces> build2DFacesB;
        };

        /// <summary> Merge two brushes togather into mergedBrush </summary>
        public static void MergeBrushes(ref CSGBrush mergedBrush, OperationType operation, CSGBrush brushA, CSGBrush brushB, double tolerance = 0.00001f)
        {
            Build2DFaceCollection build2DFaceCollection;
            build2DFaceCollection.build2DFacesA = new Dictionary<int, Build2DFaces>(brushA.faces.Length);
            build2DFaceCollection.build2DFacesB = new Dictionary<int, Build2DFaces>(brushB.faces.Length);
            brushA.RegenFaceAABB();
            brushB.RegenFaceAABB();

            // Find Intersecting faces
            for (int i = 0; i < brushA.faces.Length; i++)
                for (int j = 0; j < brushB.faces.Length; j++)
                    if (brushA.faces[i].aabb.Intersects(brushB.faces[j].aabb))
                        UpdateFace(ref brushA, i, ref brushB, j, ref build2DFaceCollection, tolerance);

            // Add faces to MeshMerge.
            MeshMerge mesh_merge = new MeshMerge(brushA.faces.Length + build2DFaceCollection.build2DFacesA.Count, brushB.faces.Length + build2DFaceCollection.build2DFacesB.Count);
            mesh_merge.vertex_snap = tolerance;
            mesh_merge.scale_a = brushA.obj.transform.localScale;

            Vector3[] points = new Vector3[3];
            Vector2[] uvs = new Vector2[3];
            for (int i = 0; i < brushA.faces.Length; i++)
            {
                if (build2DFaceCollection.build2DFacesA.ContainsKey(i))
                {
                    build2DFaceCollection.build2DFacesA[i].AddFacesToMesh(ref mesh_merge, false);
                }
                else
                {
                    var face = brushA.faces[i];
                    points[0] = face.vertices[0];
                    points[1] = face.vertices[1];
                    points[2] = face.vertices[2];
                    uvs[0] = face.uvs[0];
                    uvs[1] = face.uvs[1];
                    uvs[2] = face.uvs[2];
                    mesh_merge.AddFace(points, uvs, false);
                }
            }

            for (int i = 0; i < brushB.faces.Length; i++)
            {
                if (build2DFaceCollection.build2DFacesB.ContainsKey(i))
                {
                    build2DFaceCollection.build2DFacesB[i].AddFacesToMesh(ref mesh_merge, true);
                }
                else
                {
                    var bFace = brushB.faces[i];
                    var transformA = brushA.obj.transform;
                    var transformB = brushB.obj.transform;
                    points[0] = transformA.InverseTransformPoint(transformB.TransformPoint(bFace.vertices[0]));
                    points[1] = transformA.InverseTransformPoint(transformB.TransformPoint(bFace.vertices[1]));
                    points[2] = transformA.InverseTransformPoint(transformB.TransformPoint(bFace.vertices[2]));
                    uvs[0] = bFace.uvs[0];
                    uvs[1] = bFace.uvs[1];
                    uvs[2] = bFace.uvs[2];
                    mesh_merge.AddFace(points, uvs, true);
                }
            }

            Array.Clear(mergedBrush.faces, 0, mergedBrush.faces.Length);
            mesh_merge.PerformOperation(operation, ref mergedBrush);
            mesh_merge = null;
            GC.Collect();
        }

        static void UpdateFace(ref CSGBrush brushA, int aFaceID, ref CSGBrush brushB, int bFaceID, ref Build2DFaceCollection collection, double tolerance)
        {
            var aFace = brushA.faces[aFaceID];
            Vector3[] aVerts = { aFace.vertices[0], aFace.vertices[1], aFace.vertices[2] };

            var bFace = brushB.faces[bFaceID];
            var aTransform = brushA.obj.transform;
            var bTransform = brushB.obj.transform;
            Vector3[] bVerts = {
                aTransform.InverseTransformPoint(bTransform.TransformPoint(bFace.vertices[0])),
                aTransform.InverseTransformPoint(bTransform.TransformPoint(bFace.vertices[1])),
                aTransform.InverseTransformPoint(bTransform.TransformPoint(bFace.vertices[2]))
            };

            // Don't use degenerate faces.
            bool degenerate = false;
            if (IsDifferant(aVerts[0], aVerts[1], tolerance) ||
                IsDifferant(aVerts[0], aVerts[2], tolerance) ||
                IsDifferant(aVerts[1], aVerts[2], tolerance))
            {
                collection.build2DFacesA[aFaceID] = new Build2DFaces();
                degenerate = true;
            }

            if (IsDifferant(bVerts[0], bVerts[1], tolerance) ||
                IsDifferant(bVerts[0], bVerts[2], tolerance) ||
                IsDifferant(bVerts[1], bVerts[2], tolerance))
            {
                collection.build2DFacesB[bFaceID] = new Build2DFaces();
                degenerate = true;
            }

            if (degenerate) return;

            const double distance_tolerance = 0.3f;

            // Ensure B has points either side of or in the plane of A.
            int over = 0, under = 0;
            Plane aPlane = new Plane(aVerts[0], aVerts[1], aVerts[2]);

            for (int i = 0; i < 3; i++)
            {
                if (aPlane.GetDistanceToPoint(bVerts[i]) >= distance_tolerance)
                {
                    if (Vector3.Dot(aPlane.normal, bVerts[i]) > aPlane.distance)
                        over++;
                    else
                        under++;
                }
                //else In plane.
            }

            // If all points under or over the plane, there is no intersection.
            if (over == 3 || under == 3) return;

            // Ensure A has points either side of or in the plane of B.
            over = under = 0;
            Plane bPlane = new Plane(bVerts[0], bVerts[1], bVerts[2]);
            for (int i = 0; i < 3; i++)
            {
                if (bPlane.GetDistanceToPoint(aVerts[i]) >= distance_tolerance)
                {
                    if (Vector3.Dot(bPlane.normal, aVerts[i]) > bPlane.distance)
                        over++;
                    else
                        under++;
                }
                //else In plane.
            }

            // If all points under or over the plane, there is no intersection.
            if (over == 3 || under == 3) return;

            // Check for intersection using the SAT theorem.
            {
                // Edge pair cross product combinations.
                for (int i = 0; i < 3; i++)
                {
                    Vector3 axis_a = Vector3.Normalize(aVerts[i] - aVerts[(i + 1) % 3]);
                    for (int j = 0; j < 3; j++)
                    {
                        Vector3 axis_b = Vector3.Normalize(bVerts[j] - bVerts[(j + 1) % 3]);
                        Vector3 sep_axis = Vector3.Cross(axis_a, axis_b);
                        if (sep_axis == Vector3.zero)
                            continue; //colineal
                        sep_axis = Vector3.Normalize(sep_axis);

                        double min_a = 1e20, max_a = -1e20;
                        double min_b = 1e20, max_b = -1e20;

                        for (int k = 0; k < 3; k++)
                        {
                            double d = Vector3.Dot(sep_axis, aVerts[k]);
                            min_a = Mathf.Min(min_a, d);
                            max_a = Mathf.Max(max_a, d);
                            d = Vector3.Dot(sep_axis, bVerts[k]);
                            min_b = Mathf.Min(min_b, d);
                            max_b = Mathf.Max(max_b, d);
                        }

                        min_b -= (max_a - min_a) * 0.5;
                        max_b += (max_a - min_a) * 0.5;

                        double dmin = min_b - (min_a + max_a) * 0.5;
                        double dmax = max_b - (min_a + max_a) * 0.5;

                        if (dmin > Mathf.Small || dmax < -Mathf.Small)
                            return; // Does not contain zero, so they don't overlap.
                    }
                }
            }

            // If we're still here, the faces probably intersect, so add new faces.
            if (!collection.build2DFacesA.ContainsKey(aFaceID))
                collection.build2DFacesA.Add(aFaceID, new Build2DFaces(brushA, aFaceID));
            collection.build2DFacesA[aFaceID].Insert(brushB, bFaceID, brushA);

            if (!collection.build2DFacesB.ContainsKey(bFaceID))
                collection.build2DFacesB.Add(bFaceID, new Build2DFaces(brushB, bFaceID, brushA));
            collection.build2DFacesB[bFaceID].Insert(brushA, aFaceID);
        }

        private static bool IsDifferant(Vector3 point1, Vector3 point2, double distance) 
            => (point1 - point2).sqrMagnitude < distance * distance;


    }
}
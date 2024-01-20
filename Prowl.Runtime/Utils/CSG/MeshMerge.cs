using System;
using System.Collections;
using System.Collections.Generic;

namespace Prowl.Runtime.CSG
{
    internal class MeshMerge
    {
        // Use a limit to speed up bvh and limit the depth.
        static int BVH_LIMIT = 10;

        enum VISIT
        {
            TEST_AABB_BIT = 0,
            VISIT_LEFT_BIT = 1,
            VISIT_RIGHT_BIT = 2,
            VISIT_DONE_BIT = 3,
            VISITED_BIT_SHIFT = 29,
            NODE_IDX_MASK = (1 << VISITED_BIT_SHIFT) - 1,
            VISITED_BIT_MASK = ~NODE_IDX_MASK
        };

        internal struct FaceBVH
        {
            public int face;
            public int left;
            public int right;
            public int next;
            public Vector3 center;
            public AABBCSG aabb;
        };

        private struct Face
        {
            public bool from_b;
            public int[] points;
            public Vector2[] uvs;
        };

        private List<Vector3> points;

        private List<Face> faces_a, faces_b;
        private int face_from_a, face_from_b;

		internal double vertex_snap = 0.0f;

		Dictionary<Vector3, int> snap_cache;

		internal Vector3 scale_a;

        class FaceBVHCmpX : IComparer
        {
            int IComparer.Compare(object obj1, object obj2)
            {
                (int i, FaceBVH f) p_left = ((int, FaceBVH))obj1;
                (int i, FaceBVH f) p_right = ((int, FaceBVH))obj2;
                if (p_left.f.center.x == p_right.f.center.x)
                    return 0;
                if (p_left.f.center.x < p_right.f.center.x)
                    return 1;
                return -1;
            }
        };

        class FaceBVHCmpY : IComparer
        {
            int IComparer.Compare(object obj1, object obj2)
            {
                (int i, FaceBVH f) p_left = ((int, FaceBVH))obj1;
                (int i, FaceBVH f) p_right = ((int, FaceBVH))obj2;
                if (p_left.f.center.y == p_right.f.center.y)
                    return 0;
                if (p_left.f.center.y < p_right.f.center.y)
                    return 1;
                return -1;
            }
        };

        class FaceBVHCmpZ : IComparer
        {
            int IComparer.Compare(object obj1, object obj2)
            {
                (int i, FaceBVH f) p_left = ((int, FaceBVH))obj1;
                (int i, FaceBVH f) p_right = ((int, FaceBVH))obj2;
                if (p_left.f.center.z == p_right.f.center.z)
                    return 0;
                if (p_left.f.center.z < p_right.f.center.z)
                    return 1;
                return -1;
            }
        };

        internal MeshMerge(int size_a = 0, int size_b = 0)
        {
            points = new ();
            faces_a = new(size_a);
            faces_b = new(size_b);
            snap_cache = new(3 * (size_a + size_b));
        }

        private int CreateBVH(ref FaceBVH[] facebvh, ref (int i, FaceBVH f)[] id_facebvh, int from, int size, int depth, ref int r_max_depth)
        {
            if (depth > r_max_depth)
                r_max_depth = depth;

            if (size == 0) return -1;

            if (size <= BVH_LIMIT)
            {
                for (int i = 0; i < size - 1; i++)
                    facebvh[id_facebvh[from + i].i].next = id_facebvh[from + i + 1].i;
                return id_facebvh[from].i;
            }

            AABBCSG aabb = new(facebvh[id_facebvh[from].i].aabb.GetPosition(), facebvh[id_facebvh[from].i].aabb.GetSize());
            for (int i = 1; i < size; i++)
                aabb.MergeWith(id_facebvh[from + i].f.aabb);

            int li = aabb.GetLongestAxisIndex();
            switch (li)
            {
                case 0:
                {
                    SortArray temp = new(new FaceBVHCmpX());
                    temp.nthElement(from, from + size, from + size / 2, ref id_facebvh);
                }
                break;

                case 1:
                {
                    SortArray temp = new(new FaceBVHCmpY());
                    temp.nthElement(from, from + size, from + size / 2, ref id_facebvh);
                }
                break;

                case 2:
                {
                    SortArray temp = new(new FaceBVHCmpZ());
                    temp.nthElement(from, from + size, from + size / 2, ref id_facebvh);
                }
                break;
            }

            int left = CreateBVH(ref facebvh, ref id_facebvh, from, size / 2, depth + 1, ref r_max_depth);
            int right = CreateBVH(ref facebvh, ref id_facebvh, from + size / 2, size - size / 2, depth + 1, ref r_max_depth);

            Array.Resize(ref facebvh, facebvh.Length + 1);
            FaceBVH fBVH = new() {
                aabb = aabb,
                center = aabb.GetCenter(),
                face = -1,
                left = left,
                right = right,
                next = -1
            };
            facebvh[^1] = fBVH;
            return facebvh.Length - 1;
        }

        private void AddDistance(ref List<double> r_intersectionsA, ref List<double> r_intersectionsB, bool from_B, double distance)
        {
            List<double> intersections = from_B ? r_intersectionsB : r_intersectionsA;
            // Check if distance exists.
            foreach (double E in intersections)
                if (Mathf.Abs(E - distance) < Mathf.Small)
                    return;
            intersections.Add(distance);
        }

        private bool InsideBVH(ref FaceBVH[] facebvh, int max_depth, int bvh_first, int face_idx, bool from_faces_a)
        {
            Face face = from_faces_a ? faces_a[face_idx] : faces_b[face_idx];

            Vector3[] face_points = [points[face.points[0]], points[face.points[1]], points[face.points[2]]];
            Vector3 face_center = (face_points[0] + face_points[1] + face_points[2]) / 3.0f;
            Vector3 face_normal = new Plane(face_points[0], face_points[1], face_points[2]).normal;

            int[] stack = new int[max_depth];

            List<double> intersectionsA = [];
            List<double> intersectionsB = [];

            int level = 0;
            int pos = bvh_first;
            stack[0] = pos;
            int c = stack[level];

            while (true)
            {
                int node = stack[level] & (int)VISIT.NODE_IDX_MASK;
                FaceBVH? current_facebvh = facebvh[node];
                bool done = false;

                switch (stack[level] >> (int)VISIT.VISITED_BIT_SHIFT)
                {
                    case (int)VISIT.TEST_AABB_BIT:
                    {
                        if (current_facebvh.Value.face >= 0)
                        {
                            while (current_facebvh != null)
                            {
                                if ((current_facebvh.Value).aabb.IntersectsRay(face_center, face_normal))
                                {
                                    Face current_face = from_faces_a ? faces_b[current_facebvh.Value.face] : faces_a[current_facebvh.Value.face];

                                    Vector3 A = points[current_face.points[0]];
                                    Vector3 B = points[current_face.points[1]];
                                    Vector3 C = points[current_face.points[2]];

                                    Vector3 current_normal = new Plane(A, B, C).normal;
                                    Vector3 intersection_point = new();

                                    // Check if faces are co-planar.
                                    if (Mathf.ApproximatelyEquals(current_normal, face_normal) && Mathf.IsPointInTriangle(face_center, A, B, C))
                                    {
                                        // Only add an intersection if not a B face.
                                        if (!face.from_b)
                                            AddDistance(ref intersectionsA, ref intersectionsB, current_face.from_b, 0);
                                    }
                                    else if (Mathf.RayIntersectsTriangle(face_center, face_normal, A, B, C, out intersection_point))
                                    {
                                        double distance = Vector3.Distance(face_center, intersection_point);
                                        AddDistance(ref intersectionsA, ref intersectionsB, current_face.from_b, distance);
                                    }
                                }

                                if ((current_facebvh.Value).next != -1)
                                    current_facebvh = facebvh[(current_facebvh.Value).next];
                                else
                                    current_facebvh = null;
                            }

                            stack[level] = ((int)VISIT.VISIT_DONE_BIT << (int)VISIT.VISITED_BIT_SHIFT) | node;
                        }
                        else
                        {
                            bool valid = (current_facebvh.Value).aabb.IntersectsRay(face_center, face_normal);

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
                        stack[level + 1] = (current_facebvh.Value).left | (int)VISIT.TEST_AABB_BIT;
                        level++;
                        continue;
                    }

                    case (int)VISIT.VISIT_RIGHT_BIT:
                    {
                        stack[level] = ((int)VISIT.VISIT_DONE_BIT << (int)VISIT.VISITED_BIT_SHIFT) | node;
                        stack[level + 1] = (current_facebvh.Value).right | (int)VISIT.TEST_AABB_BIT;
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

                if (done) break;
            }
            // Inside if face normal intersects other faces an odd number of times.
            int res = (intersectionsA.Count + intersectionsB.Count) & 1;
            return res != 0;
        }

        /// <summary>
        /// This method set up the face to kown if there are inside
        /// </summary>
        public void PerformOperation(CSGOperation operation, ref CSGBrush r_merged_brush)
        {

            FaceBVH[] bvhvec_a = new FaceBVH[0];
            Array.Resize(ref bvhvec_a, faces_a.Count);
            FaceBVH[] facebvh_a = bvhvec_a;

            FaceBVH[] bvhvec_b = new FaceBVH[0];
            Array.Resize(ref bvhvec_b, faces_b.Count);
            FaceBVH[] facebvh_b = bvhvec_b;

            AABBCSG aabb_a = new AABBCSG();
            AABBCSG aabb_b = new AABBCSG();

            bool first_a = true;
            bool first_b = true;

            for (int i = 0; i < faces_a.Count; i++)
            {
                FaceBVH faceA = new();
                faceA.left = -1;
                faceA.right = -1;
                faceA.face = i;
                faceA.aabb = new AABBCSG();
                faceA.aabb.SetPosition(points[faces_a[i].points[0]]);
                faceA.aabb.Encapsulate(points[faces_a[i].points[1]]);
                faceA.aabb.Encapsulate(points[faces_a[i].points[2]]);
                faceA.center = faceA.aabb.GetCenter();
                faceA.aabb.ExpandBy(vertex_snap);
                faceA.next = -1;
                if (first_a)
                {
                    aabb_a = faceA.aabb.Copy();
                    first_a = false;
                }
                else
                {
                    aabb_a.MergeWith(faceA.aabb);
                }
                facebvh_a[i] = faceA;
            }

            for (int i = 0; i < faces_b.Count; i++)
            {
                FaceBVH faceB = new();
                faceB.left = -1;
                faceB.right = -1;
                faceB.face = i;
                faceB.aabb = new AABBCSG();
                faceB.aabb.SetPosition(points[faces_b[i].points[0]]);
                faceB.aabb.Encapsulate(points[faces_b[i].points[1]]);
                faceB.aabb.Encapsulate(points[faces_b[i].points[2]]);
                faceB.center = faceB.aabb.GetCenter();
                faceB.aabb.ExpandBy(vertex_snap);
                faceB.next = -1;
                if (first_b)
                {
                    aabb_b = faceB.aabb.Copy();
                    first_b = false;
                }
                else
                {
                    aabb_b.MergeWith(faceB.aabb);
                }
                facebvh_b[i] = faceB;
            }

            AABBCSG intersection_aabb = aabb_a.ComputeIntersection(aabb_b);

            // Check if shape AABBs intersect.
            if (operation == CSGOperation.Intersection && intersection_aabb.GetSize() == Vector3.zero)
            {
                //return;
            }

            (int, FaceBVH)[] bvhtrvec_a = [];
            Array.Resize(ref bvhtrvec_a, faces_a.Count);
            (int, FaceBVH)[] bvhptr_a = bvhtrvec_a;
            for (int i = 0; i < faces_a.Count; i++)
                bvhptr_a[i] = (i, facebvh_a[i]);

            (int, FaceBVH)[] bvhtrvec_b = [];
            Array.Resize(ref bvhtrvec_b, faces_b.Count);
            (int, FaceBVH)[] bvhptr_b = bvhtrvec_b;
            for (int i = 0; i < faces_b.Count; i++)
                bvhptr_b[i] = (i, facebvh_b[i]);

            int max_depth_a = 0;
            CreateBVH(ref facebvh_a, ref bvhptr_a, 0, face_from_a, 1, ref max_depth_a);
            int max_alloc_a = facebvh_a.Length;

            int max_depth_b = 0;
            CreateBVH(ref facebvh_b, ref bvhptr_b, 0, face_from_b, 1, ref max_depth_b);
            int max_alloc_b = facebvh_b.Length;

            switch (operation)
            {
                case CSGOperation.Union:
                {
                    int faces_count = 0;
                    Array.Resize(ref r_merged_brush.faces, faces_a.Count + faces_b.Count);

                    for (int i = 0; i < faces_a.Count; i++)
                    {
                        var face = faces_a[i];

                        // Check if face AABB intersects the intersection AABB.
                        if (!intersection_aabb.IntersectInclusive(facebvh_a[i].aabb))
                        {
                            r_merged_brush.faces[faces_count].vertices = new List<Vector3>(3) {
                                points[face.points[0]],
                                points[face.points[1]],
                                points[face.points[2]]
                            };
                            r_merged_brush.faces[faces_count].uvs = [face.uvs[0], face.uvs[1], face.uvs[2]];
                            faces_count++;
                            continue;
                        }

                        if (!InsideBVH(ref facebvh_b, max_depth_b, max_alloc_b - 1, i, true))
                        {
                            r_merged_brush.faces[faces_count].vertices = new List<Vector3>(3) {
                                points[face.points[0]],
                                points[face.points[1]],
                                points[face.points[2]]
                            };
                            r_merged_brush.faces[faces_count].uvs = [face.uvs[0], face.uvs[1], face.uvs[2]];
                            faces_count++;
                        }
                    }

                    for (int i = 0; i < faces_b.Count; i++)
                    {
                        var face = faces_b[i];

                        // Check if face AABB intersects the intersection AABB.
                        if (!intersection_aabb.IntersectInclusive(facebvh_b[i].aabb))
                        {
                            r_merged_brush.faces[faces_count].vertices = new List<Vector3>(3) {
                                points[face.points[0]],
                                points[face.points[1]],
                                points[face.points[2]]
                            };
                            r_merged_brush.faces[faces_count].uvs = [face.uvs[0], face.uvs[1], face.uvs[2]];
                            faces_count++;
                            continue;
                        }

                        if (!InsideBVH(ref facebvh_a, max_depth_a, max_alloc_a - 1, i, false))
                        {
                            r_merged_brush.faces[faces_count].vertices = new List<Vector3>(3) {
                                points[face.points[0]],
                                points[face.points[1]],
                                points[face.points[2]]
                            };
                            r_merged_brush.faces[faces_count].uvs = [face.uvs[0], face.uvs[1], face.uvs[2]];
                            faces_count++;
                        }
                    }
                    Array.Resize(ref r_merged_brush.faces, faces_count);

                }
                break;

                case CSGOperation.Intersection:
                {
                    int faces_count = 0;
                    Array.Resize(ref r_merged_brush.faces, faces_a.Count + faces_b.Count);

                    for (int i = 0; i < faces_a.Count; i++)
                    {
                        var face = faces_a[i];

                        // Check if face AABB intersects the intersection AABB.
                        if (!intersection_aabb.IntersectInclusive(facebvh_a[i].aabb))
                            continue;

                        if (InsideBVH(ref facebvh_b, max_depth_b, max_alloc_b - 1, i, true))
                        {
                            r_merged_brush.faces[faces_count].vertices = new List<Vector3>(3) {
                                points[face.points[0]],
                                points[face.points[1]],
                                points[face.points[2]]
                            };
                            r_merged_brush.faces[faces_count].uvs = [face.uvs[0], face.uvs[1], face.uvs[2]];
                            faces_count++;
                        }
                    }

                    for (int i = 0; i < faces_b.Count; i++)
                    {
                        var face = faces_b[i];

                        // Check if face AABB intersects the intersection AABB.
                        if (!intersection_aabb.IntersectInclusive(facebvh_b[i].aabb))
                            continue;

                        if (InsideBVH(ref facebvh_a, max_depth_a, max_alloc_a - 1, i, false))
                        {
                            r_merged_brush.faces[faces_count].vertices = new List<Vector3>(3) {
                                points[face.points[0]],
                                points[face.points[1]],
                                points[face.points[2]]
                            };
                            r_merged_brush.faces[faces_count].uvs = [face.uvs[0], face.uvs[1], face.uvs[2]];
                            faces_count++;
                        }
                    }
                    Array.Resize(ref r_merged_brush.faces, faces_count);
                }
                break;

                case CSGOperation.Subtraction:
                {
                    int faces_count = 0;
                    Array.Resize(ref r_merged_brush.faces, faces_a.Count + faces_b.Count);

                    for (int i = 0; i < faces_a.Count; i++)
                    {
                        var face = faces_a[i];

                        // Check if face AABB intersects the intersection AABB.
                        if (!intersection_aabb.IntersectInclusive(facebvh_a[i].aabb))
                        {
                            r_merged_brush.faces[faces_count].vertices = new List<Vector3>(3) {
                                points[face.points[0]],
                                points[face.points[1]],
                                points[face.points[2]]
                            };
                            r_merged_brush.faces[faces_count].uvs = [face.uvs[0], face.uvs[1], face.uvs[2]];
                            faces_count++;
                            continue;
                        }

                        if (!InsideBVH(ref facebvh_b, max_depth_b, max_alloc_b - 1, i, true))
                        {
                            r_merged_brush.faces[faces_count].vertices = new List<Vector3>(3) {
                                points[face.points[0]],
                                points[face.points[1]],
                                points[face.points[2]]
                            };
                            r_merged_brush.faces[faces_count].uvs = [face.uvs[0], face.uvs[1], face.uvs[2]];
                            faces_count++;
                        }
                    }

                    for (int i = 0; i < faces_b.Count; i++)
                    {
                        var face = faces_b[i];

                        // Check if face AABB intersects the intersection AABB.
                        if (!intersection_aabb.IntersectInclusive(facebvh_b[i].aabb))
                            continue;

                        if (InsideBVH(ref facebvh_a, max_depth_a, max_alloc_a - 1, i, false))
                        {
                            r_merged_brush.faces[faces_count].vertices = new List<Vector3>(3) {
                                points[face.points[1]],
                                points[face.points[0]],
                                points[face.points[2]]
                            };
                            r_merged_brush.faces[faces_count].uvs = [face.uvs[0], face.uvs[1], face.uvs[2]];
                            faces_count++;
                        }
                    }
                    Array.Resize(ref r_merged_brush.faces, faces_count);

                }
                break;
            }
        }

        internal void AddFace(Vector3[] points, Vector2[] uvs, bool from_b)
        {
            int[] indices = new int[3];
            for (int i = 0; i < 3; i++)
            {
                Vector3 vk = new() {
                    x = Mathf.RoundToInt(((points[i].x + vertex_snap) * 0.31234f) / vertex_snap),
                    y = Mathf.RoundToInt(((points[i].y + vertex_snap) * 0.31234f) / vertex_snap),
                    z = Mathf.RoundToInt(((points[i].z + vertex_snap) * 0.31234f) / vertex_snap)
                };

                if (snap_cache.TryGetValue(vk, out int value))
                {
                    indices[i] = value;
                }
                else
                {
                    indices[i] = this.points.Count;
                    this.points.Add(Vector3.Scale(points[i], scale_a));
                    snap_cache.Add(vk, indices[i]);
                }
            }

            // Don't add degenerate faces.
            if (indices[0] == indices[2] || indices[0] == indices[1] || indices[1] == indices[2])
                return;

            Face face = new() {
                from_b = from_b,
                points = new int[3],
                uvs = new Vector2[3]
            };

            for (int k = 0; k < 3; k++)
            {
                face.points[k] = indices[k];
                face.uvs[k] = uvs[k];
            }

            if (from_b)
            {
                face_from_b++;
                faces_b.Add(face);
            }
            else
            {
                face_from_a++;
                faces_a.Add(face);
            }

        }

    }
}
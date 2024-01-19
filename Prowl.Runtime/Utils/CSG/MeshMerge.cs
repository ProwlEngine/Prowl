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

        private int face_from_a = 0;
        private int face_from_b = 0;

        public double vertex_snap = 0.0f;

        Dictionary<Vector3, int> snap_cache;

        public Vector3 scale_a;

        class FaceBVHCmpX : IComparer
        {
            int IComparer.Compare(object obj1, object obj2)
            {
                (int i, FaceBVH f) p_left = ((int, FaceBVH))obj1;
                (int i, FaceBVH f) p_right = ((int, FaceBVH))obj2;
                if (p_left.f.center.x == p_right.f.center.x)
                {
                    return 0;
                }
                if (p_left.f.center.x < p_right.f.center.x)
                {
                    return 1;
                }
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
                {
                    return 0;
                }
                if (p_left.f.center.y < p_right.f.center.y)
                {
                    return 1;
                }
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
                {
                    return 0;
                }
                if (p_left.f.center.z < p_right.f.center.z)
                {
                    return 1;
                }
                return -1;
            }
        };

        internal MeshMerge(int size_a = 0, int size_b = 0)
        {
            points = new List<Vector3>();
            faces_a = new List<Face>(size_a);
            faces_b = new List<Face>(size_b);
            snap_cache = new Dictionary<Vector3, int>(3 * (size_a + size_b));
        }

        int CreateBVH(ref FaceBVH[] facebvh, ref (int i, FaceBVH f)[] id_facebvh, int from, int size, int depth, ref int r_max_depth)
        {
            if (depth > r_max_depth)
                r_max_depth = depth;

            if (size == 0)
                return -1;

            if (size <= BVH_LIMIT)
            {
                for (int i = 0; i < size - 1; i++)
                {
                    facebvh[id_facebvh[from + i].i].next = id_facebvh[from + i + 1].i;
                }
                return id_facebvh[from].i;
            }

            AABBCSG aabb = new AABBCSG(facebvh[id_facebvh[from].i].aabb.GetPosition(), facebvh[id_facebvh[from].i].aabb.GetSize());
            for (int i = 1; i < size; i++)
                aabb.MergeWith(id_facebvh[from + i].f.aabb);

            int li = aabb.GetLongestAxis();

            switch (li)
            {
                case 0:
                {
                    SortArray temp = new SortArray(new FaceBVHCmpX());
                    temp.nthElement(from, from + size, from + size / 2, ref id_facebvh);
                }
                break;

                case 1:
                {
                    SortArray temp = new SortArray(new FaceBVHCmpY());
                    temp.nthElement(from, from + size, from + size / 2, ref id_facebvh);
                }
                break;

                case 2:
                {
                    SortArray temp = new SortArray(new FaceBVHCmpZ());
                    temp.nthElement(from, from + size, from + size / 2, ref id_facebvh);
                }
                break;
            }

            int left = CreateBVH(ref facebvh, ref id_facebvh, from, size / 2, depth + 1, ref r_max_depth);
            int right = CreateBVH(ref facebvh, ref id_facebvh, from + size / 2, size - size / 2, depth + 1, ref r_max_depth);

            Array.Resize(ref facebvh, facebvh.Length + 1);
            var fbvh = facebvh[facebvh.Length - 1];
            fbvh.aabb = aabb;
            fbvh.center = aabb.GetCenter();
            fbvh.face = -1;
            fbvh.left = left;
            fbvh.right = right;
            fbvh.next = -1;
            facebvh[facebvh.Length - 1] = fbvh;

            return facebvh.Length - 1;
        }

        void AddDistance(ref List<double> r_intersectionsA, ref List<double> r_intersectionsB, bool from_B, double distance)
        {
            List<double> intersections = from_B ? r_intersectionsB : r_intersectionsA;

            // Check if distance exists.
            foreach (double E in intersections)
                if (Mathf.Abs(E - distance) < Mathf.Small)
                    return;

            intersections.Add(distance);
        }

        bool BVHInside(ref FaceBVH[] facebvh, int max_depth, int bvh_first, int face_idx, bool from_faces_a)
        {
            Face face;
            if (from_faces_a)
                face = faces_a[face_idx];
            else
                face = faces_b[face_idx];

            Vector3[] face_points = {
                points[face.points[0]],
                points[face.points[1]],
                points[face.points[2]]
            };
            Vector3 face_center = (face_points[0] + face_points[1] + face_points[2]) / 3.0f;
            Vector3 face_normal = new Plane(face_points[0], face_points[1], face_points[2]).normal;

            int[] stack = new int[max_depth];


            List<double> intersectionsA = new List<double>();
            List<double> intersectionsB = new List<double>();

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
                        if (((FaceBVH)current_facebvh).face >= 0)
                        {
                            while (current_facebvh != null)
                            {
                                if (((FaceBVH)current_facebvh).aabb.IntersectsRay(face_center, face_normal))
                                {
                                    Face current_face;
                                    if (from_faces_a)
                                    {
                                        current_face = faces_b[((FaceBVH)current_facebvh).face];
                                    }
                                    else
                                    {
                                        current_face = faces_a[((FaceBVH)current_facebvh).face];
                                    }
                                    Vector3[] current_points = {
                                        points[current_face.points[0]],
                                        points[current_face.points[1]],
                                        points[current_face.points[2]]
                                    };
                                    Vector3 current_normal = new Plane(current_points[0], current_points[1], current_points[2]).normal;
                                    Vector3 intersection_point;

                                    // Check if faces are co-planar.
                                    if (Mathf.ApproximatelyEquals(current_normal, face_normal) &&
                                        Mathf.IsPointInTriangle(face_center, current_points[0], current_points[1], current_points[2]))
                                    {
                                        // Only add an intersection if not a B face.
                                        if (!face.from_b)
                                        {
                                            AddDistance(ref intersectionsA, ref intersectionsB, current_face.from_b, 0);
                                        }
                                    }
                                    else if (Mathf.RayIntersectsTriangle(face_center, face_normal, current_points[0], current_points[1], current_points[2], out intersection_point))
                                    {
                                        double distance = Vector3.Distance(face_center, intersection_point);
                                        AddDistance(ref intersectionsA, ref intersectionsB, current_face.from_b, distance);
                                    }
                                }

                                if (((FaceBVH)current_facebvh).next != -1)
                                {
                                    current_facebvh = facebvh[((FaceBVH)current_facebvh).next];
                                }
                                else
                                {
                                    current_facebvh = null;
                                }
                            }

                            stack[level] = ((int)VISIT.VISIT_DONE_BIT << (int)VISIT.VISITED_BIT_SHIFT) | node;

                        }
                        else
                        {
                            bool valid = ((FaceBVH)current_facebvh).aabb.IntersectsRay(face_center, face_normal);

                            if (!valid)
                            {
                                stack[level] = ((int)VISIT.VISIT_DONE_BIT << (int)VISIT.VISITED_BIT_SHIFT) | node;
                            }
                            else
                            {
                                stack[level] = ((int)VISIT.VISIT_LEFT_BIT << (int)VISIT.VISITED_BIT_SHIFT) | node;
                            }
                        }
                        continue;
                    }

                    case (int)VISIT.VISIT_LEFT_BIT:
                    {
                        stack[level] = ((int)VISIT.VISIT_RIGHT_BIT << (int)VISIT.VISITED_BIT_SHIFT) | node;
                        stack[level + 1] = ((FaceBVH)current_facebvh).left | (int)VISIT.TEST_AABB_BIT;
                        level++;
                        continue;
                    }

                    case (int)VISIT.VISIT_RIGHT_BIT:
                    {
                        stack[level] = ((int)VISIT.VISIT_DONE_BIT << (int)VISIT.VISITED_BIT_SHIFT) | node;
                        stack[level + 1] = ((FaceBVH)current_facebvh).right | (int)VISIT.TEST_AABB_BIT;
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
                {
                    break;
                }
            }
            // Inside if face normal intersects other faces an odd number of times.
            int res = (intersectionsA.Count + intersectionsB.Count) & 1;
            return res != 0;
        }

        internal void PerformOperation(OperationType operation, ref CSGBrush r_merged_brush)
        {

            FaceBVH[] bvhvec_a = [];
            Array.Resize(ref bvhvec_a, faces_a.Count);
            FaceBVH[] facebvh_a = bvhvec_a;

            FaceBVH[] bvhvec_b = [];
            Array.Resize(ref bvhvec_b, faces_b.Count);
            FaceBVH[] facebvh_b = bvhvec_b;

            AABBCSG aabb_a = new AABBCSG();
            AABBCSG aabb_b = new AABBCSG();

            bool first_a = true;
            bool first_b = true;

            for (int i = 0; i < faces_a.Count; i++)
            {
                var f = new FaceBVH();
                f.left = -1;
                f.right = -1;
                f.face = i;
                f.aabb = new AABBCSG();
                f.aabb.SetPosition(points[faces_a[i].points[0]]);
                f.aabb.Encapsulate(points[faces_a[i].points[1]]);
                f.aabb.Encapsulate(points[faces_a[i].points[2]]);
                f.center = f.aabb.GetCenter();
                f.aabb.Grow(vertex_snap);
                f.next = -1;
                if (first_a)
                {
                    aabb_a = f.aabb.Copy();
                    first_a = false;
                }
                else
                {
                    aabb_a.MergeWith(f.aabb);
                }
                facebvh_a[i] = f;
            }

            for (int i = 0; i < faces_b.Count; i++)
            {
                var f = new FaceBVH();
                f.left = -1;
                f.right = -1;
                f.face = i;
                f.aabb = new AABBCSG();
                f.aabb.SetPosition(points[faces_b[i].points[0]]);
                f.aabb.Encapsulate(points[faces_b[i].points[1]]);
                f.aabb.Encapsulate(points[faces_b[i].points[2]]);
                f.center = f.aabb.GetCenter();
                f.aabb.Grow(vertex_snap);
                f.next = -1;
                if (first_b)
                {
                    aabb_b = f.aabb.Copy();
                    first_b = false;
                }
                else
                {
                    aabb_b.MergeWith(f.aabb);
                }
                facebvh_b[i] = f;
            }

            AABBCSG intersection_aabb = aabb_a.GetIntersectionAABB(aabb_b);

            // Check if shape AABBs intersect.
            if (operation == OperationType.Intersection && intersection_aabb.GetSize() == Vector3.zero)
            {
                //return;
            }

            (int, FaceBVH)[] bvhtrvec_a = new (int, FaceBVH)[0];
            Array.Resize(ref bvhtrvec_a, faces_a.Count);
            (int, FaceBVH)[] bvhptr_a = bvhtrvec_a;
            for (int i = 0; i < faces_a.Count; i++)
            {
                bvhptr_a[i] = (i, facebvh_a[i]);
            }

            (int, FaceBVH)[] bvhtrvec_b = new (int, FaceBVH)[0];
            Array.Resize(ref bvhtrvec_b, faces_b.Count);
            (int, FaceBVH)[] bvhptr_b = bvhtrvec_b;
            for (int i = 0; i < faces_b.Count; i++)
            {
                bvhptr_b[i] = (i, facebvh_b[i]);
            }

            int max_depth_a = 0;
            CreateBVH(ref facebvh_a, ref bvhptr_a, 0, face_from_a, 1, ref max_depth_a);
            int max_alloc_a = facebvh_a.Length;

            int max_depth_b = 0;
            CreateBVH(ref facebvh_b, ref bvhptr_b, 0, face_from_b, 1, ref max_depth_b);
            int max_alloc_b = facebvh_b.Length;

            switch (operation)
            {
                case OperationType.Union:
                {
                    int faces_count = 0;
                    Array.Resize(ref r_merged_brush.faces, faces_a.Count + faces_b.Count);

                    for (int i = 0; i < faces_a.Count; i++)
                    {
                        // Check if face AABB intersects the intersection AABB.
                        if (!intersection_aabb.Intersects(facebvh_a[i].aabb))
                        {
                            r_merged_brush.faces[faces_count].vertices = new List<Vector3>(3);
                            r_merged_brush.faces[faces_count].vertices.Add(points[faces_a[i].points[0]]);
                            r_merged_brush.faces[faces_count].vertices.Add(points[faces_a[i].points[1]]);
                            r_merged_brush.faces[faces_count].vertices.Add(points[faces_a[i].points[2]]);
                            r_merged_brush.faces[faces_count].uvs = new Vector2[3] { faces_a[i].uvs[0], faces_a[i].uvs[1], faces_a[i].uvs[2] };
                            faces_count++;
                            continue;
                        }

                        if (!BVHInside(ref facebvh_b, max_depth_b, max_alloc_b - 1, i, true))
                        {
                            r_merged_brush.faces[faces_count].vertices = new List<Vector3>(3);
                            r_merged_brush.faces[faces_count].vertices.Add(points[faces_a[i].points[0]]);
                            r_merged_brush.faces[faces_count].vertices.Add(points[faces_a[i].points[1]]);
                            r_merged_brush.faces[faces_count].vertices.Add(points[faces_a[i].points[2]]);
                            r_merged_brush.faces[faces_count].uvs = new Vector2[3] { faces_a[i].uvs[0], faces_a[i].uvs[1], faces_a[i].uvs[2] };
                            faces_count++;
                        }
                    }

                    for (int i = 0; i < faces_b.Count; i++)
                    {
                        // Check if face AABB intersects the intersection AABB.
                        if (!intersection_aabb.Intersects(facebvh_b[i].aabb))
                        {
                            r_merged_brush.faces[faces_count].vertices = new List<Vector3>(3);
                            r_merged_brush.faces[faces_count].vertices.Add(points[faces_b[i].points[0]]);
                            r_merged_brush.faces[faces_count].vertices.Add(points[faces_b[i].points[1]]);
                            r_merged_brush.faces[faces_count].vertices.Add(points[faces_b[i].points[2]]);
                            r_merged_brush.faces[faces_count].uvs = new Vector2[3] { faces_b[i].uvs[0], faces_b[i].uvs[1], faces_b[i].uvs[2] };
                            faces_count++;
                            continue;
                        }

                        if (!BVHInside(ref facebvh_a, max_depth_a, max_alloc_a - 1, i, false))
                        {
                            r_merged_brush.faces[faces_count].vertices = new List<Vector3>(3);
                            r_merged_brush.faces[faces_count].vertices.Add(points[faces_b[i].points[0]]);
                            r_merged_brush.faces[faces_count].vertices.Add(points[faces_b[i].points[1]]);
                            r_merged_brush.faces[faces_count].vertices.Add(points[faces_b[i].points[2]]);
                            r_merged_brush.faces[faces_count].uvs = new Vector2[3] { faces_b[i].uvs[0], faces_b[i].uvs[1], faces_b[i].uvs[2] };
                            faces_count++;
                        }
                    }
                    Array.Resize(ref r_merged_brush.faces, faces_count);

                }
                break;

                case OperationType.Intersection:
                {
                    int faces_count = 0;
                    Array.Resize(ref r_merged_brush.faces, faces_a.Count + faces_b.Count);

                    for (int i = 0; i < faces_a.Count; i++)
                    {
                        // Check if face AABB intersects the intersection AABB.
                        if (!intersection_aabb.Intersects(facebvh_a[i].aabb))
                            continue;

                        if (BVHInside(ref facebvh_b, max_depth_b, max_alloc_b - 1, i, true))
                        {
                            r_merged_brush.faces[faces_count].vertices = new List<Vector3>(3);
                            r_merged_brush.faces[faces_count].vertices.Add(points[faces_a[i].points[0]]);
                            r_merged_brush.faces[faces_count].vertices.Add(points[faces_a[i].points[1]]);
                            r_merged_brush.faces[faces_count].vertices.Add(points[faces_a[i].points[2]]);
                            r_merged_brush.faces[faces_count].uvs = new Vector2[3] { faces_a[i].uvs[0], faces_a[i].uvs[1], faces_a[i].uvs[2] };
                            faces_count++;
                        }
                    }

                    for (int i = 0; i < faces_b.Count; i++)
                    {
                        // Check if face AABB intersects the intersection AABB.
                        if (!intersection_aabb.Intersects(facebvh_b[i].aabb))
                            continue;

                        if (BVHInside(ref facebvh_a, max_depth_a, max_alloc_a - 1, i, false))
                        {
                            r_merged_brush.faces[faces_count].vertices = new List<Vector3>(3);
                            r_merged_brush.faces[faces_count].vertices.Add(points[faces_b[i].points[0]]);
                            r_merged_brush.faces[faces_count].vertices.Add(points[faces_b[i].points[1]]);
                            r_merged_brush.faces[faces_count].vertices.Add(points[faces_b[i].points[2]]);
                            r_merged_brush.faces[faces_count].uvs = new Vector2[3] { faces_b[i].uvs[0], faces_b[i].uvs[1], faces_b[i].uvs[2] };
                            faces_count++;
                        }
                    }
                    Array.Resize(ref r_merged_brush.faces, faces_count);
                }
                break;

                case OperationType.Subtraction:
                {
                    int faces_count = 0;
                    Array.Resize(ref r_merged_brush.faces, faces_a.Count + faces_b.Count);

                    for (int i = 0; i < faces_a.Count; i++)
                    {
                        // Check if face AABB intersects the intersection AABB.
                        if (!intersection_aabb.Intersects(facebvh_a[i].aabb))
                        {
                            r_merged_brush.faces[faces_count].vertices = new List<Vector3>(3);
                            r_merged_brush.faces[faces_count].vertices.Add(points[faces_a[i].points[0]]);
                            r_merged_brush.faces[faces_count].vertices.Add(points[faces_a[i].points[1]]);
                            r_merged_brush.faces[faces_count].vertices.Add(points[faces_a[i].points[2]]);
                            r_merged_brush.faces[faces_count].uvs = new Vector2[3] { faces_a[i].uvs[0], faces_a[i].uvs[1], faces_a[i].uvs[2] };
                            faces_count++;
                            continue;
                        }

                        if (!BVHInside(ref facebvh_b, max_depth_b, max_alloc_b - 1, i, true))
                        {
                            r_merged_brush.faces[faces_count].vertices = new List<Vector3>(3);
                            r_merged_brush.faces[faces_count].vertices.Add(points[faces_a[i].points[0]]);
                            r_merged_brush.faces[faces_count].vertices.Add(points[faces_a[i].points[1]]);
                            r_merged_brush.faces[faces_count].vertices.Add(points[faces_a[i].points[2]]);
                            r_merged_brush.faces[faces_count].uvs = new Vector2[3] { faces_a[i].uvs[0], faces_a[i].uvs[1], faces_a[i].uvs[2] };
                            faces_count++;
                        }
                    }

                    for (int i = 0; i < faces_b.Count; i++)
                    {
                        // Check if face AABB intersects the intersection AABB.
                        if (!intersection_aabb.Intersects(facebvh_b[i].aabb))
                            continue;

                        if (BVHInside(ref facebvh_a, max_depth_a, max_alloc_a - 1, i, false))
                        {
                            r_merged_brush.faces[faces_count].vertices = new List<Vector3>(3);
                            r_merged_brush.faces[faces_count].vertices.Add(points[faces_b[i].points[1]]);
                            r_merged_brush.faces[faces_count].vertices.Add(points[faces_b[i].points[0]]);
                            r_merged_brush.faces[faces_count].vertices.Add(points[faces_b[i].points[2]]);
                            r_merged_brush.faces[faces_count].uvs = new Vector2[3] { faces_b[i].uvs[0], faces_b[i].uvs[1], faces_b[i].uvs[2] };
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
                Vector3 vk = new Vector3();
                vk.x = Mathf.RoundToInt(((points[i].x + vertex_snap) * 0.31234f) / vertex_snap);
                vk.y = Mathf.RoundToInt(((points[i].y + vertex_snap) * 0.31234f) / vertex_snap);
                vk.z = Mathf.RoundToInt(((points[i].z + vertex_snap) * 0.31234f) / vertex_snap);

                if (snap_cache.ContainsKey(vk))
                {
                    indices[i] = snap_cache[vk];
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

            Face face = new Face();
            face.from_b = from_b;
            face.points = new int[3];
            face.uvs = new Vector2[3];

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
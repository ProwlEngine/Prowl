using System.Collections.Generic;

namespace Prowl.Runtime.CSG
{
    internal class Build2DFaces
    {
        struct Vertex2D
        {
            public Vector2 point;
            public Vector2 uv;
        };

		struct Face2D
        {
            public int[] vertex_idx;
        };

        private List<Vertex2D> vertices = new List<Vertex2D>();
		private List<Face2D> faces = new List<Face2D>();

		private double vertex_tolerance = 1e-10f;

		private TransformCSG to_3D, to_2D;

		private Plane plane;

        static Vector2 get_closest_point_to_segment(Vector2 point, Vector2[] segment)
        {
            Vector2 p = point - segment[0];
            Vector2 n = segment[1] - segment[0];
            double l2 = n.sqrMagnitude;
            if (l2 < 1e-20f)
                return segment[0]; // Both points are the same, just give any.

            double d = Vector2.Dot(n, p) / l2;
            if (d <= 0.0f)
                return segment[0]; // Before first point.
            else if (d >= 1.0f)
                return segment[1]; // After first point.
            else
                return segment[0] + n * d; // Inside.
        }


        static Vector2 LerpEdgeUV(Vector2[] segment_points, Vector2[] uvs, Vector2 interpolation)
        {
            // This condition seem quite rare
            //if (Mathf.ApproximatelyEquals(segment_points[0], segment_points[1]))
            //    return uvs[0];

            double segment_length = Vector2.Distance(segment_points[0], segment_points[1]);
            double distance = Vector2.Distance(segment_points[0], interpolation);
            double fraction = distance / segment_length;

            return Vector2.Lerp(uvs[0], uvs[1], fraction);
        }

        static Vector2 LerpTriangleUV(Vector2[] vertices, Vector2[] uvs, Vector2 interpolation_point)
        {
            // These conditions seem quite rare
            //if (Mathf.ApproximatelyEquals(interpolation_point, vertices[0]))
            //    return uvs[0];
            //if (Mathf.ApproximatelyEquals(interpolation_point, vertices[1]))
            //    return uvs[1];
            //if (Mathf.ApproximatelyEquals(interpolation_point, vertices[2]))
            //    return uvs[2];

            Vector2 edge1 = vertices[1] - vertices[0];
            Vector2 edge2 = vertices[2] - vertices[0];
            Vector2 interpolation = interpolation_point - vertices[0];

            double edge1_on_edge1 = Vector2.Dot(edge1, edge1);
            double edge1_on_edge2 = Vector2.Dot(edge1, edge2);
            double edge2_on_edge2 = Vector2.Dot(edge2, edge2);
            double inter_on_edge1 = Vector2.Dot(interpolation, edge1);
            double inter_on_edge2 = Vector2.Dot(interpolation, edge2);
            double scale = (edge1_on_edge1 * edge2_on_edge2 - edge1_on_edge2 * edge1_on_edge2);
            if (scale == 0)
                return uvs[0];

            double v = (edge2_on_edge2 * inter_on_edge1 - edge1_on_edge2 * inter_on_edge2) / scale;
            double w = (edge1_on_edge1 * inter_on_edge2 - edge1_on_edge2 * inter_on_edge1) / scale;
            double u = 1.0f - v - w;

            return uvs[0] * u + uvs[1] * v + uvs[2] * w;
        }

        static bool IsTriangleDegenerate(Vector2[] vertices, double tolerance)
        {
            double det = vertices[0].x * vertices[1].y - vertices[0].x * vertices[2].y +
                    vertices[0].y * vertices[2].x - vertices[0].y * vertices[1].x +
                    vertices[1].x * vertices[2].y - vertices[1].y * vertices[2].x;

            return det < tolerance;
        }

        internal Build2DFaces() { }

        internal Build2DFaces(CSGBrush brush, int face_idx)
        {
            // Convert 3D vertex points to 2D.
            Vector3[] points_3D = {
                brush.faces[face_idx].vertices[0],
                brush.faces[face_idx].vertices[1],
                brush.faces[face_idx].vertices[2]
            };
            plane = new Plane(points_3D[0], points_3D[1], points_3D[2]);
            to_3D = new TransformCSG();
            to_3D.SetPosition(points_3D[0]);
            to_3D.BasisSetColumn(2, plane.normal);
            Vector3 temp = points_3D[1] - points_3D[2];
            temp /= temp.magnitude;
            to_3D.BasisSetColumn(0, temp);
            temp = Vector3.Cross(to_3D.BasisGetColumn(0), to_3D.BasisGetColumn(2));
            temp /= temp.magnitude;
            to_3D.BasisSetColumn(1, temp);
            to_2D = to_3D.AffineInverse();

            Face2D face;
            face.vertex_idx = new int[3];
            for (int i = 0; i < 3; i++)
            {
                Vertex2D vertex;
                Vector3 point_2D = to_2D.XForm(points_3D[i]);
                vertex.point.x = point_2D.x;
                vertex.point.y = point_2D.y;
                vertex.uv = brush.faces[face_idx].uvs[i];
                vertices.Add(vertex);
                face.vertex_idx[i] = i;
            }
            faces.Add(face);
        }

        internal Build2DFaces(CSGBrush brush, int face_idx, CSGBrush brush_a)
        {
            // Convert 3D vertex points to 2D.
            Vector3[] points_3D = {
                brush_a.obj.transform.InverseTransformPoint(brush.obj.transform.TransformPoint(brush.faces[face_idx].vertices[0])),
                brush_a.obj.transform.InverseTransformPoint(brush.obj.transform.TransformPoint(brush.faces[face_idx].vertices[1])),
                brush_a.obj.transform.InverseTransformPoint(brush.obj.transform.TransformPoint(brush.faces[face_idx].vertices[2]))
            };
            plane = new Plane(points_3D[0], points_3D[1], points_3D[2]);
            to_3D = new TransformCSG();
            to_3D.SetPosition(points_3D[0]);
            to_3D.BasisSetColumn(2, plane.normal);
            Vector3 temp = points_3D[1] - points_3D[2];
            temp /= temp.magnitude;
            to_3D.BasisSetColumn(0, temp);
            temp = Vector3.Cross(to_3D.BasisGetColumn(0), to_3D.BasisGetColumn(2));
            temp /= temp.magnitude;
            to_3D.BasisSetColumn(1, temp);
            to_2D = to_3D.AffineInverse();

            Face2D face;
            face.vertex_idx = new int[3];
            for (int i = 0; i < 3; i++)
            {
                Vertex2D vertex;
                Vector3 point_2D = to_2D.XForm(points_3D[i]);
                vertex.point.x = point_2D.x;
                vertex.point.y = point_2D.y;
                vertex.uv = brush.faces[face_idx].uvs[i];
                vertices.Add(vertex);
                face.vertex_idx[i] = i;
            }
            faces.Add(face);
        }

        private int GetPointIndex(Vector2 point)
        {
            for (int vertex_idx = 0; vertex_idx < vertices.Count; ++vertex_idx)
                if ((vertices[vertex_idx].point - point).sqrMagnitude < vertex_tolerance)
                    return vertex_idx;
            return -1;
        }

        private int AddVertex(Vertex2D vertex)
        {
            // Check if vertex exists.
            int vertex_id = GetPointIndex(vertex.point);
            if (vertex_id != -1) return vertex_id;

            vertices.Add(vertex);
            return vertices.Count - 1;
        }

        private void AddVertexIndexSorted(List<int> vertex_indices, int new_vertex_index)
        {
            if (new_vertex_index >= 0 && vertex_indices.IndexOf(new_vertex_index) == -1)
            {
                // The first vertex.
                if (vertex_indices.Count == 0)
                {
                    vertex_indices.Add(new_vertex_index);
                    return;
                }

                // The second vertex.
                Vector2 first_point;
                Vector2 new_point;
                int axis;
                if (vertex_indices.Count == 1)
                {
                    first_point = vertices[vertex_indices[0]].point;
                    new_point = vertices[new_vertex_index].point;

                    // Sort along the axis with the greatest difference.
                    axis = 0;
                    if (Mathf.Abs(new_point.x - first_point.x) < Mathf.Abs(new_point.y - first_point.y))
                        axis = 1;

                    // Add it to the beginning or the end appropriately.
                    if (new_point[axis] < first_point[axis])
                        vertex_indices.Insert(0, new_vertex_index);
                    else
                        vertex_indices.Add(new_vertex_index);
                    return;
                }

                // Third vertices.
                first_point = vertices[vertex_indices[0]].point;
                Vector2 last_point = vertices[vertex_indices[vertex_indices.Count - 1]].point;
                new_point = vertices[new_vertex_index].point;

                // Determine axis being sorted against i.e. the axis with the greatest difference.
                axis = 0;
                if (Mathf.Abs(last_point.x - first_point.x) < Mathf.Abs(last_point.y - first_point.y))
                    axis = 1;

                // Insert the point at the appropriate index.
                for (int insert_idx = 0; insert_idx < vertex_indices.Count; ++insert_idx)
                {
                    Vector2 insert_point = vertices[vertex_indices[insert_idx]].point;
                    if (new_point[axis] < insert_point[axis])
                    {
                        vertex_indices.Insert(insert_idx, new_vertex_index);
                        return;
                    }
                }
                // New largest, add it to the end.
                vertex_indices.Add(new_vertex_index);
            }
        }

        private void MergeFaces(List<int> segment_indices)
        {
            int segments = segment_indices.Count - 1;
            if (segments < 2) return;

            // Faces around an inner vertex are merged by moving the inner vertex to the first vertex.
            for (int sorted_idx = 1; sorted_idx < segments; ++sorted_idx)
            {
                int closest_idx = 0;
                int inner_idx = segment_indices[sorted_idx];

                if (sorted_idx > segments / 2)
                {
                    // Merge to other segment end.
                    closest_idx = segments;
                    // Reverse the merge order.
                    inner_idx = segment_indices[segments + segments / 2 - sorted_idx];
                }

                // Find the mergeable faces.
                List<int> merge_faces_idx = new List<int>();
                List<Face2D> merge_faces = new List<Face2D>();
                List<int> merge_faces_inner_vertex_idx = new List<int>();
                for (int face_idx = 0; face_idx < faces.Count; ++face_idx)
                {
                    for (int face_vertex_idx = 0; face_vertex_idx < 3; ++face_vertex_idx)
                    {
                        if (faces[face_idx].vertex_idx[face_vertex_idx] == inner_idx)
                        {
                            merge_faces_idx.Add(face_idx);
                            merge_faces.Add(faces[face_idx]);
                            merge_faces_inner_vertex_idx.Add(face_vertex_idx);
                        }
                    }
                }

                List<int> degenerate_points = new List<int>();

                // Create the new faces.
                for (int merge_idx = 0; merge_idx < merge_faces.Count; ++merge_idx)
                {
                    int[] outer_edge_idx = new int[2];
                    outer_edge_idx[0] = merge_faces[merge_idx].vertex_idx[(merge_faces_inner_vertex_idx[merge_idx] + 1) % 3];
                    outer_edge_idx[1] = merge_faces[merge_idx].vertex_idx[(merge_faces_inner_vertex_idx[merge_idx] + 2) % 3];

                    // Skip flattened faces.
                    if (outer_edge_idx[0] == segment_indices[closest_idx] ||
                        outer_edge_idx[1] == segment_indices[closest_idx])
                        continue;

                    //Don't create degenerate triangles.
                    if (Mathf.AreLinesParallel(vertices[outer_edge_idx[0]].point, vertices[segment_indices[closest_idx]].point, 
                                               vertices[outer_edge_idx[1]].point, vertices[segment_indices[closest_idx]].point, 
                                               vertex_tolerance))
                    {
                        if (!degenerate_points.Contains(outer_edge_idx[0]))
                            degenerate_points.Add(outer_edge_idx[0]);
                        if (!degenerate_points.Contains(outer_edge_idx[1]))
                            degenerate_points.Add(outer_edge_idx[1]);
                        continue;
                    }

                    // Create new faces.
                    Face2D new_face;
                    new_face.vertex_idx = new int[3];
                    new_face.vertex_idx[0] = segment_indices[closest_idx];
                    new_face.vertex_idx[1] = outer_edge_idx[0];
                    new_face.vertex_idx[2] = outer_edge_idx[1];
                    faces.Add(new_face);
                }

                // Delete the old faces in reverse index order.
                merge_faces_idx.Sort();
                merge_faces_idx.Reverse();
                for (int i = 0; i < merge_faces_idx.Count; ++i)
                    faces.RemoveAt(merge_faces_idx[i]);

                if (degenerate_points.Count == 0)
                    continue;

                // Split faces using degenerate points.
                for (int face_idx = 0; face_idx < faces.Count; ++face_idx)
                {
                    Face2D face = faces[face_idx];
                    Vertex2D[] face_vertices = {
                        vertices[face.vertex_idx[0]],
                        vertices[face.vertex_idx[1]],
                        vertices[face.vertex_idx[2]]
                    };
                    Vector2[] face_points = {
                        face_vertices[0].point,
                        face_vertices[1].point,
                        face_vertices[2].point
                    };

                    for (int point_idx = 0; point_idx < degenerate_points.Count; ++point_idx)
                    {
                        int degenerate_idx = degenerate_points[point_idx];
                        Vector2 point_2D = vertices[degenerate_idx].point;

                        // Check if point is existing face vertex.
                        bool existing = false;
                        for (int i = 0; i < 3; ++i)
                        {
                            if ((face_vertices[i].point - point_2D).sqrMagnitude < vertex_tolerance)
                            {
                                existing = true;
                                break;
                            }
                        }
                        if (existing) continue;

                        // Check if point is on each edge.
                        for (int face_edge_idx = 0; face_edge_idx < 3; ++face_edge_idx)
                        {
                            Vector2[] edge_points = {
                                face_points[face_edge_idx],
                                face_points[(face_edge_idx + 1) % 3]
                            };
                            Vector2 closest_point = get_closest_point_to_segment(point_2D, edge_points);

                            if ((point_2D - closest_point).sqrMagnitude < vertex_tolerance)
                            {
                                int opposite_vertex_idx = face.vertex_idx[(face_edge_idx + 2) % 3];

                                // If new vertex snaps to degenerate vertex, just delete this face.
                                if (degenerate_idx == opposite_vertex_idx)
                                {
                                    faces.RemoveAt(face_idx);
                                    // Update index.
                                    --face_idx;
                                    break;
                                }

                                // Create two new faces around the new edge and remove this face.
                                // The new edge is the last edge.
                                Face2D left_face;
                                left_face.vertex_idx = new int[3];
                                left_face.vertex_idx[0] = degenerate_idx;
                                left_face.vertex_idx[1] = face.vertex_idx[(face_edge_idx + 1) % 3];
                                left_face.vertex_idx[2] = opposite_vertex_idx;
                                Face2D right_face;
                                right_face.vertex_idx = new int[3];
                                right_face.vertex_idx[0] = opposite_vertex_idx;
                                right_face.vertex_idx[1] = face.vertex_idx[face_edge_idx];
                                right_face.vertex_idx[2] = degenerate_idx;
                                faces.RemoveAt(face_idx);
                                faces.Insert(face_idx, right_face);
                                faces.Insert(face_idx, left_face);

                                // Don't check against the new faces.
                                ++face_idx;

                                // No need to check other edges.
                                break;
                            }
                        }
                    }
                }
            }
        }

        private void FindEdgeIntersections(Vector2[] segment_points, ref List<int> segment_indices)
        {
            // For each face.
            for (int face_idx = 0; face_idx < faces.Count; ++face_idx)
            {
                Face2D face = faces[face_idx];
                Vertex2D[] face_vertices = {
                    vertices[face.vertex_idx[0]],
                    vertices[face.vertex_idx[1]],
                    vertices[face.vertex_idx[2]]
                };

                // Check each edge.
                for (int face_edge_idx = 0; face_edge_idx < 3; ++face_edge_idx)
                {
                    Vector2[] edge_points = {
                        face_vertices[face_edge_idx].point,
                        face_vertices[(face_edge_idx + 1) % 3].point
                    };
                    Vector2[] edge_uvs = {
                        face_vertices[face_edge_idx].uv,
                        face_vertices[(face_edge_idx + 1) % 3].uv
                    };
                    Vector2 intersection_point = Vector2.zero;

                    // First check if the ends of the segment are on the edge.
                    bool on_edge = false;
                    for (int edge_point_idx = 0; edge_point_idx < 2; ++edge_point_idx)
                    {
                        intersection_point = get_closest_point_to_segment(segment_points[edge_point_idx], edge_points);
                        if ((segment_points[edge_point_idx] - intersection_point).sqrMagnitude < vertex_tolerance)
                        {
                            on_edge = true;
                            break;
                        }
                    }

                    // Else check if the segment intersects the edge.
                    if (on_edge || Mathf.DoesLineIntersectLine(segment_points[0], segment_points[1], edge_points[0], edge_points[1], out intersection_point))
                    {
                        // Check if intersection point is an edge point.
                        if (((edge_points[0] - intersection_point).sqrMagnitude < vertex_tolerance) ||
                            ((edge_points[1] - intersection_point).sqrMagnitude < vertex_tolerance))
                            continue;

                        // Check if edge exists, by checking if the intersecting segment is parallel to the edge.
                        if (Mathf.AreLinesParallel(segment_points[0], segment_points[1], edge_points[0], edge_points[1], vertex_tolerance))
                            continue;

                        // Add the intersection point as a new vertex.
                        Vertex2D new_vertex;
                        new_vertex.point = intersection_point;
                        new_vertex.uv = LerpEdgeUV(edge_points, edge_uvs, intersection_point);
                        int new_vertex_idx = AddVertex(new_vertex);
                        int opposite_vertex_idx = face.vertex_idx[(face_edge_idx + 2) % 3];
                        AddVertexIndexSorted(segment_indices, new_vertex_idx);

                        // If new vertex snaps to opposite vertex, just delete this face.
                        if (new_vertex_idx == opposite_vertex_idx)
                        {
                            faces.RemoveAt(face_idx);
                            // Update index.
                            --face_idx;
                            break;
                        }

                        // If opposite point is on the segment, add its index to segment indices too.
                        Vector2 closest_point = get_closest_point_to_segment(vertices[opposite_vertex_idx].point, segment_points);
                        if ((vertices[opposite_vertex_idx].point - closest_point).sqrMagnitude < vertex_tolerance)
                        {
                            AddVertexIndexSorted(segment_indices, opposite_vertex_idx);
                        }

                        // Create two new faces around the new edge and remove this face.
                        // The new edge is the last edge.
                        Face2D left_face;
                        left_face.vertex_idx = new int[3];
                        left_face.vertex_idx[0] = new_vertex_idx;
                        left_face.vertex_idx[1] = face.vertex_idx[(face_edge_idx + 1) % 3];
                        left_face.vertex_idx[2] = opposite_vertex_idx;
                        Face2D right_face;
                        right_face.vertex_idx = new int[3];
                        right_face.vertex_idx[0] = opposite_vertex_idx;
                        right_face.vertex_idx[1] = face.vertex_idx[face_edge_idx];
                        right_face.vertex_idx[2] = new_vertex_idx;
                        faces.RemoveAt(face_idx);
                        faces.Insert(face_idx, right_face);
                        faces.Insert(face_idx, left_face);

                        // Check against the new faces.
                        --face_idx;
                        break;
                    }
                }
            }
        }

        private int InsertPoint(Vector2 point)
        {
            int new_vertex_idx = -1;

            for (int face_idx = 0; face_idx < faces.Count; ++face_idx)
            {
                Face2D face = faces[face_idx];
                Vertex2D[] face_vertices = {
                    vertices[face.vertex_idx[0]],
                    vertices[face.vertex_idx[1]],
                    vertices[face.vertex_idx[2]]
                };
                Vector2[] points = {
                    face_vertices[0].point,
                    face_vertices[1].point,
                    face_vertices[2].point
                };
                Vector2[] uvs = {
                    face_vertices[0].uv,
                    face_vertices[1].uv,
                    face_vertices[2].uv
                };

                // Skip degenerate triangles.
                if (IsTriangleDegenerate(points, vertex_tolerance))
                    continue;

                // Check if point is existing face vertex.
                for (int i = 0; i < 3; ++i)
                    if ((face_vertices[i].point - point).sqrMagnitude < vertex_tolerance)
                        return face.vertex_idx[i];

                // Check if point is on each edge.
                bool on_edge = false;
                for (int face_edge_idx = 0; face_edge_idx < 3; ++face_edge_idx)
                {
                    Vector2[] edge_points = {
                        points[face_edge_idx],
                        points[(face_edge_idx + 1) % 3]
                    };
                    Vector2[] edge_uvs = {
                        uvs[face_edge_idx],
                        uvs[(face_edge_idx + 1) % 3]
                    };

                    Vector2 closest_point = get_closest_point_to_segment(point, edge_points);
                    if ((point - closest_point).sqrMagnitude < vertex_tolerance)
                    {
                        on_edge = true;

                        // Add the point as a new vertex.
                        Vertex2D new_vertex;
                        new_vertex.point = point;
                        new_vertex.uv = LerpEdgeUV(edge_points, edge_uvs, point);
                        new_vertex_idx = AddVertex(new_vertex);
                        int opposite_vertex_idx = face.vertex_idx[(face_edge_idx + 2) % 3];

                        // If new vertex snaps to opposite vertex, just delete this face.
                        if (new_vertex_idx == opposite_vertex_idx)
                        {
                            faces.RemoveAt(face_idx);
                            // Update index.
                            --face_idx;
                            break;
                        }

                        // Don't create degenerate triangles.
                        if (Mathf.AreLinesParallel(vertices[new_vertex_idx].point, edge_points[0], vertices[new_vertex_idx].point, vertices[opposite_vertex_idx].point, vertex_tolerance) &&
                            Mathf.AreLinesParallel(vertices[new_vertex_idx].point, edge_points[1], vertices[new_vertex_idx].point, vertices[opposite_vertex_idx].point, vertex_tolerance))
                        {
                            break;
                        }

                        // Create two new faces around the new edge and remove this face.
                        // The new edge is the last edge.
                        Face2D left_face;
                        left_face.vertex_idx = new int[3];
                        left_face.vertex_idx[0] = new_vertex_idx;
                        left_face.vertex_idx[1] = face.vertex_idx[(face_edge_idx + 1) % 3];
                        left_face.vertex_idx[2] = opposite_vertex_idx;
                        Face2D right_face;
                        right_face.vertex_idx = new int[3];
                        right_face.vertex_idx[0] = opposite_vertex_idx;
                        right_face.vertex_idx[1] = face.vertex_idx[face_edge_idx];
                        right_face.vertex_idx[2] = new_vertex_idx;
                        faces.RemoveAt(face_idx);
                        faces.Insert(face_idx, right_face);
                        faces.Insert(face_idx, left_face);

                        // Don't check against the new faces.
                        ++face_idx;

                        // No need to check other edges.
                        break;
                    }
                }

                // If not on an edge, check if the point is inside the face.
                if (!on_edge && Mathf.IsPointInTriangle(point, face_vertices[0].point, face_vertices[1].point, face_vertices[2].point))
                {
                    // Add the point as a new vertex.
                    Vertex2D new_vertex;
                    new_vertex.point = point;
                    new_vertex.uv = LerpTriangleUV(points, uvs, point);
                    new_vertex_idx = AddVertex(new_vertex);

                    // Create three new faces around this point and remove this face.
                    // The new vertex is the last vertex.
                    for (int i = 0; i < 3; ++i)
                    {
                        // Don't create degenerate triangles.
                        Vector2[] new_points = { points[i], points[(i + 1) % 3], vertices[new_vertex_idx].point };
                        if (IsTriangleDegenerate(new_points, vertex_tolerance)) continue;

                        Face2D new_face;
                        new_face.vertex_idx = new int[3];
                        new_face.vertex_idx[0] = face.vertex_idx[i];
                        new_face.vertex_idx[1] = face.vertex_idx[(i + 1) % 3];
                        new_face.vertex_idx[2] = new_vertex_idx;
                        faces.Add(new_face);
                    }
                    faces.RemoveAt(face_idx);

                    // No need to check other faces.
                    break;
                }
            }

            return new_vertex_idx;
        }

        internal void Insert(CSGBrush brush, int face_idx, CSGBrush brush_a = null)
        {
            Vector2[] points_2D = new Vector2[3];
            int points_count = 0;

            for (int i = 0; i < 3; i++)
            {
                Vector3 point_3D;
                if (brush_a == null)
                    point_3D = brush.faces[face_idx].vertices[i];
                else
                    point_3D = brush_a.obj.transform.InverseTransformPoint(brush.obj.transform.TransformPoint(brush.faces[face_idx].vertices[i]));

                if (plane.IsOnPlane(point_3D, Mathf.Small))
                {
                    // Point is in the plane, add it.
                    Vector3 point_2D = Vector3.ProjectOnPlane(point_3D, plane.normal);
                    point_2D = to_2D.XForm(point_2D);
                    points_2D[points_count++] = new Vector2(point_2D.x, point_2D.y);

                }
                else
                {
                    Vector3 next_point_3D;
                    if (brush_a == null)
                        next_point_3D = brush.faces[face_idx].vertices[(i + 1) % 3];
                    else
                        next_point_3D = brush_a.obj.transform.InverseTransformPoint(brush.obj.transform.TransformPoint(brush.faces[face_idx].vertices[(i + 1) % 3]));

                    if (plane.IsOnPlane(next_point_3D, Mathf.Small))
                        continue; // Next point is in plane, it will be added separately.
                    if (plane.IsOnPositiveSide(point_3D) == plane.IsOnPositiveSide(next_point_3D))
                        continue; // Both points on the same side of the plane, ignore.

                    // Edge crosses the plane, find and add the intersection point.
                    if (plane.DoesLineIntersectPlane(point_3D, next_point_3D, out Vector3 point_2D))
                    {
                        point_2D = to_2D.XForm(point_2D);
                        points_2D[points_count++] = new Vector2(point_2D.x, point_2D.y);
                    }
                }
            }

            List<int> segment_indices = new List<int>();
            Vector2[] segment = new Vector2[2];
            int[] inserted_index = { -1, -1, -1 };

            // Insert points.
            for (int i = 0; i < points_count; ++i)
                inserted_index[i] = InsertPoint(points_2D[i]);

            if (points_count == 2)
            {
                // Insert a single segment.
                segment[0] = points_2D[0];
                segment[1] = points_2D[1];
                FindEdgeIntersections(segment, ref segment_indices);
                for (int i = 0; i < 2; ++i)
                    AddVertexIndexSorted(segment_indices, inserted_index[i]);
                MergeFaces(segment_indices);
            }

            if (points_count == 3)
            {
                // Insert three segments.
                for (int edge_idx = 0; edge_idx < 3; ++edge_idx)
                {
                    segment[0] = points_2D[edge_idx];
                    segment[1] = points_2D[(edge_idx + 1) % 3];
                    FindEdgeIntersections(segment, ref segment_indices);
                    for (int i = 0; i < 2; ++i)
                        AddVertexIndexSorted(segment_indices, inserted_index[(edge_idx + i) % 3]);
                    MergeFaces(segment_indices);
                    segment_indices.Clear();
                }
            }
        }

        internal void AddFacesToMesh(ref MeshMerge mesh_merge, bool from_b)
        {
            for (int face_idx = 0; face_idx < faces.Count; ++face_idx)
            {
                Face2D face = faces[face_idx];
                Vertex2D[] fv = {
                    vertices[face.vertex_idx[0]],
                    vertices[face.vertex_idx[1]],
                    vertices[face.vertex_idx[2]]
                };

                // Convert 2D vertex points to 3D.
                Vector3[] points_3D = new Vector3[3];
                Vector2[] uvs = new Vector2[3];
                for (int i = 0; i < 3; ++i)
                {
                    Vector3 point_2D = new Vector3(fv[i].point.x, fv[i].point.y, 0);
                    points_3D[i] = to_3D.XForm(point_2D);
                    uvs[i] = fv[i].uv;
                }

                mesh_merge.AddFace(points_3D, uvs, from_b);
            }
        }
    }
}
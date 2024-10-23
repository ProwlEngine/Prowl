using System.Numerics;

namespace Prowl.Unwrapper
{
    public static class Triangulator
    {
        private static Vector3 CalculateNormal(Vector3 p1, Vector3 p2, Vector3 p3)
        {
            var side1 = p2 - p1;
            var side2 = p3 - p1;
            var perp = Vector3.Cross(side1, side2);
            return Vector3.Normalize(perp);
        }

        private static float CalculateAngle360(Vector3 a, Vector3 b, Vector3 direct)
        {
            const float Rad2Deg = 180.0f / (float)Math.PI;
            var angle = (float)Math.Atan2(Vector3.Cross(a, b).Length(), Vector3.Dot(a, b)) * Rad2Deg;
            var cross = Vector3.Cross(a, b);
            if (Vector3.Dot(cross, direct) < 0)
            {
                angle = 360 - angle;
            }
            return angle;
        }

        private static Vector3 VertexToVector3(Vertex vertex)
        {
            return new Vector3(vertex.position[0], vertex.position[1], vertex.position[2]);
        }

        private static bool IsPointInTriangle(Vector3 a, Vector3 b, Vector3 c, Vector3 p)
        {
            var u = b - a;
            var v = c - a;
            var w = p - a;

            var vXw = Vector3.Cross(v, w);
            var vXu = Vector3.Cross(v, u);

            if (Vector3.Dot(vXw, vXu) < 0.0f)
                return false;

            var uXw = Vector3.Cross(u, w);
            var uXv = Vector3.Cross(u, v);

            if (Vector3.Dot(uXw, uXv) < 0.0f)
                return false;

            var denom = uXv.Length();
            var r = vXw.Length() / denom;
            var t = uXw.Length() / denom;

            return r + t <= 1.0f;
        }

        private static Vector3 CalculateRingNormal(List<Vertex> vertices, List<int> ring)
        {
            var normal = Vector3.Zero;

            for (int i = 0; i < ring.Count; i++)
            {
                int j = (i + 1) % ring.Count;
                int k = (i + 2) % ring.Count;

                var enter = VertexToVector3(vertices[ring[i]]);
                var cone = VertexToVector3(vertices[ring[j]]);
                var leave = VertexToVector3(vertices[ring[k]]);

                normal += CalculateNormal(enter, cone, leave);
            }

            return Vector3.Normalize(normal);
        }

        public static void Triangulate(List<Vertex> vertices, List<Face> faces, List<int> ring)
        {
            if (ring.Count < 3)
                return;

            var fillRing = new List<int>(ring);
            var direct = CalculateRingNormal(vertices, fillRing);

            while (fillRing.Count > 3)
            {
                bool newFaceGenerated = false;

                for (int i = 0; i < fillRing.Count; i++)
                {
                    int j = (i + 1) % fillRing.Count;
                    int k = (i + 2) % fillRing.Count;

                    var enter = VertexToVector3(vertices[fillRing[i]]);
                    var cone = VertexToVector3(vertices[fillRing[j]]);
                    var leave = VertexToVector3(vertices[fillRing[k]]);

                    var angle = CalculateAngle360(cone - enter, leave - cone, direct);

                    if (angle >= 1.0f && angle <= 179.0f)
                    {
                        bool isEar = true;

                        // Check if any other point lies inside this triangle
                        for (int x = 0; x < fillRing.Count - 3; x++)
                        {
                            int h = (i + 3 + x) % fillRing.Count;
                            var fourth = VertexToVector3(vertices[fillRing[h]]);

                            if (IsPointInTriangle(enter, cone, leave, fourth))
                            {
                                isEar = false;
                                break;
                            }
                        }

                        if (isEar)
                        {
                            var newFace = new Face {
                                indices =
                                [
                                    fillRing[i],
                                    fillRing[j],
                                    fillRing[k]
                                ]
                            };

                            faces.Add(newFace);
                            fillRing.RemoveAt(j);
                            newFaceGenerated = true;
                            break;
                        }
                    }
                }

                if (!newFaceGenerated)
                    break;
            }

            // Handle the final triangle
            if (fillRing.Count == 3)
            {
                var newFace = new Face {
                    indices =
                    [
                        fillRing[0],
                        fillRing[1],
                        fillRing[2]
                    ]
                };
                faces.Add(newFace);
            }
        }
    }
}
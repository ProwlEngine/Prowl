using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace AssimpSharp
{
    public class TriangulateProcess : BaseProcess
    {
        public override bool IsActive(int flags)
        {
            return ((AiPostProcessSteps)flags).HasFlag(AiPostProcessSteps.Triangulate);
        }

        public override void Execute(AiScene scene)
        {
            Console.WriteLine("TriangulateProcess begin");

            bool has = false;
            for (int a = 0; a < scene.NumMeshes; a++)
            {
                if (TriangulateMesh(scene.Meshes[a]))
                    has = true;
            }

            if (has)
                Console.WriteLine("TriangulateProcess finished. All polygons have been triangulated.");
            else
                Console.WriteLine("TriangulateProcess finished. There was nothing to be done.");
        }

        private bool TriangulateMesh(AiMesh mesh)
        {
            if (mesh.PrimitiveType == 0)
            {
                bool need = false;
                for (int a = 0; a < mesh.NumFaces; a++)
                {
                    if (mesh.Faces[a].Count != 3)
                        need = true;
                }
                if (!need)
                    return false;
            }
            else if (!mesh.PrimitiveType.HasFlag(AiPrimitiveType.Polygon))
            {
                return false;
            }

            int numOut = 0;
            int maxOut = 0;
            bool getNormals = true;

            for (int a = 0; a < mesh.NumFaces; a++)
            {
                AiFace face = mesh.Faces[a];
                if (face.Count <= 4)
                    getNormals = false;
                if (face.Count <= 3)
                    numOut++;
                else
                {
                    numOut += face.Count - 2;
                    maxOut = Math.Max(maxOut, face.Count);
                }
            }

            System.Diagnostics.Debug.Assert(numOut != mesh.NumFaces);

            List<Vector3> norOut = null;
            if (mesh.Normals.Count == 0 && getNormals)
            {
                norOut = new List<Vector3>(new Vector3[mesh.NumVertices]);
                mesh.Normals = norOut;
            }

            mesh.PrimitiveType |= AiPrimitiveType.Triangle;
            mesh.PrimitiveType &= ~AiPrimitiveType.Polygon;

            List<AiFace> outFaces = new List<AiFace>(numOut);
            int curOut = 0;
            Vector3[] tempVerts3d = new Vector3[maxOut + 2];
            Vector2[] tempVerts = new Vector2[maxOut + 2];

            List<Vector3> verts = mesh.Vertices;

            bool[] done = new bool[maxOut];
            for (int a = 0; a < mesh.NumFaces; a++)
            {
                AiFace face = mesh.Faces[a];
                List<int> idx = face;
                int num = idx.Count;
                int next = 0;
                int prev = num - 1;
                int max = num;

                int lastFace = curOut;

                if (idx.Count <= 3)
                {
                    AiFace nFace = new AiFace();
                    nFace.AddRange(idx);
                    outFaces.Add(nFace);
                    curOut++;
                    face.Clear();
                    continue;
                }
                else if (idx.Count == 4)
                {
                    // Optimized code for quadrilaterals
                    int startVertex = FindConcaveQuadVertex(verts, idx);

                    var temp = new int[4];
                    for (int i = 0; i < 4; i++)
                        temp[i] = idx[i];

                    AiFace nFace1 = new AiFace();
                    nFace1.Add(temp[startVertex]);
                    nFace1.Add(temp[(startVertex + 1) % 4]);
                    nFace1.Add(temp[(startVertex + 2) % 4]);
                    outFaces.Add(nFace1);
                    curOut++;

                    AiFace nFace2 = new AiFace();
                    nFace2.Add(temp[startVertex]);
                    nFace2.Add(temp[(startVertex + 2) % 4]);
                    nFace2.Add(temp[(startVertex + 3) % 4]);
                    outFaces.Add(nFace2);
                    curOut++;

                    face.Clear();
                    continue;
                }
                else
                {
                    // Arbitrary polygons
                    for (int tmp = 0; tmp < max; tmp++)
                        tempVerts3d[tmp] = verts[idx[tmp]];

                    Vector3 n = new Vector3();
                    NewellNormal(n, max, tempVerts3d);

                    if (norOut != null)
                    {
                        for (int tmp = 0; tmp < max; tmp++)
                            norOut[idx[tmp]] = n;
                    }

                    int ac = 0, bc = 1;
                    float inv = n.Z;
                    if (Math.Abs(n.X) > Math.Abs(n.Y) && Math.Abs(n.X) > Math.Abs(n.Z))
                    {
                        ac = 1; bc = 2;
                        inv = n.X;
                    }
                    else if (Math.Abs(n.Y) > Math.Abs(n.Z))
                    {
                        ac = 2; bc = 0;
                        inv = n.Y;
                    }

                    if (inv < 0)
                    {
                        int t = ac;
                        ac = bc;
                        bc = t;
                    }

                    for (int tmp = 0; tmp < max; tmp++)
                    {
                        tempVerts[tmp].X = verts[idx[tmp]][ac];
                        tempVerts[tmp].Y = verts[idx[tmp]][bc];
                        done[tmp] = false;
                    }

                    while (num > 3)
                    {
                        int ear = FindEar(tempVerts, done, num, next, prev);

                        if (ear == -1)
                        {
                            Console.WriteLine("Failed to triangulate polygon (no ear found). Probably not a simple polygon?");
                            break;
                        }

                        AiFace nFace = new AiFace();
                        nFace.Add(idx[prev]);
                        nFace.Add(idx[ear]);
                        nFace.Add(idx[next]);
                        outFaces.Add(nFace);
                        curOut++;

                        done[ear] = true;
                        num--;

                        if (num > 3)
                        {
                            prev = GetPreviousIndex(ear, num, done);
                            next = GetNextIndex(ear, num, done);
                        }
                    }

                    if (num == 3)
                    {
                        AiFace nFace = new AiFace();
                        for (int i = 0; i < max; i++)
                        {
                            if (!done[i])
                                nFace.Add(idx[i]);
                        }
                        outFaces.Add(nFace);
                        curOut++;
                    }
                }

                for (int f = lastFace; f < curOut; f++)
                {
                    List<int> indices = outFaces[f];

                    float abs = Math.Abs(GetArea2D(tempVerts[indices[0]], tempVerts[indices[1]], tempVerts[indices[2]]));
                    if (abs < 1e-5f)
                    {
                        Console.WriteLine("Dropping triangle with area 0");
                        curOut--;
                        outFaces.RemoveAt(f);
                        f--;
                        continue;
                    }

                    indices[0] = idx[indices[0]];
                    indices[1] = idx[indices[1]];
                    indices[2] = idx[indices[2]];
                }

                face.Clear();
            }

            mesh.Faces.Clear();
            mesh.Faces.AddRange(outFaces.Take(curOut));
            mesh.NumFaces = curOut;

            return true;
        }

        private static float GetArea2D(Vector2 v1, Vector2 v2, Vector2 v3)
        {
            return 0.5f * (v1.X * (v3.Y - v2.Y) + v2.X * (v1.Y - v3.Y) + v3.X * (v2.Y - v1.Y));
        }

        private static bool OnLeftSideOfLine2D(Vector2 p0, Vector2 p1, Vector2 p2)
        {
            return GetArea2D(p0, p2, p1) > 0;
        }

        private static bool PointInTriangle2D(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 pp)
        {
            Vector2 v0 = p1 - p0;
            Vector2 v1 = p2 - p0;
            Vector2 v2 = pp - p0;

            float dot00 = Vector2.Dot(v0, v0);
            float dot01 = Vector2.Dot(v0, v1);
            float dot02 = Vector2.Dot(v0, v2);
            float dot11 = Vector2.Dot(v1, v1);
            float dot12 = Vector2.Dot(v1, v2);

            float invDenom = 1 / (dot00 * dot11 - dot01 * dot01);
            float u = (dot11 * dot02 - dot01 * dot12) * invDenom;
            float v = (dot00 * dot12 - dot01 * dot02) * invDenom;

            return (u > 0) && (v > 0) && (u + v < 1);
        }

        private static void NewellNormal(Vector3 normal, int num, Vector3[] vecs)
        {
            normal.X = normal.Y = normal.Z = 0;

            for (int i = 0; i < num; i++)
            {
                int i1 = (i + 1) % num;
                normal.X += (vecs[i].Y - vecs[i1].Y) * (vecs[i].Z + vecs[i1].Z);
                normal.Y += (vecs[i].Z - vecs[i1].Z) * (vecs[i].X + vecs[i1].X);
                normal.Z += (vecs[i].X - vecs[i1].X) * (vecs[i].Y + vecs[i1].Y);
            }

            normal = Vector3.Normalize(normal);
        }

        private static int FindConcaveQuadVertex(List<Vector3> verts, List<int> face)
        {
            for (int i = 0; i < 4; i++)
            {
                Vector3 v0 = verts[face[(i + 3) % 4]];
                Vector3 v1 = verts[face[(i + 2) % 4]];
                Vector3 v2 = verts[face[(i + 1) % 4]];
                Vector3 v = verts[face[i]];

                Vector3 left = Vector3.Normalize(v0 - v);
                Vector3 diag = Vector3.Normalize(v1 - v);
                Vector3 right = Vector3.Normalize(v2 - v);

                float angle = (float)(Math.Acos(Vector3.Dot(left, diag)) + Math.Acos(Vector3.Dot(right, diag)));
                if (angle > Math.PI)
                {
                    return i;
                }
            }
            return 0;
        }

        private static int FindEar(Vector2[] verts, bool[] done, int num, int start, int prev)
        {
            int ear = start;
            int counter = 0;
            while (true)
            {
                int next = GetNextIndex(ear, num, done);

                if (!OnLeftSideOfLine2D(verts[prev], verts[next], verts[ear]))
                {
                    prev = ear;
                    ear = next;
                    if (++counter == num)
                        return -1;
                    continue;
                }

                bool is_ear = true;
                for (int i = 0; i < num; i++)
                {
                    if (i == ear || i == prev || i == next || done[i])
                        continue;

                    if (PointInTriangle2D(verts[prev], verts[ear], verts[next], verts[i]))
                    {
                        is_ear = false;
                        break;
                    }
                }

                if (is_ear)
                    return ear;

                prev = ear;
                ear = next;

                if (++counter == num)
                    return -1;
            }
        }

        private static int GetNextIndex(int index, int num, bool[] done)
        {
            int next = index;
            do
            {
                next = (next + 1) % num;
            } while (done[next]);
            return next;
        }

        private static int GetPreviousIndex(int index, int num, bool[] done)
        {
            int prev = index;
            do
            {
                prev = (prev - 1 + num) % num;
            } while (done[prev]);
            return prev;
        }
    }
}

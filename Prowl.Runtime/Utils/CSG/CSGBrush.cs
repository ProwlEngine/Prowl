using System;
using System.Collections.Generic;

namespace Prowl.Runtime.CSG
{
    public class CSGBrush
    {
        public GameObject obj;

        public struct Face
        {
            public List<Vector3> vertices;
            public Vector2[] uvs;
            public AABBCSG aabb;
        };

        public Face[] faces;

        public CSGBrush(GameObject objet)
        {
            faces = new Face[0];
            obj = objet;
        }

        public void BuildFromFaces(List<Vector3> vertices, List<Vector2> uvs)
        {
            Array.Clear(faces, 0, faces.Length);
            List<Vector3> rv = vertices;
            List<Vector2> ruv = uvs;

            Array.Resize(ref faces, vertices.Count / 3);

            for (int i = 0; i < faces.Length; i++)
            {
                Face new_face = new();
                new_face.vertices = [ vertices[i * 3 + 2], vertices[i * 3 + 1], vertices[i * 3 + 0] ];
                new_face.uvs = [ ruv[i * 3 + 2], ruv[i * 3 + 1], ruv[i * 3 + 0] ];
                faces[i] = new_face;
            }

            RegenerateFaceAABBs();
        }

        public void BuildFromMesh(Mesh mesh)
        {
            Array.Clear(faces, 0, faces.Length);

            Array.Resize(ref faces, mesh.Indices.Length / 3);

            var verts = mesh.Vertices;
            var ind = mesh.Indices;
            var uvs = mesh.UV;
            for (int i = 0; i < faces.Length; i++)
            {
                Face new_face = new Face();
                new_face.vertices = new(3);
                new_face.vertices.Add(verts[ind[i * 3 + 2]]);
                new_face.vertices.Add(verts[ind[i * 3 + 1]]);
                new_face.vertices.Add(verts[ind[i * 3 + 0]]);

                new_face.uvs = new Vector2[3] { uvs[ind[i * 3 + 2]], uvs[ind[i * 3 + 1]], uvs[ind[i * 3 + 0]] };

                faces[i] = new_face;
            }
        }

        public void RegenerateFaceAABBs()
        {
            for (int i = 0; i < faces.Length; i++)
            {
                faces[i].aabb = new AABBCSG();
                faces[i].aabb.SetPosition(obj.Transform.TransformPoint(faces[i].vertices[0]));
                faces[i].aabb.Encapsulate(obj.Transform.TransformPoint(faces[i].vertices[1]));
                faces[i].aabb.Encapsulate(obj.Transform.TransformPoint(faces[i].vertices[2]));
            }
        }

        public Mesh GetMesh(Mesh m = null)
        {
            if (m == null)
            {
                m = new Mesh();
            }

            System.Numerics.Vector3[] vert = new System.Numerics.Vector3[faces.Length * 3];
            System.Numerics.Vector2[] uv = new System.Numerics.Vector2[faces.Length * 3];
            uint[] triangles = new uint[faces.Length * 3];
            for (uint i = 0; i < faces.Length; i++)
            {
                vert[3 * i + 2] = faces[i].vertices[0];
                vert[3 * i + 1] = faces[i].vertices[1];
                vert[3 * i + 0] = faces[i].vertices[2];
                uv[3 * i + 2] = faces[i].uvs[0];
                uv[3 * i + 1] = faces[i].uvs[1];
                uv[3 * i + 0] = faces[i].uvs[2];
                triangles[3 * i + 0] = (3 * i);
                triangles[3 * i + 1] = (3 * i + 1);
                triangles[3 * i + 2] = (3 * i + 2);
            }
            m.Vertices = vert;
            m.Indices = triangles;
            m.RecalculateNormals();
            m.RecalculateTangents();
            return m;
        }

    }
}
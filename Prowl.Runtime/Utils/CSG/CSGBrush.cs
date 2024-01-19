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
                Face new_face = new Face();
                new_face.vertices = new List<Vector3>(3);
                new_face.vertices.Add(vertices[i * 3 + 2]);
                new_face.vertices.Add(vertices[i * 3 + 1]);
                new_face.vertices.Add(vertices[i * 3 + 0]);
                new_face.uvs = new Vector2[3];
                new_face.uvs[0] = ruv[i * 3 + 2];
                new_face.uvs[1] = ruv[i * 3 + 1];
                new_face.uvs[2] = ruv[i * 3 + 0];

                faces[i] = new_face;
            }

            RegenFaceAABB();
        }

        public void BuildFromMesh(Mesh mesh)
        {
            Array.Clear(faces, 0, faces.Length);

            Array.Resize(ref faces, mesh.triangles.Length / 3);

            for (int i = 0; i < faces.Length; i++)
            {
                Face new_face = new Face();
                new_face.vertices = new List<Vector3>(3);
                new_face.vertices.Add(mesh.vertices[mesh.triangles[i * 3 + 2]].Position);
                new_face.vertices.Add(mesh.vertices[mesh.triangles[i * 3 + 1]].Position);
                new_face.vertices.Add(mesh.vertices[mesh.triangles[i * 3 + 0]].Position);

                new_face.uvs = new Vector2[3] { mesh.vertices[mesh.triangles[i * 3 + 2]].TexCoord, mesh.vertices[mesh.triangles[i * 3 + 1]].TexCoord, mesh.vertices[mesh.triangles[i * 3 + 0]].TexCoord };

                faces[i] = new_face;
            }
        }

        public void RegenFaceAABB()
        {
            for (int i = 0; i < faces.Length; i++)
            {
                faces[i].aabb = new AABBCSG();
                faces[i].aabb.SetPosition(obj.transform.TransformPoint(faces[i].vertices[0]));
                faces[i].aabb.Encapsulate(obj.transform.TransformPoint(faces[i].vertices[1]));
                faces[i].aabb.Encapsulate(obj.transform.TransformPoint(faces[i].vertices[2]));
            }
        }

        public Mesh GetMesh(Mesh m = null)
        {
            if (m == null)
                m = new Mesh();

            Mesh.Vertex[] vert = new Mesh.Vertex[faces.Length * 3];
            ushort[] triangles = new ushort[faces.Length * 3];
            for (int i = 0; i < faces.Length; i++)
            {
                var a = new Mesh.Vertex();
                var b = new Mesh.Vertex();
                var c = new Mesh.Vertex();
                a.Position = faces[i].vertices[0];
                a.TexCoord = faces[i].uvs[0];
                b.Position = faces[i].vertices[1];
                b.TexCoord = faces[i].uvs[1];
                c.Position = faces[i].vertices[2];
                c.TexCoord = faces[i].uvs[2];


                vert[3 * i + 2] = a;
                vert[3 * i + 1] = b;
                vert[3 * i] = c;
                triangles[3 * i] = (ushort)(3 * i);
                triangles[3 * i + 1] = (ushort)(3 * i + 1);
                triangles[3 * i + 2] = (ushort)(3 * i + 2);
            }
            m.vertices = vert;
            m.triangles = triangles;
            //m.RecalculateNormals();
            return m;
        }

    }
}
using System;
using System.Collections.Generic;
using Veldrid;

namespace Prowl.Runtime
{
    public static class Gizmos
    {
        private readonly static List<(Gizmo, Matrix4x4)> gizmoDrawQueue = new(100);
        private static Material gizmoMaterial;

        public static Matrix4x4 Matrix = Matrix4x4.Identity;
        public static Color Color = Color.white;

        public static void DrawLine(Vector3 from, Vector3 to)
        {
            Add(new LineGizmo(from, to, Color));
        }

        public static void DrawCube(Vector3 center, Vector3 size)
        {
            Matrix = Matrix4x4.CreateScale(size) * Matrix * Matrix4x4.CreateTranslation(center);
            Add(new CubeGizmo(Color));
        }

        public static void DrawCylinder(Vector3 center, float radius, float height)
        {
            Matrix = Matrix4x4.CreateScale(new Vector3(radius, height, radius)) * Matrix * Matrix4x4.CreateTranslation(center);
            Add(new CylinderGizmo(Color));
        }

        public static void DrawArc(Vector3 center, float radius, float startAngle, float degrees)
        {
            Matrix = Matrix4x4.CreateScale(new Vector3(radius)) * Matrix * Matrix4x4.CreateTranslation(center);
            Add(new ArcGizmo(startAngle, degrees, Color));
        }

        public static void DrawSphere(Vector3 center, float radius)
        {
            Matrix = Matrix4x4.CreateScale(new Vector3(radius)) * Matrix * Matrix4x4.CreateTranslation(center);
            Add(new SphereGizmo(Color));
        }

        public static void DrawCapsule(Vector3 center, float radius, float height)
        {
            Matrix *= Matrix4x4.CreateTranslation(center);
            Add(new CapsuleGizmo(radius, height, Color));
        }

        public static void Add(Gizmo gizmo)
        {
            gizmoDrawQueue.Add((gizmo, Matrix));
            Matrix = Matrix4x4.Identity;
        }


        private static void EnsureMaterial()
        {
            if (gizmoMaterial != null)
                return;

            const string vertex = @"
#version 450

layout (location = 0) in vec3 vertexPosition;

layout(set = 0, binding = 0) uniform MVPBuffer
{
    mat4 MVPMatrix;
};

void main()
{
    vec4 clipPos = MVPMatrix * vec4(vertexPosition, 1.0);
    gl_Position = clipPos;
}
            ";

            const string fragment = @"
#version 450

layout (location = 0) out vec4 finalColor;

layout(set = 1, binding = 0) uniform ColorBuffer
{
    vec4 Color;
};

void main()
{
	finalColor = Color;
}
            ";

            // Pass creation info (Name, tags)
            Pass pass = new Pass("DrawGizmos", []);

            pass.CreateProgram(Graphics.CreateFromSpirv(vertex, fragment));

            // The input channels the vertex shader expects
            pass.AddVertexInput(MeshResource.Position);

            // MVP matrix resources
            pass.AddResourceElement([ 
                new ShaderResource("MVPMatrix", ResourceType.Matrix4x4, ShaderStages.Vertex)
            ]);
            
            // Other shader resources
            pass.AddResourceElement([
                new ShaderResource("Color", ResourceType.Vector4, ShaderStages.Fragment) 
            ]);

            pass.cullMode = FaceCullMode.None;

            var shader = new Shader("Gizmos/GizmoShader", pass);

            gizmoMaterial = new Material(shader);
        }

        public static void Render()
        {
            EnsureMaterial();  

            Graphics.SetPass(gizmoMaterial, 0, PolygonFillMode.Solid, PrimitiveTopology.LineList);

            foreach (var gizmo in gizmoDrawQueue)
                gizmo.Item1.Render(gizmoMaterial, gizmo.Item2);

            Clear();
        }

        public static void Clear()
        {
            gizmoDrawQueue.Clear();
        }
    }

    public interface Gizmo
    {
        void Render(Material mat, Matrix4x4 worldMatrix);
    }

    public class LineGizmo(Vector3 start, Vector3 end, Color color) : Gizmo
    {
        private static Mesh lineMesh = new Mesh()
        {
            MeshTopology = PrimitiveTopology.LineList,
            Vertices = [ new Vector3(0, 0, 0), new Vector3(0, 0, 1) ],
            Indices = [ 0, 1 ],
        };

        public void Render(Material mat, Matrix4x4 m)
        {
            lineMesh.Vertices = [ start, end ];

            mat.SetColor("Color", color);

            Graphics.DrawMesh(lineMesh, mat, m);
        }
    }

    public class ArcGizmo(float startAngle, float degrees, Color color) : Gizmo
    {
        private static Mesh GenArcMesh(float degrees)
        {
            Mesh mesh = new Mesh();

            const int numSegments = 24;

            var verts = new System.Numerics.Vector3[numSegments * 2];
            var indices = new uint[numSegments * 2];

            for (int i = 0; i < numSegments; i++)
            {
                int index = i * 2;

                float angle = (float)i / numSegments * degrees * (float)MathD.Deg2Rad;
                float angle2 = (float)(i + 1) / numSegments * degrees * (float)MathD.Deg2Rad;

                Vector3 point1 = new Vector3(MathF.Cos(angle), 0f, MathF.Sin(angle));
                Vector3 point2 = new Vector3(MathF.Cos(angle2), 0f, MathF.Sin(angle2));

                verts[index] = point1;
                verts[index + 1] = point2; 

                indices[index] = (uint)index;
                indices[index + 1] = (uint)index + 1;
            }

            mesh.Vertices = verts;
            mesh.Indices = indices;
            mesh.MeshTopology = PrimitiveTopology.LineList;

            return mesh;
        }

        private Mesh arcMesh = GenArcMesh(degrees);

        public void Render(Material mat, Matrix4x4 m)
        {
            mat.SetColor("Color", color);

            m = Matrix4x4.CreateRotationY(startAngle) * m;

            Graphics.DrawMesh(arcMesh, mat, m);
        }
    }

    public class SphereGizmo(Color color) : Gizmo
    {
        public void Render(Material mat, Matrix4x4 m)
        {
            ArcGizmo arc = new ArcGizmo(0.0f, 360.0f, color);
            arc.Render(mat, m);

            m = Matrix4x4.CreateRotationX(MathF.PI / 2f) * m;
            arc.Render(mat, m);

            m = Matrix4x4.CreateRotationZ(MathF.PI / 2f) * m;
            arc.Render(mat, m);
        }
    }

    public class CubeGizmo(Color color) : Gizmo
    {
        private static Mesh cubeMesh = new Mesh()
        {
            Vertices = [ 
                // Bottom verts
                new Vector3(-0.5, -0.5, -0.5),
                new Vector3(0.5, -0.5, -0.5), 
                new Vector3(0.5, -0.5, 0.5),
                new Vector3(-0.5, -0.5, 0.5), 

                // Top verts
                new Vector3(-0.5, 0.5, -0.5), 
                new Vector3(0.5, 0.5, -0.5), 
                new Vector3(0.5, 0.5, 0.5),
                new Vector3(-0.5, 0.5, 0.5), 
            ],

            Indices = [
                0, 1, 1, 2, 2, 3, 3, 0, // Bottom 
                4, 5, 5, 6, 6, 7, 7, 4, // Top
                0, 4, 1, 5, 2, 6, 3, 7 // Connecting segments
            ],

            MeshTopology = PrimitiveTopology.LineList
        };

        public void Render(Material mat, Matrix4x4 m)
        {
            mat.SetColor("Color", color);

            Graphics.DrawMesh(cubeMesh, mat, m);
        }
    }

    public class CylinderGizmo(Color color) : Gizmo
    {
        private static Mesh cylinderMesh = new Mesh()
        {
            Vertices = [ 
                new Vector3(0.0, -0.5, -1.0),
                new Vector3(0.0, 0.5, -1.0), 

                new Vector3(0.0, -0.5, 1.0),
                new Vector3(0.0, 0.5, 1.0), 

                new Vector3(-1.0, -0.5, 0.0),
                new Vector3(-1.0, 0.5, 0.0), 

                new Vector3(1.0, -0.5, 0.0),
                new Vector3(1.0, 0.5, 0.0),
            ],

            Indices = [
                0, 1, 2, 3, 4, 5, 6, 7 // Connecting segments
            ],

            MeshTopology = PrimitiveTopology.LineList
        };

        public void Render(Material mat, Matrix4x4 m)
        {
            ArcGizmo arc = new ArcGizmo(0.0f, 360.0f, color);

            arc.Render(mat, Matrix4x4.CreateTranslation(new Vector3(0, 0.5, 0)) * m);
            arc.Render(mat, Matrix4x4.CreateTranslation(new Vector3(0, -0.5, 0)) * m);

            mat.SetColor("Color", color);
            Graphics.DrawMesh(cylinderMesh, mat, m);
        }
    }

    public class CapsuleGizmo(float radius, float height, Color color) : Gizmo
    {
        public void Render(Material mat, Matrix4x4 m)
        {
            CylinderGizmo cylinder = new CylinderGizmo(color);
            
            cylinder.Render(mat, Matrix4x4.CreateScale(new Vector3(radius, height, radius)) * m);

            ArcGizmo arc = new ArcGizmo(0.0f, 180.0f, color);

            Quaternion rotX90 = Quaternion.AngleAxis(90 * MathD.Deg2Rad, Vector3.right);
            Quaternion rotXN90 = Quaternion.AngleAxis(-90 * MathD.Deg2Rad, Vector3.right);
            Quaternion rotY90 = Quaternion.AngleAxis(90 * MathD.Deg2Rad, Vector3.up);

            Vector3 size = new Vector3(radius, height, radius);
            Vector3 top = new Vector3(0, height * 0.5, 0);

            arc.Render(mat, Matrix4x4.TRS(top, rotXN90, size) * m);
            arc.Render(mat, Matrix4x4.TRS(top, rotY90 * rotXN90, size) * m);

            arc.Render(mat, Matrix4x4.TRS(-top, rotX90, size) * m);
            arc.Render(mat, Matrix4x4.TRS(-top, rotY90 * rotX90, size) * m);
        }
    }
}
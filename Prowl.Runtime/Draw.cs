using Prowl.Runtime.Components;
using System.Collections.Generic;
using System.Numerics;

namespace Prowl.Runtime
{
    public static class Draw
    {
        private readonly static List<float> lineVertices = new();
        private readonly static List<float> lineColors = new();

        private static Resources.Material mat;

        public static void Line(Vector3 pointA, Vector3 pointB, Color color)
        {
            lineVertices.Capacity += 6;
            lineVertices.Add(pointA.X);
            lineVertices.Add(pointA.Y);
            lineVertices.Add(pointA.Z);

            lineVertices.Add(pointB.X);
            lineVertices.Add(pointB.Y);
            lineVertices.Add(pointB.Z);

            lineColors.Capacity += 3;
            lineColors.Add(color.r);
            lineColors.Add(color.g);
            lineColors.Add(color.b);
        }

        public static void Render(Camera cam)
        {
            //if (lineVertices.Count == 0) return;
            //
            //mat ??= new Resources.Material(Resources.Shader.Find("Defaults/Gizmos.shader"));
            //
            //unsafe
            //{
            //    var vaoId = Rlgl.rlLoadVertexArray();
            //    Rlgl.rlEnableVertexArray(vaoId);
            //
            //    var vertices = lineVertices.ToArray();
            //    var colors = lineColors.ToArray();
            //
            //    int RL_FLOAT = 0x1406;
            //
            //    uint posVBO;
            //    uint colVBO;
            //
            //    // Enable vertex attributes: position (shader-location = 0)
            //    fixed (float* vptr = vertices)
            //        posVBO = Rlgl.rlLoadVertexBuffer(vptr, vertices.Length * sizeof(float), false);
            //    Rlgl.rlSetVertexAttribute(0, 3, RL_FLOAT, 0, 0, null);
            //    Rlgl.rlEnableVertexAttribute(0);
            //
            //    // Enable vertex attribute: color (shader-location = 1)
            //    fixed (float* cptr = colors)
            //        colVBO = Rlgl.rlLoadVertexBuffer(cptr, colors.Length * sizeof(float), false);
            //    Rlgl.rlSetVertexAttribute(1, 3, RL_FLOAT, 0, 0, null);
            //    Rlgl.rlEnableVertexAttribute(1);
            //
            //    Rlgl.rlEnableWireMode();
            //    mat.SetPass(0);
            //    Rlgl.rlDrawVertexArray(0, vertices.Length / 3);
            //    mat.EndPass();
            //    Rlgl.rlDisableWireMode();
            //
            //    // Unload rlgl vboId data
            //    Rlgl.rlUnloadVertexArray(vaoId);
            //    Rlgl.rlUnloadVertexBuffer(posVBO);
            //    Rlgl.rlUnloadVertexBuffer(colVBO);
            //}
        }

        public static void Clear()
        {
            lineVertices.Clear();
            lineColors.Clear();
        }
    }
}

// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Jitter2;
using Jitter2.LinearMath;

using Prowl.Vector;

namespace Prowl.Runtime;

public class JitterGizmosDrawer : IDebugDrawer
{
    static JitterGizmosDrawer m_Instance;
    public static JitterGizmosDrawer Instance => m_Instance ??= new();
    public Color color { get; set; } = new Color(0, 255, 0, 128);

    public void DrawCube(in JVector p, in JQuaternion ori, in JVector size)
    {
        Double3 center = new Double3(p.X, p.Y, p.Z);
        Quaternion rotation = new Quaternion((float)ori.X, (float)ori.Y, (float)ori.Z, (float)ori.W);
        Double3 extents = new Double3(size.X * 0.501f, size.Y * 0.501f, size.Z * 0.501f);

        Debug.PushMatrix(Double4x4.CreateTRS(center, rotation, Double3.One));
        Debug.DrawCube(center, extents, color);
        Debug.PopMatrix();
    }

    public void DrawPoint(in JVector p)
    {
        Double3 center = new Double3(p.X, p.Y, p.Z);
        Debug.DrawSphere(center, 0.1f, color, 8);
    }

    public void DrawSegment(in JVector pA, in JVector pB)
    {
        Double3 a = new Double3(pA.X, pA.Y, pA.Z);
        Double3 b = new Double3(pB.X, pB.Y, pB.Z);
        Debug.DrawLine(a, b, color);
    }

    public void DrawSphere(in JVector p, in JQuaternion ori, float radius)
    {
        Double3 center = new Double3(p.X, p.Y, p.Z);
        Quaternion rotation = new Quaternion((float)ori.X, (float)ori.Y, (float)ori.Z, (float)ori.W);
        Debug.DrawWireSphere(center, radius, color);
    }

    public void DrawTriangle(in JVector pA, in JVector pB, in JVector pC)
    {
        Double3 a = new Double3(pA.X, pA.Y, pA.Z);
        Double3 b = new Double3(pB.X, pB.Y, pB.Z);
        Double3 c = new Double3(pC.X, pC.Y, pC.Z);
        //Debug.DrawTriangle(a, b, c, color);
        Debug.DrawLine(a, b, color);
        Debug.DrawLine(b, c, color);
        Debug.DrawLine(c, a, color);
    }
}

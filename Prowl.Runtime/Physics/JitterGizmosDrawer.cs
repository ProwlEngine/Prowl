// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Jitter2;
using Jitter2.LinearMath;

namespace Prowl.Runtime;

public class JitterGizmosDrawer : IDebugDrawer
{
    static JitterGizmosDrawer m_Instance;
    public static JitterGizmosDrawer Instance => m_Instance ??= new();
    public Color color { get; set; } = new Color(0, 255, 0, 128);

    public void DrawCube(in JVector p, in JQuaternion ori, in JVector size)
    {
        Vector3 center = new Vector3(p.X, p.Y, p.Z);
        Quaternion rotation = new Quaternion(ori.X, ori.Y, ori.Z, ori.W);
        Vector3 extents = new Vector3(size.X * 0.501f, size.Y * 0.501f, size.Z * 0.501f);

        Debug.PushMatrix(Matrix4x4.TRS(center, rotation, Vector3.one));
        Debug.DrawCube(center, extents, color);
        Debug.PopMatrix();
    }

    public void DrawPoint(in JVector p)
    {
        Vector3 center = new Vector3(p.X, p.Y, p.Z);
        Debug.DrawSphere(center, 0.1f, color, 8);
    }

    public void DrawSegment(in JVector pA, in JVector pB)
    {
        Vector3 a = new Vector3(pA.X, pA.Y, pA.Z);
        Vector3 b = new Vector3(pB.X, pB.Y, pB.Z);
        Debug.DrawLine(a, b, color);
    }

    public void DrawSphere(in JVector p, in JQuaternion ori, float radius)
    {
        Vector3 center = new Vector3(p.X, p.Y, p.Z);
        Quaternion rotation = new Quaternion(ori.X, ori.Y, ori.Z, ori.W);
        Debug.DrawWireSphere(center, radius, color);
    }

    public void DrawTriangle(in JVector pA, in JVector pB, in JVector pC)
    {
        Vector3 a = new Vector3(pA.X, pA.Y, pA.Z);
        Vector3 b = new Vector3(pB.X, pB.Y, pB.Z);
        Vector3 c = new Vector3(pC.X, pC.Y, pC.Z);
        //Debug.DrawTriangle(a, b, c, color);
        Debug.DrawLine(a, b, color);
        Debug.DrawLine(b, c, color);
        Debug.DrawLine(c, a, color);
    }
}

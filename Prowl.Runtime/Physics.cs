// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Jitter2;
using Jitter2.LinearMath;

using Prowl.Runtime.SceneManagement;
using Prowl.Runtime.Utils;

namespace Prowl.Runtime;


[FilePath("PhysicsSettings.projsetting", FilePathAttribute.Location.Setting)]
public class PhysicsSetting : ScriptableSingleton<PhysicsSetting>
{
    public Vector3 Gravity = new Vector3(0, -9.81f, 0);
    [Range(1, 16)]
    public int SolverIterations = 6;
    [Range(0, 16)]
    public int RelaxIterations = 4;
    [Range(1, 16)]
    public int Substep = 1;
    [Range(5, 120)]
    public int TargetFrameRate = 50;
    public bool AllowSleep = true;
    public bool UseMultithreading = true;
    public bool AutoSyncTransforms = true;

}

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

public static class Physics
{

    static World _world;
    public static World World => _world ??= new();

    private static double timer = 0;

    [OnAssemblyLoad, OnAssemblyUnload, OnSceneLoad, OnSceneUnload, OnPlaymodeChanged]
    public static void Clear()
    {
        _world?.Clear();
    }

    public static void Update()
    {
        timer += Time.deltaTime;
        int count = 0;
        while (timer >= Time.fixedDeltaTime && count++ < 10)
        {
            SceneManager.PhysicsUpdate();

            _world.SolverIterations = (PhysicsSetting.Instance.SolverIterations, PhysicsSetting.Instance.RelaxIterations);
            _world.SubstepCount = PhysicsSetting.Instance.Substep;
            _world.AllowDeactivation = PhysicsSetting.Instance.AllowSleep;

            _world.Gravity = new JVector(PhysicsSetting.Instance.Gravity.x, PhysicsSetting.Instance.Gravity.y, PhysicsSetting.Instance.Gravity.z);

            _world.Step(Time.fixedDeltaTime, PhysicsSetting.Instance.UseMultithreading);

            timer -= Time.fixedDeltaTime;
        }
    }


}

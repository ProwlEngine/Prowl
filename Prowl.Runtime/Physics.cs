// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Jitter2;
using Jitter2.Collision;
using Jitter2.Collision.Shapes;
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

    public Boolean32Matrix s_collisionMatrix = new(true);
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

public class LayerFilter : IBroadPhaseFilter
{
    public bool Filter(IDynamicTreeProxy proxyA, IDynamicTreeProxy proxyB)
    {
        if (proxyA is RigidBodyShape rbsA && proxyB is RigidBodyShape rbsB)
        {
            if (rbsA.RigidBody.Tag is not Rigidbody3D.RigidBodyUserData udA ||
                rbsB.RigidBody.Tag is not Rigidbody3D.RigidBodyUserData udB)
                return true;

            return Physics.GetLayerCollision(udA.Layer, udB.Layer);
        }

        return false;
    }
}

public static class Physics
{

    static World _world;
    public static World World => _world ??= new()
    {
        BroadPhaseFilter = new LayerFilter()
    };

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

    /// <summary>
    /// Sets the collision matrix for two layers
    /// </summary>
    public static void SetLayerCollision(int layer1Index, int layer2Index, bool shouldCollide)
    {
        PhysicsSetting.Instance.s_collisionMatrix.SetSymmetric(layer1Index, layer2Index, shouldCollide);
    }

    /// <summary>
    /// Makes sure the collision matrix is symmetric (if [a,b] collides, [b,a] should too)
    /// </summary>
    public static void EnsureSymmetric()
    {
        PhysicsSetting.Instance.s_collisionMatrix.MakeSymmetric();
    }

    /// <summary>
    /// Sets all collisions for a specific layer
    /// </summary>
    public static void SetLayerCollisions(int layer, bool shouldCollide)
    {
        PhysicsSetting.Instance.s_collisionMatrix.SetRow(layer, shouldCollide);
        // Make sure to maintain symmetry
        PhysicsSetting.Instance.s_collisionMatrix.SetColumn(layer, shouldCollide);
    }

    /// <summary>
    /// Gets weather two layers should collide
    /// </summary>
    public static bool GetLayerCollision(int layer1, int layer2)
    {
        return PhysicsSetting.Instance.s_collisionMatrix[layer1, layer2];
    }

    /// <summary>
    /// Gets all collisions for a specific layer
    /// </summary>
    public static bool[] GetLayerCollisions(int layer)
    {
        return PhysicsSetting.Instance.s_collisionMatrix.GetRow(layer);
    }

    /// <summary>
    /// Sets all layers to collide or not collide
    /// </summary>
    public static void SetAllCollisions(bool shouldCollide)
    {
        PhysicsSetting.Instance.s_collisionMatrix.SetAll(shouldCollide);
    }
}

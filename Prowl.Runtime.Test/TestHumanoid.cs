// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Generic;

using Prowl.Motion;
using Prowl.Runtime.Resources;
using Prowl.Vector;
using Prowl.Vector.Spatial;

using Xunit;

using MotionSkeleton = Prowl.Motion.Skeleton;

namespace Prowl.Runtime.Test;

/// <summary>A small standing humanoid rig and the hierarchy it binds to, shared by the ragdoll tests.</summary>
internal static class TestHumanoid
{
    // A standing humanoid rig and the hierarchy it binds to, built from one list of bones.
    public static (MotionSkeleton Skeleton, GameObject Root) Build()
    {
        var defs = new (string Name, string? Parent, Float3 Local)[]
        {
            ("Hips", null, new Float3(0f, 1f, 0f)),
            ("Spine", "Hips", new Float3(0f, 0.15f, 0f)),
            ("Chest", "Spine", new Float3(0f, 0.15f, 0f)),
            ("UpperChest", "Chest", new Float3(0f, 0.1f, 0f)),
            ("Neck", "UpperChest", new Float3(0f, 0.15f, 0f)),
            ("Head", "Neck", new Float3(0f, 0.1f, 0f)),
            ("LeftShoulder", "UpperChest", new Float3(-0.05f, 0.03f, 0f)),
            ("LeftUpperArm", "LeftShoulder", new Float3(-0.15f, 0.02f, 0f)),
            ("LeftLowerArm", "LeftUpperArm", new Float3(-0.3f, 0f, 0f)),
            ("LeftHand", "LeftLowerArm", new Float3(-0.25f, 0f, 0f)),
            ("RightShoulder", "UpperChest", new Float3(0.05f, 0.03f, 0f)),
            ("RightUpperArm", "RightShoulder", new Float3(0.15f, 0.02f, 0f)),
            ("RightLowerArm", "RightUpperArm", new Float3(0.3f, 0f, 0f)),
            ("RightHand", "RightLowerArm", new Float3(0.25f, 0f, 0f)),
            ("LeftUpperLeg", "Hips", new Float3(-0.1f, 0f, 0f)),
            ("LeftLowerLeg", "LeftUpperLeg", new Float3(0f, -0.45f, 0f)),
            ("LeftFoot", "LeftLowerLeg", new Float3(0f, -0.45f, 0f)),
            ("RightUpperLeg", "Hips", new Float3(0.1f, 0f, 0f)),
            ("RightLowerLeg", "RightUpperLeg", new Float3(0f, -0.45f, 0f)),
            ("RightFoot", "RightLowerLeg", new Float3(0f, -0.45f, 0f)),
        };

        var root = new GameObject("Character");
        var objects = new Dictionary<string, GameObject>();
        var ids = new StringID[defs.Length];
        var parents = new int[defs.Length];
        var rest = new Transform3D[defs.Length];
        for (int i = 0; i < defs.Length; i++)
        {
            (string name, string? parent, Float3 local) = defs[i];
            ids[i] = new StringID(name);
            parents[i] = parent == null ? MotionSkeleton.InvalidIndex : Array.FindIndex(defs, d => d.Name == parent);
            rest[i] = new Transform3D(local, Quaternion.Identity, Float3.One);

            var go = new GameObject(name);
            go.SetParent(parent == null ? root : objects[parent], false);
            go.Transform.LocalPosition = local;
            objects[name] = go;
        }
        return (new MotionSkeleton(ids, parents, rest), root);
    }

    /// <summary>The hierarchy's humanoid bones, by the names the rig gives them.</summary>
    public static Dictionary<HumanBodyBone, Transform> Bones(GameObject root)
    {
        var bones = new Dictionary<HumanBodyBone, Transform>();
        void Collect(GameObject go)
        {
            if (Enum.TryParse(go.Name, out HumanBodyBone bone)) bones[bone] = go.Transform;
            foreach (GameObject child in go.Children) Collect(child);
        }
        Collect(root);
        return bones;
    }

    public static Transform Hips(GameObject root) => root.Children.First(g => g.Name == "Hips").Transform;

    /// <summary>Turns a bone of a pose from its rest, and moves it by <paramref name="shift"/>.</summary>
    public static void Turn(Pose pose, MotionSkeleton skeleton, string bone, Quaternion rotation, Float3 shift = default)
    {
        int index = skeleton.GetBoneIndex(new StringID(bone));
        Transform3D rest = skeleton.GetBoneParentSpaceTransform(index);
        pose.SetTransform(index, new Transform3D(rest.position + shift, rotation, rest.scale));
    }

    /// <summary>A turn of <paramref name="degrees"/> about an axis.</summary>
    public static Quaternion About(float x, float y, float z, float degrees) => Quaternion.AxisAngle(new Float3(x, y, z), degrees * Maths.Deg2Rad);
}

// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Echo;
using Prowl.Vector;

namespace Prowl.Runtime;

/// <summary>
/// Represents a skeletal structure with bones, hierarchy, and bind poses.
/// This class handles all the heavy lifting for bone IDs, transforms, and hierarchy.
/// </summary>
public sealed class Skeleton : EngineObject, ISerializable
{
    public List<Bone> Bones { get; private set; } = [];

    private Dictionary<string, int> _boneNameToIndex = [];
    private Dictionary<string, Bone> _boneNameToBone = [];

    /// <summary>
    /// Represents a single bone in the skeleton
    /// </summary>
    public class Bone
    {
        public int ID { get; set; }
        public string Name { get; set; }
        public int ParentID { get; set; } = -1; // -1 means root bone

        // Bind pose (default/rest pose) - this is the local transform
        public Float3 BindPosition { get; set; }
        public Quaternion BindRotation { get; set; }
        public Float3 BindScale { get; set; }

        // Offset matrix (inverse bind pose in world space)
        // This is used for skinning to transform from bind pose to bone space
        public Float4x4 OffsetMatrix { get; set; }

        // Mesh references - bones can have attached meshes
        public int? MeshIndex { get; set; }
        public List<int> MeshIndices { get; set; } = [];

        public Bone(int id, string name)
        {
            ID = id;
            Name = name;
            BindPosition = Float3.Zero;
            BindRotation = Quaternion.Identity;
            BindScale = Float3.One;
            OffsetMatrix = Float4x4.Identity;
        }
    }

    public Skeleton()
    {
    }

    /// <summary>
    /// Adds a bone to the skeleton
    /// </summary>
    public void AddBone(Bone bone)
    {
        Bones.Add(bone);
        _boneNameToIndex[bone.Name] = bone.ID;
        _boneNameToBone[bone.Name] = bone;
    }

    /// <summary>
    /// Gets a bone by name
    /// </summary>
    public Bone? GetBone(string name)
    {
        if (_boneNameToBone.TryGetValue(name, out Bone? bone))
            return bone;
        return null;
    }

    /// <summary>
    /// Gets a bone by ID
    /// </summary>
    public Bone? GetBone(int id)
    {
        if (id >= 0 && id < Bones.Count)
            return Bones[id];
        return null;
    }

    /// <summary>
    /// Gets the bone index by name
    /// </summary>
    public int GetBoneIndex(string name)
    {
        if (_boneNameToIndex.TryGetValue(name, out int index))
            return index;
        return -1;
    }

    /// <summary>
    /// Gets the parent bone of a given bone
    /// </summary>
    public Bone? GetParent(Bone bone)
    {
        if (bone.ParentID >= 0 && bone.ParentID < Bones.Count)
            return Bones[bone.ParentID];
        return null;
    }

    /// <summary>
    /// Calculates world transforms for all bones from local transforms
    /// </summary>
    public void CalculateWorldTransforms(Float3[] localPositions, Quaternion[] localRotations, Float3[] localScales,
        out Float4x4[] worldTransforms)
    {
        if (localPositions.Length != Bones.Count || localRotations.Length != Bones.Count || localScales.Length != Bones.Count)
            throw new ArgumentException("Transform arrays must match bone count");

        worldTransforms = new Float4x4[Bones.Count];

        // Calculate world transforms for each bone
        for (int i = 0; i < Bones.Count; i++)
        {
            Bone bone = Bones[i];
            Float4x4 localMatrix = Float4x4.CreateTRS(localPositions[i], localRotations[i], localScales[i]);

            if (bone.ParentID >= 0)
            {
                // Child bone: multiply by parent's world transform
                worldTransforms[i] = worldTransforms[bone.ParentID] * localMatrix;
            }
            else
            {
                // Root bone: world transform equals local transform
                worldTransforms[i] = localMatrix;
            }
        }
    }

    /// <summary>
    /// Calculates final bone matrices for GPU skinning using mesh-specific offset matrices
    /// </summary>
    /// <param name="worldTransforms">World transforms for each bone</param>
    /// <param name="boneNames">Bone names from the mesh</param>
    /// <param name="meshOffsetMatrices">Offset matrices specific to this mesh</param>
    public Float4x4[] CalculateSkinningMatrices(Float4x4[] worldTransforms, string[] boneNames, Float4x4[] meshOffsetMatrices)
    {
        if (boneNames.Length != meshOffsetMatrices.Length)
            throw new ArgumentException("Bone names and offset matrices must have the same length");

        Float4x4[] skinningMatrices = new Float4x4[boneNames.Length];

        bool logged = false;
        for (int i = 0; i < boneNames.Length; i++)
        {
            // Find the bone index in the skeleton
            int boneIndex = GetBoneIndex(boneNames[i]);
            if (boneIndex < 0 || boneIndex >= worldTransforms.Length)
            {
                if (!logged)
                {
                    Debug.LogWarning($"[Skinning] Bone '{boneNames[i]}' (joint {i}) not found in skeleton! Using identity. Total joints={boneNames.Length}, skeleton bones={Bones.Count}");
                    logged = true;
                }
                skinningMatrices[i] = Float4x4.Identity;
                continue;
            }

            // Final matrix = world transform * mesh-specific offset matrix
            skinningMatrices[i] = worldTransforms[boneIndex] * meshOffsetMatrices[i];

            // Debug: check if the resulting matrix is near-identity (expected in bind pose)
            // or degenerate (translation component is huge or NaN)
            var t = skinningMatrices[i].Translation;
            if (!logged && (float.IsNaN(t.X) || float.IsNaN(t.Y) || float.IsNaN(t.Z) ||
                MathF.Abs(t.X) > 10000 || MathF.Abs(t.Y) > 10000 || MathF.Abs(t.Z) > 10000))
            {
                Debug.LogWarning($"[Skinning] Bone '{boneNames[i]}' (joint {i}, skeletonIdx {boneIndex}) has degenerate skinning matrix! Translation=({t.X},{t.Y},{t.Z})");
                logged = true;
            }
        }

        return skinningMatrices;
    }

    public void Serialize(ref EchoObject value, SerializationContext ctx)
    {
        value.Add("Name", new EchoObject(Name));

        var boneList = EchoObject.NewList();
        foreach (Bone bone in Bones)
        {
            var boneProp = EchoObject.NewCompound();
            boneProp.Add("ID", new EchoObject(bone.ID));
            boneProp.Add("Name", new EchoObject(bone.Name));
            boneProp.Add("ParentID", new EchoObject(bone.ParentID));

            boneProp.Add("BindPosition", Serializer.Serialize(bone.BindPosition, ctx));
            boneProp.Add("BindRotation", Serializer.Serialize(bone.BindRotation, ctx));
            boneProp.Add("BindScale", Serializer.Serialize(bone.BindScale, ctx));
            boneProp.Add("OffsetMatrix", Serializer.Serialize(bone.OffsetMatrix, ctx));

            if (bone.MeshIndex.HasValue)
                boneProp.Add("MeshIndex", new EchoObject(bone.MeshIndex.Value));

            if (bone.MeshIndices.Count > 0)
            {
                var meshList = EchoObject.NewList();
                foreach (int meshIdx in bone.MeshIndices)
                    meshList.ListAdd(new EchoObject(meshIdx));
                boneProp.Add("MeshIndices", meshList);
            }

            boneList.ListAdd(boneProp);
        }
        value.Add("Bones", boneList);
    }

    public void Deserialize(EchoObject value, SerializationContext ctx)
    {
        Name = value.Get("Name").StringValue;

        EchoObject? boneList = value.Get("Bones");
        foreach (EchoObject boneProp in boneList.List)
        {
            var bone = new Bone(
                boneProp.Get("ID").IntValue,
                boneProp.Get("Name").StringValue
            );

            bone.ParentID = boneProp.Get("ParentID").IntValue;
            bone.BindPosition = Serializer.Deserialize<Float3>(boneProp.Get("BindPosition"), ctx);
            bone.BindRotation = Serializer.Deserialize<Quaternion>(boneProp.Get("BindRotation"), ctx);
            bone.BindScale = Serializer.Deserialize<Float3>(boneProp.Get("BindScale"), ctx);
            bone.OffsetMatrix = Serializer.Deserialize<Float4x4>(boneProp.Get("OffsetMatrix"), ctx);

            // Load mesh references if present
            EchoObject? meshIndexObj = boneProp.Get("MeshIndex");
            if (meshIndexObj != null)
                bone.MeshIndex = meshIndexObj.IntValue;

            EchoObject? meshIndicesObj = boneProp.Get("MeshIndices");
            if (meshIndicesObj != null)
            {
                foreach (EchoObject meshIdxObj in meshIndicesObj.List)
                    bone.MeshIndices.Add(meshIdxObj.IntValue);
            }

            AddBone(bone);
        }
    }
}

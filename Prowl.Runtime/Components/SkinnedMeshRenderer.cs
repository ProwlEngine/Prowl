// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Echo;
using Prowl.Runtime.Rendering;
using Prowl.Runtime.Resources;
using Prowl.Vector;

namespace Prowl.Runtime;

/// <summary>
/// Renders a skinned mesh deformed by bone Transforms.
/// Bones are child GameObjects — their Transforms drive the skinning.
/// Supports multiple materials via submeshes.
/// </summary>
[AddComponentMenu("Rendering/Skinned Mesh Renderer")]
public class SkinnedMeshRenderer : MonoBehaviour
{
    /// <summary>The mesh to render (may contain submeshes).</summary>
    public AssetRef<Mesh> SharedMesh;

    /// <summary>Materials array — one per submesh. If fewer materials than submeshes, last material is reused.</summary>
    public List<AssetRef<Material>> Materials = new();

    /// <summary>
    /// The root bone Transform. Used as the reference frame for skinning.
    /// Typically the top of the skeleton hierarchy (e.g., "Hips" or "Armature").
    /// </summary>
    [SerializeField]
    private Guid _rootBoneId;

    /// <summary>
    /// All bone Transforms, indexed to match Mesh.BindPoses and Mesh.BoneNames.
    /// Set during import — each entry corresponds to one joint.
    /// </summary>
    [SerializeField]
    private Guid[] _boneIds;

    public Color MainColor = Color.White;

    // Resolved at runtime
    [System.NonSerialized] private Transform? _rootBone;
    [System.NonSerialized] private Transform?[]? _bones;
    [System.NonSerialized] private Float4x4[]? _skinMatrices;
    [System.NonSerialized] private bool _resolved;

    /// <summary>Get/set the root bone Transform.</summary>
    public Transform? RootBone
    {
        get { Resolve(); return _rootBone; }
        set { _rootBone = value; _rootBoneId = value?.GameObject?.Identifier ?? Guid.Empty; }
    }

    /// <summary>Get/set the bone Transforms array.</summary>
    public Transform?[]? Bones
    {
        get { Resolve(); return _bones; }
        set
        {
            _bones = value;
            if (value != null)
            {
                _boneIds = new Guid[value.Length];
                for (int i = 0; i < value.Length; i++)
                    _boneIds[i] = value[i]?.GameObject?.Identifier ?? Guid.Empty;
            }
            else _boneIds = null;
        }
    }

    private void Resolve()
    {
        if (_resolved) return;
        _resolved = true;

        // Resolve root bone by identifier
        if (_rootBoneId != Guid.Empty && GameObject?.Scene != null)
        {
            var rootGo = FindByIdentifier(_rootBoneId);
            _rootBone = rootGo?.Transform;
        }

        // Resolve bone array
        if (_boneIds != null)
        {
            _bones = new Transform?[_boneIds.Length];
            for (int i = 0; i < _boneIds.Length; i++)
            {
                if (_boneIds[i] != Guid.Empty)
                {
                    var boneGo = FindByIdentifier(_boneIds[i]);
                    _bones[i] = boneGo?.Transform;
                }
            }
        }
    }

    private GameObject? FindByIdentifier(Guid id)
    {
        // Search in the scene and in this GO's hierarchy
        var root = GameObject;
        while (root.Parent != null) root = root.Parent;
        return root.FindChildByIdentifier(id);
    }

    public override void OnEnable()
    {
        _resolved = false; // Force re-resolve on enable
    }

    public override void OnRenderCollect()
    {
        var mesh = SharedMesh.Res;
        if (mesh == null || Materials.Count == 0) return;

        Resolve();

        // Compute skinning matrices
        if (_bones != null && _bones.Length > 0 && mesh.BindPoses != null)
        {
            if (_skinMatrices == null || _skinMatrices.Length != _bones.Length)
                _skinMatrices = new Float4x4[_bones.Length];

            Float4x4 worldToLocal = Transform.WorldToLocalMatrix;
            for (int i = 0; i < _bones.Length; i++)
            {
                if (_bones[i] != null && i < mesh.BindPoses.Length)
                    _skinMatrices[i] = worldToLocal * _bones[i].LocalToWorldMatrix * mesh.BindPoses[i];
                else
                    _skinMatrices[i] = Float4x4.Identity;
            }
        }

        // Render each submesh with its material
        int subCount = mesh.SubMeshCount;
        for (int s = 0; s < subCount; s++)
        {
            Material? mat = null;
            if (s < Materials.Count)
                mat = Materials[s].Res;
            else if (Materials.Count > 0)
                mat = Materials[^1].Res; // Reuse last material for extra submeshes

            if (mat == null) continue;

            PropertyState props = new();
            props.SetInt("_ObjectID", InstanceID);
            props.SetColor("_MainColor", MainColor);
            if (_skinMatrices != null)
                props.SetMatrices("boneTransforms", _skinMatrices);

            GameObject.Scene.PushRenderable(new MeshRenderable(
                mesh, mat, Transform.LocalToWorldMatrix,
                GameObject.LayerIndex, props, subMeshIndex: s));
        }
    }
}

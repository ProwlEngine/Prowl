// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Prowl.Clay;
using Prowl.Clay.Importer;
using Prowl.Runtime.Resources;
using Prowl.Vector;

using ClayMesh = Prowl.Clay.Mesh;
using ClayMaterial = Prowl.Clay.Material;
using ClayAnim = Prowl.Clay.AnimationClip;
using ClayBinding = Prowl.Clay.AnimationBinding;
using ClayTexture = Prowl.Clay.Texture;
using ClaySettings = Prowl.Clay.Importer.ModelImporterSettings;
using PMesh = Prowl.Runtime.Resources.Mesh;
using PMaterial = Prowl.Runtime.Resources.Material;
using PAnim = Prowl.Runtime.AnimationClip;
using PBlendShape = Prowl.Runtime.Resources.BlendShape;
using PBlendShapeFrame = Prowl.Runtime.Resources.BlendShapeFrame;

namespace Prowl.Runtime.AssetImporting;

/// <summary>
/// Bakes a <see cref="Prowl.Clay.Model"/> into a Prowl <see cref="ModelImportResult"/>:
/// runtime <see cref="PMesh"/>es, <see cref="PMaterial"/>s wired to the Standard shader, a
/// <see cref="GameObject"/> hierarchy with MeshRenderer / SkinnedMeshRenderer components, and
/// <see cref="PAnim"/>s with per-axis curves.
/// </summary>
internal static class ClayBackedImporter
{
    public static ModelImportResult Import(FileInfo assetPath, ModelImporterSettings settings)
    {
        string ext = assetPath.Extension.ToLowerInvariant();
        Model clayModel = Clay.Importer.ModelImporter.Load(assetPath.FullName, MapSettings(settings, ext));
        return Bake(clayModel, Path.GetFileNameWithoutExtension(assetPath.Name), settings);
    }

    public static ModelImportResult Import(Stream stream, string virtualPath, ModelImporterSettings settings)
    {
        string ext = Path.GetExtension(virtualPath).ToLowerInvariant();
        string format = ext switch
        {
            ".gltf" => "gltf",
            ".glb" => "glb",
            ".vrm" => "vrm",
            ".obj" => "obj",
            ".fbx" => "fbx",
            _ => throw new NotSupportedException($"Unsupported model format: {ext}"),
        };
        Model clayModel = Clay.Importer.ModelImporter.Load(stream, format, MapSettings(settings, ext));
        return Bake(clayModel, Path.GetFileNameWithoutExtension(virtualPath), settings);
    }

    // ----------------------------------------------------------------------------------------
    // Settings translation
    // ----------------------------------------------------------------------------------------

    private static ClaySettings MapSettings(ModelImporterSettings s, string fileExt)
    {
        // Start from Clay's GameQuality preset (triangulate, dedup, tangents, bone-weight limit,
        // populate skeletons, bounds, RH->LH coord convert, sort by topology, etc.) then layer the
        // Prowl-specific toggles on top.
        PostProcessFlags flags = PostProcessPresets.GameQuality;

        // Normals come from Clay. Its steps split vertices along edges sharper than the smoothing
        // angle, which is what actually produces flat or hard-edged shading; generating them at the
        // runtime mesh layer instead can only write one normal per existing vertex.
        flags &= ~(PostProcessFlags.GenerateNormals | PostProcessFlags.GenerateSmoothNormals);
        if (s.GenerateNormals || s.RecalculateNormals)
            flags |= s.GenerateSmoothNormals ? PostProcessFlags.GenerateSmoothNormals : PostProcessFlags.GenerateNormals;

        // Tangents come from Clay too. Its step runs before JoinIdenticalVertices, which is the
        // order tangent generation wants, and it keeps tangents the source authored rather than
        // overwriting them the way the old mesh-layer pass did.
        if (s.CalculateTangentSpace) flags |= PostProcessFlags.CalcTangentSpace;
        else flags &= ~PostProcessFlags.CalcTangentSpace;

        // glTF stores UVs with V=0 at the top of the texture; FBX and OBJ store V=0 at the bottom,
        // which is what Prowl's shaders expect post-import because Texture2D uploads bottom-up. So
        // the flip is a property of the source format, not a preference: glTF always needs it and
        // FBX and OBJ never do. It used to be a toggle, which could only ever produce upside-down
        // textures on every glTF or do nothing at all.
        if (fileExt is ".gltf" or ".glb" or ".vrm") flags |= PostProcessFlags.FlipUVs;
        else flags &= ~PostProcessFlags.FlipUVs;

        if (s.OptimizeMeshes) flags |= PostProcessFlags.OptimizeMeshes;
        else flags &= ~PostProcessFlags.OptimizeMeshes;

        if (s.OptimizeHierarchy) flags |= PostProcessFlags.OptimizeGraph;
        else flags &= ~PostProcessFlags.OptimizeGraph;

        // No mesh splitting: a mesh that outgrows a 16-bit index buffer gets a 32-bit one, which
        // the bake already picks per mesh from its vertex count. Cutting a model into pieces to fit
        // an index width is a workaround for hardware Prowl does not target.
        flags &= ~PostProcessFlags.SplitLargeMeshes;

        // Strict validation needs the validator itself, which is not in the GameQuality preset.
        if (s.StrictValidation) flags |= PostProcessFlags.ValidateDataStructure;

        return new ClaySettings
        {
            PostProcess = flags,
            GlobalScale = s.UnitScale,
            BoneWeightLimit = 4,
            SmoothNormalsAngleDeg = s.SmoothNormalsAngleDeg,
            RecalculateNormals = s.RecalculateNormals,
            StrictValidation = s.StrictValidation,
            OptimizeGraphPreserveNodeNames = s.PreserveNodeNames,
            SceneIndex = s.SceneIndex,
        };
    }

    // ----------------------------------------------------------------------------------------
    // Bake: Clay model -> Prowl ModelImportResult
    // ----------------------------------------------------------------------------------------

    private static ModelImportResult Bake(Clay.Model clayModel, string modelName, ModelImporterSettings settings)
    {
        if (clayModel.Log.Entries.Count > 0)
        {
            foreach (ImportLogEntry entry in clayModel.Log.Entries)
            {
                if (entry.Severity == ImportLogSeverity.Warning)
                    Debug.LogWarning($"[Clay] {entry}");
                else if (entry.Severity == ImportLogSeverity.Error)
                    Debug.LogError($"[Clay] {entry}");
            }
        }

        // 1. Textures - resolve once, share by Clay texture index. Resolution (not necessarily
        // decoding - see IModelTextureResolver) happens through the resolver, so the editor can
        // supply one that only ever produces references to database textures and never touches pixel data itself.
        IModelTextureResolver resolver = settings.TextureResolver ?? DefaultModelTextureResolver.Instance;
        var textureCache = new Texture2D?[clayModel.Textures.Count];
        // Skipped wholesale when materials are off, so an import that wants no materials also does
        // not decode or register the textures only those materials would have referenced.
        if (settings.ImportMaterials)
        {
            bool[] colour = ColourTextures(clayModel);
            for (int i = 0; i < clayModel.Textures.Count; i++)
                textureCache[i] = ResolveModelTexture(clayModel.Textures[i], resolver, colour[i]);
        }

        // 2. Materials. Left empty when the import does not want them, which makes every renderer
        // fall back to the default material rather than to a material built from the file.
        // One ref per material the file defines. An extracted material is referenced where it lives, so
        // edits to it survive a reimport; everything else is built here and handed back as a sub asset.
        var materialRefs = new List<PMaterial?>(clayModel.Materials.Count);
        var materials = new List<PMaterial>();
        if (settings.ImportMaterials)
            for (int i = 0; i < clayModel.Materials.Count; i++)
            {
                PMaterial? extracted = settings.MaterialResolver is { } materialResolver ? materialResolver.Resolve(MaterialName(clayModel, i)) : null;
                if (extracted is not null) { materialRefs.Add(extracted); continue; }

                PMaterial built = BuildMaterial(clayModel.Materials[i], textureCache);
                built.Name = MaterialName(clayModel, i);
                materials.Add(built);
                materialRefs.Add(built);
            }

        // 3. Meshes (with per-submesh material index propagated).
        var meshes = new List<PMesh>(clayModel.Meshes.Count);
        var meshSubmeshMaterials = new List<int[]>(clayModel.Meshes.Count);
        for (int i = 0; i < clayModel.Meshes.Count; i++)
        {
            (PMesh pmesh, int[] submeshMatIndices) = BuildMesh(clayModel.Meshes[i], settings);
            meshes.Add(pmesh);
            meshSubmeshMaterials.Add(submeshMatIndices);
        }

        // 4. Build GameObject hierarchy. Index matches clayModel.Nodes.Index.
        var nodeGOs = new GameObject[clayModel.Nodes.Count];
        for (int i = 0; i < clayModel.Nodes.Count; i++)
        {
            ModelNode n = clayModel.Nodes[i];
            var go = new GameObject(string.IsNullOrEmpty(n.Name) ? $"Node_{i}" : n.Name);
            nodeGOs[i] = go;
            go.Transform.LocalPosition = n.LocalPosition;
            go.Transform.LocalRotation = n.LocalRotation;
            go.Transform.LocalScale = n.LocalScale;
        }
        // Parenting pass (after all GOs exist so SetParent can find the parent).
        for (int i = 0; i < clayModel.Nodes.Count; i++)
        {
            ModelNode? parent = clayModel.Nodes[i].Parent;
            if (parent is null) continue;
            nodeGOs[i].SetParent(nodeGOs[parent.Index], worldPositionStays: false);
        }
        // Rename the model root.
        nodeGOs[clayModel.Root.Index].Name = string.IsNullOrEmpty(modelName) ? "Model" : modelName;
        GameObject rootGO = nodeGOs[clayModel.Root.Index];

        // 5. Renderers + skin wiring.
        // Bind poses and bone names live on the Mesh, but a model may point one mesh at two
        // different skins. Writing both onto the shared mesh would leave whichever node came last
        // deciding the skinning for all of them, so a second skin gets its own copy of the mesh.
        int[] meshSkin = new int[clayModel.Meshes.Count];
        Array.Fill(meshSkin, UnclaimedMesh);

        for (int i = 0; i < clayModel.Nodes.Count; i++)
        {
            ModelNode n = clayModel.Nodes[i];
            if (n.MeshIndex < 0) continue;
            GameObject go = nodeGOs[i];
            PMesh mesh = meshes[n.MeshIndex];
            List<PMaterial> matRefs = BuildMatRefs(meshSubmeshMaterials[n.MeshIndex], materialRefs);

            if (n.SkinIndex >= 0)
            {
                if (meshSkin[n.MeshIndex] == UnclaimedMesh)
                {
                    meshSkin[n.MeshIndex] = n.SkinIndex;
                }
                else if (meshSkin[n.MeshIndex] != n.SkinIndex)
                {
                    (mesh, _) = BuildMesh(clayModel.Meshes[n.MeshIndex], settings);
                    mesh.Name = $"{mesh.Name}_Skin{n.SkinIndex}";
                    meshes.Add(mesh);
                }

                Skin clayskin = clayModel.Skins[n.SkinIndex];
                // Mirror Clay.Skin -> Prowl Mesh.BindPoses + Mesh.BoneNames (relative paths).
                mesh.BindPoses = [.. clayskin.InverseBindPoses];
                mesh.BoneNames = new string[clayskin.BoneNodeIndices.Length];
                var boneTransforms = new Transform[clayskin.BoneNodeIndices.Length];
                for (int b = 0; b < clayskin.BoneNodeIndices.Length; b++)
                {
                    int boneNodeIdx = clayskin.BoneNodeIndices[b];
                    boneTransforms[b] = nodeGOs[boneNodeIdx].Transform;
                    mesh.BoneNames[b] = Transform.GetRelativePath(boneTransforms[b], rootGO.Transform);
                }

                SkinnedMeshRenderer smr = go.AddComponent<SkinnedMeshRenderer>();
                smr.SharedMesh = mesh;
                smr.Materials = matRefs;
                Transform? rootBoneTransform = clayskin.RootNodeIndex >= 0
                    ? nodeGOs[clayskin.RootNodeIndex].Transform
                    : (boneTransforms.Length > 0 ? boneTransforms[0] : null);
                smr.SetBones(boneTransforms, rootBoneTransform);
            }
            else if (mesh.HasBlendShapes)
            {
                // Morph-only mesh (no skin): a SkinnedMeshRenderer still owns the blend-shape
                // weights. No bones to wire skinning stays disabled in-shader.
                SkinnedMeshRenderer smr = go.AddComponent<SkinnedMeshRenderer>();
                smr.SharedMesh = mesh;
                smr.Materials = matRefs;
            }
            else
            {
                MeshRenderer mr = go.AddComponent<MeshRenderer>();
                mr.Mesh = mesh;
                mr.Materials = matRefs;
            }
        }

        // 5b. Cameras, spot and directional lights aim down their node's -Z, so each goes on a child turned to face +Z.
        if (settings.ImportCameras)
            for (int i = 0; i < clayModel.Nodes.Count; i++)
                if (clayModel.Nodes[i].CameraIndex is int ci and >= 0)
                    BuildCamera(clayModel.Cameras[ci], ForwardChild(nodeGOs[i], "Camera"));

        if (settings.ImportLights)
            for (int i = 0; i < clayModel.Nodes.Count; i++)
                if (clayModel.Nodes[i].LightIndex is int li and >= 0)
                {
                    Clay.Light light = clayModel.Lights[li];
                    BuildLight(light, light.Type != Clay.LightType.Point ? ForwardChild(nodeGOs[i], "Light") : nodeGOs[i]);
                }

        // 6. The rig, then the clips that play on it.
        bool wantsRig = settings.RigType != ModelRigType.None
            && (clayModel.Skins.Count > 0 || (settings.ImportAnimations && clayModel.AnimationClips.Count > 0));

        Avatar? avatar = null;
        var animations = new List<PAnim>();
        if (wantsRig)
        {
            ModelRigBuilder.Rig rig = ModelRigBuilder.BuildRig(clayModel, nodeGOs, rootGO);
            avatar = BuildAvatar(rig, settings, modelName);
            if (settings.ImportAnimations)
                foreach (ClayAnim clip in clayModel.AnimationClips)
                    animations.Add(BuildClip(clip, clayModel, rig, avatar, settings));

            Animator animator = rootGO.AddComponent<Animator>();
            animator.Avatar = avatar;
            animator.Clips = [.. animations];
        }

        return new ModelImportResult
        {
            RootGO = rootGO,
            Meshes = meshes,
            Materials = materials,
            Animations = animations,
            Avatar = avatar,
        };
    }

    /// <summary>No node has claimed this mesh for a skin yet.</summary>
    private const int UnclaimedMesh = -1;

    /// <summary>The name a material is addressed by, which is what an extracted asset is keyed on.</summary>
    public static string MaterialName(Clay.Model model, int index)
    {
        string name = model.Materials[index].Name;
        return string.IsNullOrEmpty(name) ? $"Material_{index}" : name;
    }

    private static List<PMaterial> BuildMatRefs(int[] submeshMatIndices, List<PMaterial?> materialRefs)
    {
        var matRefs = new List<PMaterial>(submeshMatIndices.Length);
        for (int s = 0; s < submeshMatIndices.Length; s++)
        {
            int idx = submeshMatIndices[s];
            matRefs.Add(idx >= 0 && idx < materialRefs.Count ? materialRefs[idx]! : null!);
        }
        return matRefs;
    }

    // ----------------------------------------------------------------------------------------
    // Mesh bake
    // ----------------------------------------------------------------------------------------

    private static (PMesh mesh, int[] submeshMaterials) BuildMesh(ClayMesh src, ModelImporterSettings settings)
    {
        var dst = new PMesh
        {
            Name = src.Name,
            MeshTopology = Topology.Triangles,
            // IndexFormat must be assigned BEFORE Indices below: Prowl's IndexFormat setter wipes
            // the index buffer as a side effect, so setting it after Indices would leave us with
            // a mesh that fails Upload() with "Mesh has no indices".
            IndexFormat = src.Has32BitIndices ? IndexFormat.UInt32 : IndexFormat.UInt16,
        };

        if (src.VertexCount == 0)
            return (dst, Array.Empty<int>());

        dst.Vertices = src.Vertices;
        if (src.Normals is not null) dst.Normals = src.Normals;
        if (src.Tangents is not null) dst.Tangents = src.Tangents;
        if (src.Colors is not null) dst.Colors = src.Colors;
        if (src.UVs.Length > 0 && src.UVs[0] is not null) dst.UV = src.UVs[0];
        if (src.UVs.Length > 1 && src.UVs[1] is not null) dst.UV2 = src.UVs[1];

        // Clay's BoneWeight (struct with 4 indices + 4 weights) -> Prowl's parallel Float4 arrays.
        // Prowl's skinning shader uses 1-based bone indices with 0 reserved as "no bone": every
        // shader iteration checks "boneIndex > 0" before fetching boneMatrix[boneIndex - 1].
        // Shift Clay's 0-based glTF joint indices by +1, and zero out any slot whose weight is
        // 0 so the shader's no-bone branch fires (rather than uselessly fetching bone[0]).
        if (src.BoneWeights is not null)
        {
            var indices4 = new Float4[src.BoneWeights.Length];
            var weights4 = new Float4[src.BoneWeights.Length];
            for (int v = 0; v < src.BoneWeights.Length; v++)
            {
                BoneWeight bw = src.BoneWeights[v];
                indices4[v] = new Float4(
                    bw.Weight0 > 0f ? bw.Index0 + 1 : 0,
                    bw.Weight1 > 0f ? bw.Index1 + 1 : 0,
                    bw.Weight2 > 0f ? bw.Index2 + 1 : 0,
                    bw.Weight3 > 0f ? bw.Index3 + 1 : 0);
                weights4[v] = new Float4(bw.Weight0, bw.Weight1, bw.Weight2, bw.Weight3);
            }
            dst.BoneIndices = indices4;
            dst.BoneWeights = weights4;
        }

        // Blend shapes (morph targets). Clay already expands sparse deltas to full vertex count and
        // remaps them through vertex dedup, so the delta arrays line up 1:1 with dst.Vertices.
        if (settings.ImportBlendShapes && src.BlendShapes is { Length: > 0 })
        {
            var shapes = new PBlendShape[src.BlendShapes.Length];
            for (int i = 0; i < src.BlendShapes.Length; i++)
            {
                Clay.BlendShape cb = src.BlendShapes[i];
                var frames = new PBlendShapeFrame[cb.Frames.Length];
                for (int f = 0; f < cb.Frames.Length; f++)
                {
                    Clay.BlendShapeFrame cf = cb.Frames[f];
                    frames[f] = new PBlendShapeFrame
                    {
                        Weight = cf.Weight,
                        DeltaVertices = cf.DeltaVertices,
                        DeltaNormals = cf.DeltaNormals,
                        DeltaTangents = cf.DeltaTangents,
                    };
                }
                shapes[i] = new PBlendShape { Name = cb.Name ?? $"BlendShape{i}", Frames = frames };
            }
            dst.BlendShapes = shapes;
        }

        dst.Indices = src.Indices;

        int[] submeshMatIndices = new int[src.SubMeshes.Length];
        if (src.SubMeshes.Length > 1)
        {
            dst.SetSubMeshCount(src.SubMeshes.Length);
            for (int s = 0; s < src.SubMeshes.Length; s++)
            {
                SubMesh sm = src.SubMeshes[s];
                dst.SetSubMesh(s, new SubMeshDescriptor(sm.IndexStart, sm.IndexCount, MapTopology(sm.Topology)));
                submeshMatIndices[s] = sm.MaterialIndex;
            }
        }
        else if (src.SubMeshes.Length == 1)
        {
            submeshMatIndices[0] = src.SubMeshes[0].MaterialIndex;
        }

        dst.RecalculateBounds();
        return (dst, submeshMatIndices);
    }

    private static Topology MapTopology(PrimitiveTopology t) => t switch
    {
        PrimitiveTopology.Triangles => Topology.Triangles,
        PrimitiveTopology.Lines => Topology.Lines,
        PrimitiveTopology.Points => Topology.Points,
        _ => Topology.Triangles,
    };

    // ----------------------------------------------------------------------------------------
    // Camera / light bake
    // ----------------------------------------------------------------------------------------

    private static GameObject ForwardChild(GameObject node, string name)
    {
        var child = new GameObject(name);
        child.SetParent(node, worldPositionStays: false);
        child.Transform.LocalRotation = Quaternion.AxisAngle(Float3.UnitY, MathF.PI);
        return child;
    }

    /// <summary>Attaches a <see cref="Camera"/> matching the source lens.</summary>
    private static void BuildCamera(Clay.Camera src, GameObject go)
    {
        Camera cam = go.AddComponent<Camera>();

        if (src.Projection == CameraProjection.Orthographic)
        {
            cam.ProjectionMode = Camera.ProjectionType.Orthographic;
            cam.OrthographicSize = src.OrthographicHalfHeight;
        }
        else
        {
            cam.ProjectionMode = Camera.ProjectionType.Perspective;
            cam.FieldOfView = src.VerticalFovRadians * (180f / MathF.PI);
        }

        cam.NearClipPlane = src.NearPlane;
        // An absent far plane means an infinite projection, which Prowl's camera cannot express, so
        // it gets a far distance rather than a broken one.
        cam.FarClipPlane = src.FarPlane ?? 10000f;

        // An imported camera is a viewpoint the file described, not the one the game renders from.
        // Enabling it would fight whatever camera the scene already has.
        cam.Enabled = false;
    }

    /// <summary>
    /// Attaches a <see cref="Light"/> of the source's type.
    /// </summary>
    /// <remarks>
    /// glTF intensity is photometric (lux for directional, candela for point and spot) while Prowl's
    /// is an arbitrary scale, so the value is carried across as-is and will usually want adjusting.
    /// Converting it would need a scene-wide exposure convention Prowl does not have.
    /// </remarks>
    private static void BuildLight(Clay.Light src, GameObject go)
    {
        Light light = go.AddComponent<Light>();
        light.Type = src.Type switch
        {
            Clay.LightType.Directional => LightType.Directional,
            Clay.LightType.Spot => LightType.Spot,
            _ => LightType.Point,
        };
        light.Color = src.Color;
        light.Intensity = src.Intensity;

        // Range is optional in glTF and means unlimited when absent, which no real-time light can
        // do, so an absent one keeps the component's own default.
        if (src.Range is { } range) light.Range = range;

        // Both measure cone angles from the axis
        if (light.Type == LightType.Spot)
        {
            light.OuterAngle = src.OuterConeAngleRadians * (180f / MathF.PI);
            light.InnerAngle = src.InnerConeAngleRadians * (180f / MathF.PI);
        }
    }

    // ----------------------------------------------------------------------------------------
    // Material bake
    // ----------------------------------------------------------------------------------------

    /// <summary>The surface matching the source material's alpha mode and sidedness.</summary>
    private static SurfaceSettings SurfaceFor(ClayMaterial src)
    {
        SurfaceType type = src.AlphaMode switch
        {
            MaterialAlphaMode.Mask => SurfaceType.Cutout,
            MaterialAlphaMode.Blend => SurfaceType.Transparent,
            _ => SurfaceType.Opaque,
        };
        return new SurfaceSettings(type, SurfaceBlend.Alpha, src.DoubleSided ? SurfaceCull.Off : SurfaceCull.Back);
    }

    private static PMaterial BuildMaterial(ClayMaterial src, Texture2D?[] textureCache)
    {
        // Models also import on the loader thread, which never loads other assets, so built-ins are referenced.
        var mat = new PMaterial(Shader.GetDefault(src.Unlit ? DefaultShader.Unlit : DefaultShader.Lit))
        {
            Name = string.IsNullOrEmpty(src.Name) ? "Material" : src.Name,
            Surface = SurfaceFor(src),
        };

        // BaseColor is linear (glTF baseColorFactor) and multiplies the decoded albedo, so it crosses over untouched
        mat.SetColor("_MainColor", src.BaseColor);

        // Slots without a texture stay unset: the shader then leaves their input out
        SetTextureIfAny(mat, "_MainTex", ResolveTexture(src.BaseColorTexture, textureCache));
        mat.SetInt("_MainTexUV", UVSetFor(src.BaseColorTexture, src));

        ApplyTextureTransform(mat, src);

        if (src.AlphaMode == MaterialAlphaMode.Mask)
            mat.SetFloat("_AlphaCutoff", src.AlphaCutoff);

        if (src.Unlit)
            return mat;

        SetTextureIfAny(mat, "_NormalTex", ResolveTexture(src.NormalTexture, textureCache));
        mat.SetFloat("_NormalScale", src.NormalScale);
        mat.SetInt("_NormalTexUV", UVSetFor(src.NormalTexture, src));

        // Factors multiply the texture channels, matching glTF
        SetTextureIfAny(mat, "_SurfaceTex", ResolveTexture(src.MetallicRoughnessTexture, textureCache));
        mat.SetFloat("_Metallic", src.Metallic);
        mat.SetFloat("_Roughness", src.Roughness);
        mat.SetInt("_SurfaceTexUV", UVSetFor(src.MetallicRoughnessTexture, src));

        // When a model packs ORM into one image both occlusion and metallic roughness point at it
        SetTextureIfAny(mat, "_OcclusionTex", ResolveTexture(src.OcclusionTexture, textureCache));
        mat.SetFloat("_OcclusionStrength", src.OcclusionStrength);
        mat.SetInt("_OcclusionTexUV", UVSetFor(src.OcclusionTexture, src));

        Texture2D? emission = ResolveTexture(src.EmissiveTexture, textureCache);
        bool emits = src.EmissiveStrength > 0 && (src.EmissiveFactor.R > 0 || src.EmissiveFactor.G > 0 || src.EmissiveFactor.B > 0);
        if (emits)
        {
            mat.SetInt("_Emission", 1);
            SetTextureIfAny(mat, "_EmissionTex", emission);
            mat.SetColor("_EmissiveColor", src.EmissiveFactor);
            mat.SetFloat("_EmissionIntensity", src.EmissiveStrength);
            mat.SetInt("_EmissionTexUV", UVSetFor(src.EmissiveTexture, src));
        }

        return mat;
    }

    private static void SetTextureIfAny(PMaterial mat, string name, Texture2D? texture)
    {
        if (texture.IsValid()) mat.SetTexture(name, texture);
    }

    private static int UVSetFor(MaterialTextureSlot? slot, ClayMaterial src)
    {
        if (slot is null) return 0;
        if (slot.UVChannel is 0 or 1) return slot.UVChannel;

        Debug.LogWarning($"[Clay] Material '{src.Name}' samples a texture from UV channel {slot.UVChannel}; " +
            "a mesh carries UV0 and UV1 only, so UV0 was used instead.");
        return 0;
    }

    /// <summary>
    /// Carries KHR_texture_transform across. The Standard shaders apply one tiling/offset pair to
    /// every slot, so the base-colour slot wins and anything the shader cannot express is reported
    /// rather than dropped silently.
    /// </summary>
    private static void ApplyTextureTransform(PMaterial mat, ClayMaterial src)
    {
        MaterialTextureSlot? slot = src.BaseColorTexture;
        if (slot is not null)
        {
            mat.SetVector("_Tiling", slot.Scale);
            mat.SetVector("_Offset", slot.Offset);

            if (slot.Rotation != 0f)
                Debug.LogWarning($"[Clay] Material '{src.Name}' uses a rotated UV transform ({slot.Rotation:0.###} rad); " +
                    "the Standard shaders only support tiling and offset, so the rotation was dropped.");
        }

        foreach (MaterialTextureSlot? other in EnumerateSlots(src))
        {
            if (other is null || ReferenceEquals(other, slot)) continue;

            bool differs = slot is null
                ? other.Scale != Float2.One || other.Offset != Float2.Zero
                : other.Scale != slot.Scale || other.Offset != slot.Offset;

            if (differs)
            {
                Debug.LogWarning($"[Clay] Material '{src.Name}' uses per-slot UV transforms that differ from its base " +
                    "colour slot; the Standard shaders apply one transform to every slot, so the base colour slot won.");
            }
        }
    }

    private static IEnumerable<MaterialTextureSlot?> EnumerateSlots(ClayMaterial src)
    {
        yield return src.BaseColorTexture;
        yield return src.MetallicRoughnessTexture;
        yield return src.NormalTexture;
        yield return src.OcclusionTexture;
        yield return src.EmissiveTexture;
    }

    private static Texture2D? ResolveTexture(MaterialTextureSlot? slot, Texture2D?[] cache)
    {
        if (slot is null) return null;
        int idx = slot.TextureIndex;
        if ((uint)idx >= (uint)cache.Length) return null;
        return cache[idx];
    }

    // Textures some material samples as colour (base colour, emission); the rest are only read as data
    private static bool[] ColourTextures(Model model)
    {
        bool[] colour = new bool[model.Textures.Count];
        bool[] used = new bool[model.Textures.Count];
        foreach (ClayMaterial material in model.Materials)
        {
            foreach (MaterialTextureSlot? slot in EnumerateSlots(material))
                if (slot is not null && (uint)slot.TextureIndex < (uint)used.Length) used[slot.TextureIndex] = true;
            foreach (MaterialTextureSlot? slot in new[] { material.BaseColorTexture, material.EmissiveTexture })
                if (slot is not null && (uint)slot.TextureIndex < (uint)colour.Length) colour[slot.TextureIndex] = true;
        }
        for (int i = 0; i < colour.Length; i++)
            if (!used[i]) colour[i] = true;
        return colour;
    }

    private static Texture2D? ResolveModelTexture(ClayTexture src, IModelTextureResolver resolver, bool srgb)
    {
        try
        {
            if (!string.IsNullOrEmpty(src.SourcePath) && File.Exists(src.SourcePath))
                return resolver.ResolveExternal(src.SourcePath, srgb);
            if (src.EncodedBytes is { Length: > 0 } bytes)
                return resolver.ResolveEmbedded(src.Name, bytes, src.MimeType, srgb);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[Clay] Failed to resolve texture '{src.Name ?? src.SourcePath ?? "(embedded)"}': {ex.Message}");
        }
        return default;
    }

    // ----------------------------------------------------------------------------------------
    // Animation bake
    // ----------------------------------------------------------------------------------------

    private static Avatar BuildAvatar(ModelRigBuilder.Rig rig, ModelImporterSettings settings, string modelName)
    {
        string name = $"{modelName} Avatar";
        if (settings.RigType != ModelRigType.Humanoid)
            return Avatar.CreateGeneric(rig.Skeleton, 0, name);

        Avatar avatar = Avatar.CreateAutomatic(rig.Skeleton, out Motion.HumanoidMapResult report, name);
        avatar = ApplyBoneMap(avatar, rig.Skeleton, settings.HumanoidBoneMap, name);

        if (!avatar.IsHuman)
            Debug.LogWarning($"[Model] '{modelName}' was imported as humanoid but could not be mapped. Missing: {string.Join(", ", report.UnmappedBones)}. It plays as a generic rig until the mapping is fixed.");
        else if (!report.IsRestPoseTPose)
            Debug.LogWarning($"[Model] '{modelName}' does not rest in a T-pose. It retargets, but reach and hand orientation will be offset.");
        return avatar;
    }

    /// <summary>
    /// Lays the hand made mapping over the one the auto mapper found. Fixing the two bones it got wrong
    /// is two entries rather than a whole rig, and a bone the mapper should not have claimed is cleared
    /// by mapping it to nothing.
    /// </summary>
    private static Avatar ApplyBoneMap(Avatar avatar, Motion.Skeleton skeleton, Dictionary<string, string>? map, string name)
    {
        if (map is not { Count: > 0 }) return avatar;

        Motion.HumanDescription description = avatar.Description?.Clone() ?? new Motion.HumanDescription();
        foreach (KeyValuePair<string, string> entry in map)
        {
            if (!Enum.TryParse(entry.Key, ignoreCase: true, out Motion.HumanBodyBone humanBone)) continue;

            int bone = string.IsNullOrEmpty(entry.Value)
                ? Motion.Skeleton.InvalidIndex
                : skeleton.GetBoneIndex(new Motion.StringID(entry.Value));
            description.SetSkeletonBoneIndex(humanBone, bone);
        }

        return Avatar.CreateHumanoid(skeleton, description, name);
    }

    private static PAnim BuildClip(ClayAnim source, Clay.Model clayModel, ModelRigBuilder.Rig rig, Avatar avatar, ModelImporterSettings settings)
    {
        string sourceName = string.IsNullOrEmpty(source.Name) ? "Animation" : source.Name;
        ModelClipSettings overrides = default;
        settings.ClipOverrides?.TryGetValue(sourceName, out overrides);

        string name = string.IsNullOrWhiteSpace(overrides.Name) ? sourceName : overrides.Name!;

        // A humanoid moves by its hips; any other rig by the highest bone the clip moves.
        int body = avatar.Runtime is { IsHuman: true } && avatar.Description is { } description
            ? description.GetSkeletonBoneIndex(Motion.HumanBodyBone.Hips)
            : ModelRigBuilder.AnimatedRootBone(source, rig.Skeleton);
        var root = new ModelRigBuilder.RootExtraction(body,
            overrides.RootTravel ?? true, overrides.RootTurn ?? true, overrides.RootHeight ?? false);

        Motion.CompressedAnimationClip sampled = ModelRigBuilder.BuildClip(
            source, clayModel, rig, settings.AnimationSampleRate, overrides.TrimStart, overrides.TrimEnd, root);

        (float start, float end) = ModelRigBuilder.Trim(source, settings.AnimationSampleRate, overrides.TrimStart, overrides.TrimEnd);
        List<ClipEvent>? events = overrides.Events is { Count: > 0 } onTake ? ModelRigBuilder.PlaceEvents(onTake, start, end) : null;

        // A humanoid rig bakes into muscle space, which is what lets the clip play on another rig at all.
        PAnim clip = settings.RigType == ModelRigType.Humanoid && avatar.Runtime is { IsHuman: true } human
            ? PAnim.FromHumanoid(Motion.HumanoidClip.Bake(human, sampled), avatar, name, events)
            : PAnim.FromSkeletal(sampled, avatar, name, events);

        clip.Loop = overrides.Loop ?? settings.LoopAnimations;
        clip.SourceName = sourceName;
        clip.TakeStart = start;
        return clip;
    }
}

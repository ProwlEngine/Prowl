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
        var clayModel = Clay.Importer.ModelImporter.Load(assetPath.FullName, MapSettings(settings, ext));
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
        var clayModel = Clay.Importer.ModelImporter.Load(stream, format, MapSettings(settings, ext));
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
        var flags = PostProcessPresets.GameQuality;

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

        // FlipUVs: glTF stores UVs with V=0 at the top of the texture (top-left origin convention).
        // FBX and OBJ store V=0 at the bottom (OpenGL convention) which is what Prowl's shaders
        // expect post-import. So only flip when the source is glTF/GLB/VRM - flipping FBX or OBJ
        // would invert UVs and ship textures upside down.
        bool sourceNeedsFlip = fileExt is ".gltf" or ".glb" or ".vrm";
        if (s.FlipUVs && sourceNeedsFlip) flags |= PostProcessFlags.FlipUVs;
        else flags &= ~PostProcessFlags.FlipUVs;

        return new ClaySettings
        {
            PostProcess = flags,
            GlobalScale = s.UnitScale,
            BoneWeightLimit = 4,
            SmoothNormalsAngleDeg = s.SmoothNormalsAngleDeg,
            RecalculateNormals = s.RecalculateNormals,
        };
    }

    // ----------------------------------------------------------------------------------------
    // Bake: Clay model -> Prowl ModelImportResult
    // ----------------------------------------------------------------------------------------

    private static ModelImportResult Bake(Clay.Model clayModel, string modelName, ModelImporterSettings settings)
    {
        if (clayModel.Log.Entries.Count > 0)
        {
            foreach (var entry in clayModel.Log.Entries)
            {
                if (entry.Severity == ImportLogSeverity.Warning)
                    Debug.LogWarning($"[Clay] {entry}");
                else if (entry.Severity == ImportLogSeverity.Error)
                    Debug.LogError($"[Clay] {entry}");
            }
        }

        // 1. Textures - resolve once, share by Clay texture index. Resolution (not necessarily
        // decoding - see IModelTextureResolver) happens through the resolver, so the editor can
        // supply one that only ever produces AssetRefs and never touches pixel data itself.
        var resolver = settings.TextureResolver ?? DefaultModelTextureResolver.Instance;
        var textureCache = new AssetRef<Texture2D>[clayModel.Textures.Count];
        for (int i = 0; i < clayModel.Textures.Count; i++)
            textureCache[i] = ResolveModelTexture(clayModel.Textures[i], resolver);

        // 2. Materials.
        var materials = new List<PMaterial>(clayModel.Materials.Count);
        for (int i = 0; i < clayModel.Materials.Count; i++)
            materials.Add(BuildMaterial(clayModel.Materials[i], textureCache));

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
            var n = clayModel.Nodes[i];
            var go = new GameObject(string.IsNullOrEmpty(n.Name) ? $"Node_{i}" : n.Name);
            nodeGOs[i] = go;
            go.Transform.LocalPosition = n.LocalPosition;
            go.Transform.LocalRotation = n.LocalRotation;
            go.Transform.LocalScale = n.LocalScale;
        }
        // Parenting pass (after all GOs exist so SetParent can find the parent).
        for (int i = 0; i < clayModel.Nodes.Count; i++)
        {
            var parent = clayModel.Nodes[i].Parent;
            if (parent is null) continue;
            nodeGOs[i].SetParent(nodeGOs[parent.Index], worldPositionStays: false);
        }
        // Rename the model root.
        nodeGOs[clayModel.Root.Index].Name = string.IsNullOrEmpty(modelName) ? "Model" : modelName;
        var rootGO = nodeGOs[clayModel.Root.Index];

        // 5. Renderers + skin wiring.
        for (int i = 0; i < clayModel.Nodes.Count; i++)
        {
            var n = clayModel.Nodes[i];
            if (n.MeshIndex < 0) continue;
            var go = nodeGOs[i];
            var mesh = meshes[n.MeshIndex];
            var matRefs = BuildMatRefs(meshSubmeshMaterials[n.MeshIndex], materials);

            if (n.SkinIndex >= 0)
            {
                var clayskin = clayModel.Skins[n.SkinIndex];
                // Mirror Clay.Skin -> Prowl Mesh.BindPoses + Mesh.BoneNames (relative paths).
                mesh.BindPoses = clayskin.InverseBindPoses.ToArray();
                mesh.BoneNames = new string[clayskin.BoneNodeIndices.Length];
                var boneTransforms = new Transform[clayskin.BoneNodeIndices.Length];
                for (int b = 0; b < clayskin.BoneNodeIndices.Length; b++)
                {
                    int boneNodeIdx = clayskin.BoneNodeIndices[b];
                    boneTransforms[b] = nodeGOs[boneNodeIdx].Transform;
                    mesh.BoneNames[b] = Transform.GetRelativePath(boneTransforms[b], rootGO.Transform);
                }

                var smr = go.AddComponent<SkinnedMeshRenderer>();
                smr.SharedMesh = new AssetRef<PMesh>(mesh);
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
                var smr = go.AddComponent<SkinnedMeshRenderer>();
                smr.SharedMesh = new AssetRef<PMesh>(mesh);
                smr.Materials = matRefs;
            }
            else
            {
                var mr = go.AddComponent<MeshRenderer>();
                mr.Mesh = new AssetRef<PMesh>(mesh);
                mr.Materials = matRefs;
            }
        }

        // 5b. Cameras and lights. Both are node-attached with no geometry, so they only need the
        // component putting on the GameObject the node already produced.
        if (settings.ImportCameras)
            for (int i = 0; i < clayModel.Nodes.Count; i++)
                if (clayModel.Nodes[i].CameraIndex is int ci and >= 0)
                    BuildCamera(clayModel.Cameras[ci], nodeGOs[i]);

        if (settings.ImportLights)
            for (int i = 0; i < clayModel.Nodes.Count; i++)
                if (clayModel.Nodes[i].LightIndex is int li and >= 0)
                    BuildLight(clayModel.Lights[li], nodeGOs[i]);

        // 6. Animations.
        var animations = new List<PAnim>(clayModel.AnimationClips.Count);
        foreach (var clip in clayModel.AnimationClips)
            animations.Add(BuildAnimationClip(clip, clayModel, nodeGOs, rootGO, settings.AnimationWrapMode));

        if (animations.Count > 0)
        {
            var anim = rootGO.AddComponent<AnimationComponent>();
            anim.DefaultClip = new AssetRef<PAnim>(animations[0]);
            anim.Clips = animations.Select(c => new AssetRef<PAnim>(c)).ToList();
        }

        return new ModelImportResult
        {
            RootGO = rootGO,
            Meshes = meshes,
            Materials = materials,
            Animations = animations,
        };
    }

    private static List<AssetRef<PMaterial>> BuildMatRefs(int[] submeshMatIndices, List<PMaterial> materials)
    {
        var matRefs = new List<AssetRef<PMaterial>>(submeshMatIndices.Length);
        for (int s = 0; s < submeshMatIndices.Length; s++)
        {
            int idx = submeshMatIndices[s];
            matRefs.Add(idx >= 0 && idx < materials.Count
                ? new AssetRef<PMaterial>(materials[idx])
                : default);
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
                var bw = src.BoneWeights[v];
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
        if (src.BlendShapes is { Length: > 0 })
        {
            var shapes = new PBlendShape[src.BlendShapes.Length];
            for (int i = 0; i < src.BlendShapes.Length; i++)
            {
                var cb = src.BlendShapes[i];
                var frames = new PBlendShapeFrame[cb.Frames.Length];
                for (int f = 0; f < cb.Frames.Length; f++)
                {
                    var cf = cb.Frames[f];
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

        var submeshMatIndices = new int[src.SubMeshes.Length];
        if (src.SubMeshes.Length > 1)
        {
            dst.SetSubMeshCount(src.SubMeshes.Length);
            for (int s = 0; s < src.SubMeshes.Length; s++)
            {
                var sm = src.SubMeshes[s];
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

    /// <summary>
    /// Attaches a <see cref="Camera"/> matching the source lens. Orientation needs no handling here:
    /// glTF aims a camera down its node's -Z, and Clay's coordinate conversion mirrors that to the
    /// +Z the engine treats as forward.
    /// </summary>
    private static void BuildCamera(Clay.Camera src, GameObject go)
    {
        var cam = go.AddComponent<Camera>();

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
    /// Attaches the <see cref="Light"/> subclass matching the source type.
    /// </summary>
    /// <remarks>
    /// glTF intensity is photometric (lux for directional, candela for point and spot) while Prowl's
    /// is an arbitrary scale, so the value is carried across as-is and will usually want adjusting.
    /// Converting it would need a scene-wide exposure convention Prowl does not have.
    /// </remarks>
    private static void BuildLight(Clay.Light src, GameObject go)
    {
        Light light = src.Type switch
        {
            Clay.LightType.Directional => go.AddComponent<DirectionalLight>(),
            Clay.LightType.Spot => go.AddComponent<SpotLight>(),
            _ => go.AddComponent<PointLight>(),
        };

        light.Color = src.Color;
        light.Intensity = src.Intensity;

        // Range is optional in glTF and means unlimited when absent, which no real-time light can
        // do, so an absent one keeps the component's own default.
        if (light is PointLight point && src.Range is { } pointRange)
            point.Range = pointRange;

        if (light is SpotLight spot)
        {
            if (src.Range is { } spotRange) spot.Range = spotRange;

            // glTF measures cone angles from the axis; Prowl's are full cone angles.
            spot.SpotAngle = src.OuterConeAngleRadians * 2f * (180f / MathF.PI);
            spot.InnerSpotAngle = src.InnerConeAngleRadians * 2f * (180f / MathF.PI);
        }
    }

    // ----------------------------------------------------------------------------------------
    // Material bake
    // ----------------------------------------------------------------------------------------

    /// <summary>
    /// Picks the Default/Standard* or Default/Unlit* variant matching the source material's alpha
    /// mode and sidedness. Render state (cull, blend, depth write) is baked into the shader rather
    /// than overridden per material, so the mode has to be chosen here.
    /// </summary>
    private static DefaultShader SelectShader(ClayMaterial src)
    {
        bool twoSided = src.DoubleSided;

        if (src.Unlit)
        {
            return src.AlphaMode switch
            {
                MaterialAlphaMode.Mask => twoSided ? DefaultShader.UnlitCutoutDoubleSided : DefaultShader.UnlitCutout,
                MaterialAlphaMode.Blend => twoSided ? DefaultShader.UnlitTransparentDoubleSided : DefaultShader.UnlitTransparent,
                _ => twoSided ? DefaultShader.UnlitDoubleSided : DefaultShader.Unlit,
            };
        }

        return src.AlphaMode switch
        {
            MaterialAlphaMode.Mask => twoSided ? DefaultShader.StandardCutoutDoubleSided : DefaultShader.StandardCutout,
            MaterialAlphaMode.Blend => twoSided ? DefaultShader.StandardTransparentDoubleSided : DefaultShader.StandardTransparent,
            _ => twoSided ? DefaultShader.StandardDoubleSided : DefaultShader.Standard,
        };
    }

    private static PMaterial BuildMaterial(ClayMaterial src, AssetRef<Texture2D>[] textureCache)
    {
        var mat = new PMaterial(Shader.LoadDefault(SelectShader(src)))
        {
            Name = string.IsNullOrEmpty(src.Name) ? "Material" : src.Name,
        };

        // BaseColor is linear (glTF baseColorFactor), and the Standard shaders multiply _MainColor
        // in after decoding the albedo texture, so it crosses over untouched.
        mat.SetColor("_MainColor", src.BaseColor);

        // An absent albedo texture means "the factor is the colour", so the neutral is white. The
        // grid checker is the missing-texture placeholder for hand-authored materials and would
        // otherwise get multiplied into every untextured imported material.
        mat.SetTexture("_MainTex", OrDefault(ResolveTexture(src.BaseColorTexture, textureCache), DefaultTexture.White));
        mat.SetInt("_MainTexUV", UVSetFor(src.BaseColorTexture, src));

        ApplyTextureTransform(mat, src);

        if (src.AlphaMode == MaterialAlphaMode.Mask)
            mat.SetFloat("_AlphaCutoff", src.AlphaCutoff);

        // The unlit shaders carry only the base slots; everything below has no uniform there.
        if (src.Unlit)
            return mat;

        mat.SetTexture("_NormalTex", OrDefault(ResolveTexture(src.NormalTexture, textureCache), DefaultTexture.Normal));
        mat.SetFloat("_NormalScale", src.NormalScale);
        mat.SetInt("_NormalTexUV", UVSetFor(src.NormalTexture, src));

        // Factors multiply the texture channels, matching glTF. White is the neutral texture so a
        // factor-only material lands on exactly its factors.
        mat.SetTexture("_SurfaceTex", OrDefault(ResolveTexture(src.MetallicRoughnessTexture, textureCache), DefaultTexture.White));
        mat.SetFloat("_Metallic", src.Metallic);
        mat.SetFloat("_Roughness", src.Roughness);
        mat.SetInt("_SurfaceTexUV", UVSetFor(src.MetallicRoughnessTexture, src));

        // Occlusion is its own slot. When a model packs ORM into one image and points both
        // occlusionTexture and metallicRoughnessTexture at it, both slots resolve to that image.
        mat.SetTexture("_OcclusionTex", OrDefault(ResolveTexture(src.OcclusionTexture, textureCache), DefaultTexture.White));
        mat.SetFloat("_OcclusionStrength", src.OcclusionStrength);
        mat.SetInt("_OcclusionTexUV", UVSetFor(src.OcclusionTexture, src));

        mat.SetTexture("_EmissionTex", OrDefault(ResolveTexture(src.EmissiveTexture, textureCache), DefaultTexture.White));
        mat.SetColor("_EmissiveColor", src.EmissiveFactor);
        mat.SetFloat("_EmissionIntensity", src.EmissiveStrength);
        mat.SetInt("_EmissionTexUV", UVSetFor(src.EmissiveTexture, src));

        return mat;
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
        var slot = src.BaseColorTexture;
        if (slot is not null)
        {
            mat.SetVector("_Tiling", slot.Scale);
            mat.SetVector("_Offset", slot.Offset);

            if (slot.Rotation != 0f)
                Debug.LogWarning($"[Clay] Material '{src.Name}' uses a rotated UV transform ({slot.Rotation:0.###} rad); " +
                    "the Standard shaders only support tiling and offset, so the rotation was dropped.");
        }

        foreach (var other in EnumerateSlots(src))
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

    private static AssetRef<Texture2D> ResolveTexture(MaterialTextureSlot? slot, AssetRef<Texture2D>[] cache)
    {
        if (slot is null) return default;
        int idx = slot.TextureIndex;
        if ((uint)idx >= (uint)cache.Length) return default;
        return cache[idx];
    }

    // "No texture in this slot" is never resolved through IModelTextureResolver - it isn't a texture
    // reference to look up, it's the absence of one. Falls back to the shared, GUID-tagged default
    // texture singletons (same as always).
    private static AssetRef<Texture2D> OrDefault(AssetRef<Texture2D> tex, DefaultTexture fallback) =>
        tex.IsExplicitNull ? new AssetRef<Texture2D>(Texture2D.LoadDefault(fallback)) : tex;

    private static AssetRef<Texture2D> ResolveModelTexture(ClayTexture src, IModelTextureResolver resolver)
    {
        try
        {
            if (!string.IsNullOrEmpty(src.SourcePath) && File.Exists(src.SourcePath))
                return resolver.ResolveExternal(src.SourcePath);
            if (src.EncodedBytes is { Length: > 0 } bytes)
                return resolver.ResolveEmbedded(src.Name, bytes, src.MimeType);
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

    private static PAnim BuildAnimationClip(ClayAnim src, Clay.Model clayModel, GameObject[] nodeGOs, GameObject rootGO, AnimationWrapMode wrap)
    {
        var clip = new PAnim
        {
            Name = src.Name,
            // A clip authored on a shared timeline can start after zero; carrying that keeps the
            // player from sitting on the first pose for the gap.
            StartTime = src.StartTime,
            Duration = src.Duration,
            DurationInTicks = src.Duration,
            TicksPerSecond = 1f,
            Wrap = wrap,
        };

        // Bin bindings by target node -> AnimBone. Blend-shape weight channels are handled
        // separately (they target a renderer + named shape, not a bone transform).
        var boneByNode = new Dictionary<int, PAnim.AnimBone>();
        foreach (var b in src.Bindings)
        {
            if (b.NodeIndex < 0 || b.NodeIndex >= nodeGOs.Length) continue;

            if (b.Property == AnimatedProperty.BlendShapeWeight)
            {
                ApplyBlendShapeBinding(b, clayModel, nodeGOs, rootGO, clip);
                continue;
            }

            var targetGO = nodeGOs[b.NodeIndex];
            string bonePath = Transform.GetRelativePath(targetGO.Transform, rootGO.Transform);
            if (!boneByNode.TryGetValue(b.NodeIndex, out var bone))
            {
                bone = new PAnim.AnimBone { BoneName = bonePath };
                boneByNode[b.NodeIndex] = bone;
            }
            ApplyBinding(b, bone);
        }

        // P/R/S backfill is now done at Clay's SceneBaker so every consumer gets complete
        // 9-channel-per-bone clips. Any (NodeIndex, Property) tuple still null here would be
        // a Clay-side regression.

        foreach (var bone in boneByNode.Values)
            clip.AddBone(bone);

        clip.EnsureQuaternionContinuity();
        return clip;
    }

    /// <summary>
    /// Hands Clay's curve to the bone channel unchanged. Both sides are
    /// <see cref="Prowl.Vector.AnimationCurve"/>, so per-key interpolation and cubic tangents cross
    /// intact rather than being resampled into scalar points.
    /// </summary>
    private static void ApplyBinding(ClayBinding binding, PAnim.AnimBone bone)
    {
        switch (binding.Property)
        {
            case AnimatedProperty.Position: bone.Position = binding.Curve; break;
            case AnimatedProperty.Rotation: bone.Rotation = binding.Curve; break;
            case AnimatedProperty.Scale: bone.Scale = binding.Curve; break;
            // Visibility: not handled by Prowl yet. BlendShapeWeight is handled separately
            // (see ApplyBlendShapeBinding) since it targets a renderer + named shape, not a bone.
        }
    }

    /// <summary>
    /// Converts a Clay BlendShapeWeight binding into a Prowl <see cref="PAnim.BlendShapeAnim"/>.
    /// Resolves the renderer path and the blend-shape name. Clay normalizes weight curves to the
    /// 0-100 scale (matching <c>SetBlendShapeWeight</c> and the frame weights), so no scaling here.
    /// </summary>
    private static void ApplyBlendShapeBinding(ClayBinding binding, Clay.Model clayModel, GameObject[] nodeGOs, GameObject rootGO, PAnim clip)
    {
        var node = clayModel.Nodes[binding.NodeIndex];
        if (node.MeshIndex < 0 || node.MeshIndex >= clayModel.Meshes.Count) return;

        var clayMesh = clayModel.Meshes[node.MeshIndex];
        if (binding.SubIndex < 0 || binding.SubIndex >= clayMesh.BlendShapes.Length) return;

        string shapeName = clayMesh.BlendShapes[binding.SubIndex].Name ?? $"BlendShape{binding.SubIndex}";
        string path = Transform.GetRelativePath(nodeGOs[binding.NodeIndex].Transform, rootGO.Transform);

        clip.AddBlendShape(new PAnim.BlendShapeAnim
        {
            Path = path,
            ShapeName = shapeName,
            Weight = binding.Curve,
        });
    }

}

// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.IO;

using Prowl.Runtime.Resources;

namespace Prowl.Runtime.AssetImporting;

public struct ModelImporterSettings
{
    /// <summary>Generate normals if the mesh doesn't have them.</summary>
    public bool GenerateNormals = true;

    /// <summary>Use smooth (area-weighted vertex) normals instead of flat/faceted.</summary>
    public bool GenerateSmoothNormals = true;

    /// <summary>Force recalculate normals even if the mesh already has them.</summary>
    public bool RecalculateNormals = false;

    /// <summary>Generate tangent vectors for normal mapping.</summary>
    public bool CalculateTangentSpace = true;

    /// <summary>Flip V texture coordinate (some formats use top-left origin).</summary>
    public bool FlipUVs = true;

    /// <summary>Uniform scale applied to all vertex positions.</summary>
    public float UnitScale = 1.0f;

    /// <summary>Generate a lightmap UV set (UV2) for every mesh via Prowl.Unwrapper. Off by default
    /// (it's slow and some models ship their own UV2); the built-in default models force it on.</summary>
    public bool GenerateLightmapUVs = false;

    /// <summary>Strategy for turning a model's texture references into AssetRefs. Null (the default)
    /// uses <see cref="DefaultModelTextureResolver"/>, which decodes/GPU-uploads immediately - correct
    /// for a direct runtime load with no separate asset-tracking step. The editor importer supplies
    /// its own resolver that only ever produces GUID-backed AssetRefs, with no decode of its own.</summary>
    public IModelTextureResolver? TextureResolver;

    public ModelImporterSettings() { }
}

/// <summary>
/// Result of a model import live objects ready for the asset database to process.
/// </summary>
public class ModelImportResult
{
    public GameObject? RootGO;
    public List<Mesh> Meshes = [];
    public List<Material> Materials = [];
    public List<AnimationClip> Animations = [];
}

/// <summary>
/// Loads .gltf / .glb / .obj into a fully-baked <see cref="ModelImportResult"/>. Backed by the
/// Prowl.Clay library (the previous in-tree GltfImporter / ObjImporter were retired in favor of
/// this single unified path).
/// </summary>
public class ModelImporter
{
    public ModelImportResult Import(FileInfo assetPath, ModelImporterSettings? settings = null)
    {
        var s = settings ?? new ModelImporterSettings();
        return PostProcess(ClayBackedImporter.Import(assetPath, s), s);
    }

    public ModelImportResult Import(Stream stream, string virtualPath, ModelImporterSettings? settings = null)
    {
        var s = settings ?? new ModelImporterSettings();
        return PostProcess(ClayBackedImporter.Import(stream, virtualPath, s), s);
    }

    private static ModelImportResult PostProcess(ModelImportResult result, ModelImporterSettings settings)
    {
        // Lightmap UV2 generation lives in the runtime import path so the built-in default models
        // (parsed via this importer at runtime) get it too, not just editor-imported models.
        if (settings.GenerateLightmapUVs)
            for (int i = 0; i < result.Meshes.Count; i++)
                LightmapUVGenerator.Generate(result.Meshes[i]);
        return result;
    }
}

/// <summary>
/// Strategy for turning a model's texture references into <see cref="AssetRef{T}"/>s during import.
/// Invoked once per distinct texture the model references (the caller caches and reuses the result
/// across every material slot that references the same texture).
/// <para/>
/// The default (used when nothing else is supplied) decodes and GPU-uploads immediately - correct
/// for a direct runtime load with no separate asset-tracking step to hand off to. The editor supplies
/// its own implementation that never decodes another asset's pixel data itself: an externally
/// referenced texture is resolved purely by path, against the asset database's existing GUID for
/// that file, and an embedded texture is registered as a proper sub-asset for the asset database to
/// own and cache - so importing a model never grows or duplicates the pixel data of anything else.
/// </summary>
public interface IModelTextureResolver
{
    /// <summary>
    /// Resolve a texture referenced by a sibling file on disk. <paramref name="sourcePath"/> is
    /// always an already-resolved, existing, absolute path.
    /// </summary>
    /// <returns>An <see cref="AssetRef{T}"/> for the texture, or <see langword="default"/> if it
    /// can't/shouldn't be resolved - the caller falls back to the material slot's built-in default
    /// texture (Grid/Normal/Surface/Emission).</returns>
    AssetRef<Texture2D> ResolveExternal(string sourcePath);

    /// <summary>
    /// Resolve a texture embedded directly in the model file (GLB bufferView, FBX Video::Clip
    /// content, data: URI - no file of its own).
    /// </summary>
    /// <returns>An <see cref="AssetRef{T}"/> for the texture, or <see langword="default"/> if it
    /// can't/shouldn't be resolved.</returns>
    AssetRef<Texture2D> ResolveEmbedded(string? name, byte[] encodedBytes, string? mimeType);
}

/// <summary>
/// The <see cref="IModelTextureResolver"/> used when a model import doesn't supply its own -
/// i.e. every genuine direct runtime load, with no separate asset-tracking system to hand
/// resolution off to. Decodes and GPU-uploads immediately, matching how model-referenced textures
/// were always loaded before this resolver existed.
/// </summary>
public sealed class DefaultModelTextureResolver : IModelTextureResolver
{
    public static readonly DefaultModelTextureResolver Instance = new();

    public AssetRef<Texture2D> ResolveExternal(string sourcePath)
    {
        try
        {
            var tex = Texture2D.LoadFromFile(sourcePath, generateMipmaps: true);
            if (string.IsNullOrEmpty(tex.Name))
                tex.Name = Path.GetFileNameWithoutExtension(sourcePath);
            return new AssetRef<Texture2D>(tex);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[Clay] Failed to load external texture '{sourcePath}': {ex.Message}");
            return default;
        }
    }

    public AssetRef<Texture2D> ResolveEmbedded(string? name, byte[] encodedBytes, string? mimeType)
    {
        try
        {
            using var ms = new MemoryStream(encodedBytes);
            var tex = Texture2D.LoadFromStream(ms, generateMipmaps: true);
            tex.Name = string.IsNullOrEmpty(name) ? "EmbeddedTexture" : name;
            return new AssetRef<Texture2D>(tex);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[Clay] Failed to load embedded texture '{name ?? "(unnamed)"}': {ex.Message}");
            return default;
        }
    }
}

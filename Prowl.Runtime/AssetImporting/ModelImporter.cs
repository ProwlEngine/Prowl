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

    /// <summary>Use smooth (angle-weighted vertex) normals instead of flat/faceted.</summary>
    public bool GenerateSmoothNormals = true;

    /// <summary>
    /// Edges sharper than this stay hard when generating smooth normals; the vertices along them
    /// are split so each side keeps its own normal. Only used when <see cref="GenerateSmoothNormals"/>
    /// is on.
    /// </summary>
    public float SmoothNormalsAngleDeg = 80f;

    /// <summary>Force recalculate normals even if the mesh already has them.</summary>
    public bool RecalculateNormals = false;

    /// <summary>Generate tangent vectors for normal mapping.</summary>
    public bool CalculateTangentSpace = true;

    /// <summary>Uniform scale applied to all vertex positions.</summary>
    public float UnitScale = 1.0f;

    /// <summary>
    /// Build the materials the file describes. Off makes every renderer fall back to the default
    /// </summary>
    public bool ImportMaterials = true;

    /// <summary>Build the animation clips the file describes, and the component that plays them.</summary>
    public bool ImportAnimations = true;

    /// <summary>
    /// Keep morph targets. Off drops them, which is worth doing for a model whose shapes the game
    /// never drives: the deltas are a full extra copy of the vertex data per shape.
    /// </summary>
    public bool ImportBlendShapes = true;

    /// <summary>
    /// Merge sibling meshes that share a material into one. Reduces draw calls for models authored
    /// as many small parts, at the cost of the parts no longer being separately addressable.
    /// </summary>
    public bool OptimizeMeshes = false;

    /// <summary>
    /// Collapse pass-through nodes, folding their transforms into their children. Sockets, markers,
    /// bones, animated nodes and anything carrying metadata are kept regardless.
    /// </summary>
    public bool OptimizeHierarchy = false;

    /// <summary>Node names <see cref="OptimizeHierarchy"/> must never collapse.</summary>
    public string[] PreserveNodeNames = [];

    /// <summary>
    /// Fail the import when the file fails cross-reference validation, instead of warning and
    /// importing whatever survived.
    /// </summary>
    public bool StrictValidation = false;

    /// <summary>
    /// Which scene to import from a file defining several, or -1 for the one the file nominates.
    /// </summary>
    public int SceneIndex = -1;

    /// <summary>
    /// Create a <see cref="Camera"/> for every camera the file defines. Imported cameras are added
    /// disabled, since they describe viewpoints the author set up rather than the one the game
    /// renders from.
    /// </summary>
    public bool ImportCameras = true;

    /// <summary>Create a light component for every punctual light the file defines.</summary>
    public bool ImportLights = true;

    /// <summary>
    /// Whether imported clips loop. Model formats carry no looping flag of their own, so this is a
    /// choice the importer has to make rather than read. Looping suits the cycles most character
    /// animation ships as; a one-shot clip (a door, a chest, an emote) does not.
    /// </summary>
    public bool LoopAnimations = true;

    /// <summary>
    /// How the rig is built. Generic drives the bones as the file names them; Humanoid also maps them
    /// to the human body, so clips retarget onto any humanoid rig whatever its proportions.
    /// </summary>
    public ModelRigType RigType = ModelRigType.Generic;

    /// <summary>
    /// Frames per second clips are sampled at. Curves are resampled on import because a shipped game
    /// only ever samples, and frames compress and blend where curves do neither.
    /// </summary>
    public float AnimationSampleRate = 30f;

    /// <summary>Generate a lightmap UV set (UV2) for every mesh via Prowl.Unwrapper. Off by default
    /// (it's slow and some models ship their own UV2); the built-in default models force it on.</summary>
    public bool GenerateLightmapUVs = false;

    /// <summary>
    /// Per clip overrides, keyed by the name the clip has in the file. A clip with no entry takes the
    /// settings above.
    /// </summary>
    public Dictionary<string, ModelClipSettings>? ClipOverrides;

    /// <summary>
    /// Where a material comes from. Null builds every material the file describes; an implementation
    /// can point a slot at an asset that was extracted out of the model instead.
    /// </summary>
    public IModelMaterialResolver? MaterialResolver;

    /// <summary>
    /// Humanoid bone name to the skeleton bone playing it, applied over whatever the auto mapper found.
    /// An empty value clears a bone the auto mapper got wrong. Only read for a humanoid rig.
    /// </summary>
    public Dictionary<string, string>? HumanoidBoneMap;

    /// <summary>Strategy for turning a model's texture references into AssetRefs. Null (the default)
    /// uses <see cref="DefaultModelTextureResolver"/>, which decodes/GPU-uploads immediately - correct
    /// for a direct runtime load with no separate asset-tracking step. The editor importer supplies
    /// its own resolver that only ever produces GUID-backed AssetRefs, with no decode of its own.</summary>
    public IModelTextureResolver? TextureResolver;

    public ModelImporterSettings() { }
}

/// <summary>
/// What one clip of a model should become, for the clips that want something other than the defaults.
/// </summary>
public struct ModelClipSettings
{
    /// <summary>A new name for the clip, or null to keep the one in the file.</summary>
    public string? Name;

    /// <summary>Whether this clip loops, or null to take the import's setting.</summary>
    public bool? Loop;

    /// <summary>Seconds trimmed from the start of the clip.</summary>
    public float TrimStart;

    /// <summary>The clip's new end, in seconds from its original start. Zero keeps the original end.</summary>
    public float TrimEnd;

    /// <summary>
    /// Whether the body's travel across the ground moves the character instead of staying in the pose,
    /// or null for the default, which is on.
    /// </summary>
    public bool? RootTravel;

    /// <summary>Whether the body's turn moves the character, or null for the default, which is on.</summary>
    public bool? RootTurn;

    /// <summary>
    /// The clip's markers, timed in seconds of the take as the file holds it rather than of the trimmed
    /// clip, so changing the trim leaves each one on the moment it marks. Any the trim cuts away are dropped.
    /// </summary>
    public List<ClipEvent>? Events;

    /// <summary>
    /// Whether the body's rise and fall moves the character, or null for the default, which is off: a
    /// jump usually wants its height in the pose, over a controller that stays on the ground.
    /// </summary>
    public bool? RootHeight;

    public ModelClipSettings() { }
}

/// <summary>
/// Decides where a material comes from. The editor points a slot at an asset the user extracted out of
/// the model, so edits to it survive a reimport; anything it does not claim is built and owned by the
/// import as before.
/// </summary>
public interface IModelMaterialResolver
{
    /// <summary>
    /// The asset standing in for a material the file defines, or <see langword="default"/> to have the
    /// import build it. Returning default for a reference that has gone missing is what lets a model
    /// heal itself on the next reimport.
    /// </summary>
    AssetRef<Material> Resolve(string materialName);
}

/// <summary>
/// Result of a model import live objects ready for the asset database to process.
/// </summary>
public class ModelImportResult
{
    public GameObject? RootGO;
    public List<Mesh> Meshes = [];
    /// <summary>The materials this import built and owns. Extracted ones are referenced, not listed.</summary>
    public List<Material> Materials = [];
    public List<AnimationClip> Animations = [];

    /// <summary>The rig the model's clips play on, or null when it has no bones and no animation.</summary>
    public Avatar? Avatar;
}

/// <summary>How a model's rig is built on import.</summary>
public enum ModelRigType
{
    /// <summary>No skeleton and no clips, for a model that is only geometry.</summary>
    None,
    /// <summary>Bones as the file names them.</summary>
    Generic,
    /// <summary>Bones mapped to the human body, so clips retarget across rigs.</summary>
    Humanoid,
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

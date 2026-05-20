using System.Collections.Generic;
using Prowl.Vector;

namespace Photonic.Demo;

/// <summary>How the bake-time UV1 layer is produced for a given model on import.</summary>
internal enum UV1Strategy
{
    /// <summary>Run Prowl.Unwrapper. Best quality, slow on large meshes.</summary>
    AutoUnwrap,
    /// <summary>Per-triangle shelf-pack. Every triangle gets a unique atlas region; no chart overlap.</summary>
    TrianglePack,
    /// <summary>Use the highest-numbered UV layer the model already ships with (prefers UV2, then UV1, then UV0).</summary>
    UseExisting,
}

/// <summary>Decoded geometry + materials for one source file. Constructed by <see cref="ModelLoader"/>.</summary>
internal sealed class LoadedModel
{
    public required string SourcePath;
    public required string DisplayName;

    public required Float3[] Positions;
    public required Float3[] Normals;
    public required Float2[] UV0;
    public required int[]    Indices;

    public required SubMeshSlice[] SubMeshes;
    public required MaterialInfo[] Materials;
    public required TextureBlob?[] Textures;

    /// <summary>Best provided UV layer for the <see cref="UV1Strategy.UseExisting"/> path. Empty when the model only had UV0.</summary>
    public required Float2[] BestExistingUV;
    /// <summary>True if <see cref="BestExistingUV"/> came from a layer other than UV0 (i.e. it's worth using as a lightmap layer).</summary>
    public required bool HasDedicatedUV;

    public struct SubMeshSlice
    {
        public int IndexStart;
        public int IndexCount;
        public int MaterialIndex;
    }

    public sealed class MaterialInfo
    {
        public required string Name;
        public Float3 BaseColor;
        public int DiffuseTextureIndex = -1;
    }

    public sealed class TextureBlob
    {
        public required string Name;
        public required int Width;
        public required int Height;
        public required byte[] RGBA;
    }
}

/// <summary>One model placement in the demo scene.</summary>
internal sealed class SceneModel
{
    public string Name = "Model";
    public required LoadedModel Source;

    // Transform, edited via the Inspector. Recomposed to a matrix before bake / draw.
    public System.Numerics.Vector3 Position = System.Numerics.Vector3.Zero;
    public System.Numerics.Vector3 RotationEulerDeg = System.Numerics.Vector3.Zero;
    public System.Numerics.Vector3 Scale = System.Numerics.Vector3.One;

    public UV1Strategy UV1Mode = UV1Strategy.AutoUnwrap;

    // Produced by UV1Generator at bake time. The full per-vertex layout of UV1 (may differ in length
    // from Source.Positions because AutoUnwrap can split seam-conflicting verts).
    public Float3[] BakedPositions = System.Array.Empty<Float3>();
    public Float3[] BakedNormals   = System.Array.Empty<Float3>();
    public Float2[] BakedUV0       = System.Array.Empty<Float2>();
    public Float2[] BakedUV1       = System.Array.Empty<Float2>();
    public int[]    BakedIndices   = System.Array.Empty<int>();
    public LoadedModel.SubMeshSlice[] BakedSubMeshes = System.Array.Empty<LoadedModel.SubMeshSlice>();

    // Filled by the bake pipeline so the renderer can sample the correct atlas.
    public int AtlasTargetIndex = -1;
    public Float2 UVOffset = Float2.Zero;
    public Float2 UVScale  = Float2.One;
}

internal enum SceneLightKind { Directional, Point, Spot }

/// <summary>One light in the demo scene. Edited via the Inspector.</summary>
internal sealed class SceneLight
{
    public string Name = "Light";
    public SceneLightKind Kind = SceneLightKind.Directional;

    public System.Numerics.Vector3 Position  = new(0f, 5f, 0f);
    public System.Numerics.Vector3 Direction = new(-0.5f, -1f, -0.3f);
    public System.Numerics.Vector3 Color     = new(3f, 2.8f, 2.4f);
    public float Range = 20f;
    public float ConeAngleDeg = 30f;
    public bool CastsShadows = true;
}

internal enum SceneSelectionKind { None, Model, Light }

internal struct SceneSelection
{
    public SceneSelectionKind Kind;
    public int Index;
    public static SceneSelection None => new() { Kind = SceneSelectionKind.None, Index = -1 };
}

/// <summary>Demo-scene state: list of models, list of lights, current selection.</summary>
internal sealed class Scene
{
    public readonly List<SceneModel> Models = new();
    public readonly List<SceneLight> Lights = new();
    public SceneSelection Selection = SceneSelection.None;

    public SceneModel? SelectedModel =>
        Selection.Kind == SceneSelectionKind.Model && Selection.Index >= 0 && Selection.Index < Models.Count
            ? Models[Selection.Index] : null;

    public SceneLight? SelectedLight =>
        Selection.Kind == SceneSelectionKind.Light && Selection.Index >= 0 && Selection.Index < Lights.Count
            ? Lights[Selection.Index] : null;

    public void AddModel(SceneModel m)
    {
        Models.Add(m);
        Selection = new SceneSelection { Kind = SceneSelectionKind.Model, Index = Models.Count - 1 };
    }

    public void AddLight(SceneLight l)
    {
        Lights.Add(l);
        Selection = new SceneSelection { Kind = SceneSelectionKind.Light, Index = Lights.Count - 1 };
    }

    public void RemoveSelected()
    {
        switch (Selection.Kind)
        {
            case SceneSelectionKind.Model:
                if (Selection.Index >= 0 && Selection.Index < Models.Count) Models.RemoveAt(Selection.Index);
                break;
            case SceneSelectionKind.Light:
                if (Selection.Index >= 0 && Selection.Index < Lights.Count) Lights.RemoveAt(Selection.Index);
                break;
        }
        Selection = SceneSelection.None;
    }

    public static Float4x4 GetModelTransform(SceneModel m)
    {
        var q = Quaternion.FromEuler(new Float3(m.RotationEulerDeg.X, m.RotationEulerDeg.Y, m.RotationEulerDeg.Z));
        return Float4x4.CreateTRS(
            new Float3(m.Position.X, m.Position.Y, m.Position.Z),
            q,
            new Float3(m.Scale.X, m.Scale.Y, m.Scale.Z));
    }
}

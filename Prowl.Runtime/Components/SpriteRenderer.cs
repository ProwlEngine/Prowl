// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Echo;
using System.Collections.Generic;

using Prowl.Graphite.Rendering;
using Prowl.Runtime.Rendering;
using Prowl.Runtime.Resources;
using Prowl.Vector;

using GMaterial = Prowl.Graphite.Rendering.Material;
using GTexture = Prowl.Graphite.Texture;
using Material = Prowl.Runtime.Resources.Material;
using Shader = Prowl.Runtime.Resources.Shader;

namespace Prowl.Runtime;

/// <summary>
/// Draws a <see cref="Sprite"/> in world space on the XY plane at its native size (pixels-per-unit). It uses
/// the sprite's own geometry, so a tight-mesh sprite renders tight. (Nine-slice / tiled / filled are UI
/// concerns handled by <c>UIImage</c>, not by world sprites.)
/// </summary>
[AddComponentMenu("Rendering/Sprite Renderer")]
[ComponentIcon("")] // Image
public class SpriteRenderer : Component, IMaterialRenderer, IColorTint, IRenderSync
{
    int IMaterialRenderer.MaterialSlotCount => 1;
    Material? IMaterialRenderer.GetMaterial(int slot) { MaterialSlots.CheckSingle(slot); return Material; }
    void IMaterialRenderer.SetMaterial(int slot, Material? material) { MaterialSlots.CheckSingle(slot); Material = material; }
    Color IColorTint.Tint { get => Color; set => Color = value; }

    /// <summary>Sorting-order spacing along Z (world units) so higher orders sort in front for the transparent queue.</summary>
    private const float SortBias = 0.0001f;

    public Sprite? Sprite { get => sprite; set { sprite = value; Wake(); } }
    [SerializeField] private Sprite? sprite;

    /// <summary>Tint multiplied into the sprite. Baked into vertex color.</summary>
    public Color Color { get => color; set { color = value; Wake(); } }
    [SerializeField] private Color color = Color.White;

    public bool FlipX { get => flipX; set { flipX = value; Wake(); } }
    [SerializeField] private bool flipX;
    public bool FlipY { get => flipY; set { flipY = value; Wake(); } }
    [SerializeField] private bool flipY;

    /// <summary>Optional material override. Defaults to the built-in unlit alpha-blended sprite material.</summary>
    public Material? Material { get => material; set { material = value; Wake(); } }
    [SerializeField] private Material? material;

    /// <summary>Higher values render in front of lower ones (applied as a small Z bias for the transparent queue).</summary>
    public int SortingOrder { get => sortingOrder; set { sortingOrder = value; Wake(); } }
    [SerializeField] private int sortingOrder;

    public ShadowCasting ShadowCasting { get => shadowCasting; set { shadowCasting = value; Wake(); } }
    [SerializeField] private ShadowCasting shadowCasting = ShadowCasting.Off;

    [System.NonSerialized] private Mesh? _mesh;
    [System.NonSerialized, NotHeld] private readonly RenderableState _renderable = new();
    [System.NonSerialized, NotHeld] private readonly OwnedMaterial _material = new();
    [System.NonSerialized, NotHeld] private readonly GMaterial[] _gpuMaterials = new GMaterial[1];
    [System.NonSerialized] private uint _worldVersion, _transformVersion;
    [System.NonSerialized] private int _sortingOrder;

    // Snapshot of the inputs the baked mesh was built from, so we only rebuild when something changes.
    [System.NonSerialized] private Sprite? _bakedSprite;
    [System.NonSerialized] private int _bakedContent;
    [System.NonSerialized] private bool _bakedFlipX, _bakedFlipY;
    [System.NonSerialized] private Color _bakedColor;

    private static Material? s_defaultMaterial;

    /// <summary>The shared built-in unlit, alpha-blended, tinted sprite material.</summary>
    public static Material DefaultSpriteMaterial =>
        s_defaultMaterial.IsValid() ? s_defaultMaterial : (s_defaultMaterial = new Material(Shader.LoadDefault(DefaultShader.Sprite)));

    /// <summary>The renderable in the scene's world, invalid while nothing draws.</summary>
    public RenderableHandle Renderable => _renderable.Handle;

    // Syncs it again next frame, for a setting that changed
    private void Wake() => SceneRendering.Wake(this, GameObject);

    bool IRenderSync.Sync(RenderWorld world)
    {
        Sprite? sprite = Sprite;
        Texture2D? tex = sprite.IsValid() ? sprite.Texture : null;
        if (sprite.IsNotValid() || tex.IsNotValid() || tex.Width == 0 || tex.Height == 0 || sprite.Rect.Width <= 0 || sprite.Rect.Height <= 0)
        {
            Detach(world);
            return false;
        }

        EnsureMesh(sprite);
        Mesh? mesh = _mesh;
        if (mesh.IsNotValid())
        {
            Detach(world);
            return false;
        }

        Material assigned = Material;
        Material material = assigned.IsValid() ? assigned : DefaultSpriteMaterial;
        GMaterial? gpu = _material.Of(material.IsLoaded ? material.GpuMaterial : null);
        if (gpu == null)
        {
            if (material.State == AssetState.Unloaded) _ = material.LoadAsync();
            _renderable.Detach(world);
            return false;
        }
        _material.SetTexture("_MainTex", tex);
        foreach (KeyValuePair<string, Texture2D> secondary in sprite.SecondaryTextures)
            if (secondary.Value.IsValid()) _material.SetTexture(secondary.Key, secondary.Value);
        _gpuMaterials[0] = gpu;

        Float4x4 matrix = Transform.LocalToWorldMatrix;
        if (SortingOrder != 0)
            matrix *= Float4x4.CreateTranslation(new Float3(0, 0, SortingOrder * SortBias));
        if (_worldVersion != Transform.WorldVersion || _sortingOrder != SortingOrder)
        {
            _worldVersion = Transform.WorldVersion;
            _sortingOrder = SortingOrder;
            _transformVersion++;
        }

        _renderable.Sync(world, new RenderableState.Desc
        {
            Mesh = mesh.GpuIfLoaded,
            Materials = _gpuMaterials,
            Transform = (Double4x4)matrix,
            TransformVersion = _transformVersion,
            Flags = RenderableState.FlagsFor(GameObject, false, false) | ShadowCasting.ToFlags(),
            LayerMask = RenderableState.LayerBit(GameObject.LayerIndex),
            ObjectId = (uint)InstanceID,
        });
        return material.IsLoaded && tex.IsLoaded;
    }

    void IRenderSync.Detach(RenderWorld world) => Detach(world);

    private void Detach(RenderWorld world)
    {
        _renderable.Detach(world);
        _material.Release();
    }

    private void EnsureMesh(Sprite sprite)
    {
        if (!NeedsRebuild(sprite)) return;

        Float2[] srcVerts = sprite.Vertices;
        Float2[] srcUV = sprite.UV;
        ushort[] srcIdx = sprite.Indices;
        int n = srcVerts.Length;
        if (n < 3 || srcIdx.Length < 3 || srcUV.Length != n)
            return; // nothing valid to draw; keep the previous mesh (if any)

        if (_mesh.IsNotValid()) _mesh = new Mesh();

        // Flip mirrors the geometry about the pivot - vertices are already pivot-relative, so negating
        // an axis is the whole operation. Mirroring the UVs instead would slide the artwork across a
        // silhouette that stayed put: invisible on a quad, but a tight mesh would then clip its own
        // image against the wrong edges.
        float flipX = FlipX ? -1f : 1f;
        float flipY = FlipY ? -1f : 1f;

        var verts = new Float3[n];
        var uvs = new Float2[n];
        var colors = new Color32[n];
        Color32 tint = (Color32)Color;
        for (int i = 0; i < n; i++)
        {
            verts[i] = new Float3(srcVerts[i].X * flipX, srcVerts[i].Y * flipY, 0f);
            uvs[i] = srcUV[i];
            colors[i] = tint;
        }

        uint[] idx = new uint[srcIdx.Length];
        for (int i = 0; i < srcIdx.Length; i++) idx[i] = srcIdx[i];

        // Mirroring exactly one axis reverses the winding. The built-in sprite shader is Cull Off, but
        // an assigned material need not be, so put the orientation back.
        if (FlipX ^ FlipY)
            for (int i = 0; i + 2 < idx.Length; i += 3)
                (idx[i + 1], idx[i + 2]) = (idx[i + 2], idx[i + 1]);

        _mesh.Vertices = verts; // sets first so length-matched arrays below validate
        _mesh.UV = uvs;
        _mesh.Colors32 = colors;
        _mesh.Indices = idx;
        _mesh.RecalculateBounds();
        _mesh.Upload();

        Snapshot(sprite);
    }

    private bool NeedsRebuild(Sprite sprite) =>
        _mesh.IsNotValid() ||
        !ReferenceEquals(_bakedSprite, sprite) || _bakedContent != sprite.ContentVersion ||
        _bakedFlipX != FlipX || _bakedFlipY != FlipY ||
        !_bakedColor.Equals(Color);

    private void Snapshot(Sprite sprite)
    {
        _bakedSprite = sprite;
        _bakedContent = sprite.ContentVersion;
        _bakedFlipX = FlipX; _bakedFlipY = FlipY;
        _bakedColor = Color;
    }

    public override void DrawGizmosSelected()
    {
        Sprite? sprite = Sprite;
        if (sprite.IsNotValid()) return;

        float ppu = sprite.PixelsPerUnit > 0 ? sprite.PixelsPerUnit : 100f;
        float w = sprite.Rect.Width / ppu, h = sprite.Rect.Height / ppu;
        Float2 pivot = sprite.Pivot;
        float x0 = -pivot.X * w, y0 = -pivot.Y * h, x1 = (1f - pivot.X) * w, y1 = (1f - pivot.Y) * h;

        // Match the mesh, which mirrors about the pivot. Only visible with an off-centre pivot.
        if (FlipX) (x0, x1) = (-x1, -x0);
        if (FlipY) (y0, y1) = (-y1, -y0);

        // Rect outline, following the GameObject's transform (so rotation/scale are respected).
        Float4x4 world = Transform.LocalToWorldMatrix;
        Float3 a = Float4x4.TransformPoint(new Float3(x0, y0, 0f), world);
        Float3 b = Float4x4.TransformPoint(new Float3(x1, y0, 0f), world);
        Float3 c = Float4x4.TransformPoint(new Float3(x1, y1, 0f), world);
        Float3 d = Float4x4.TransformPoint(new Float3(x0, y1, 0f), world);

        var outline = new Color(0.4f, 1f, 0.55f, 1f);
        Debug.DrawLine(a, b, outline);
        Debug.DrawLine(b, c, outline);
        Debug.DrawLine(c, d, outline);
        Debug.DrawLine(d, a, outline);

        // Pivot cross at the sprite's origin (the GameObject position).
        Float3 p = Transform.Position;
        Float3 right = Transform.Right * 0.08f;
        Float3 up = Transform.Up * 0.08f;
        var pivotColor = new Color(1f, 0.85f, 0.2f, 1f);
        Debug.DrawLine(p - right, p + right, pivotColor);
        Debug.DrawLine(p - up, p + up, pivotColor);
    }

    protected override void OnDispose()
    {
        if (_mesh.IsValid()) _mesh.Dispose();
        _mesh = null;
        base.OnDispose();
    }
}

/// <summary>
/// A renderer's own child of a material: it draws with everything the material sets plus the few values only this
/// renderer gives, such as a sprite's texture.
/// </summary>
internal sealed class OwnedMaterial
{
    private GMaterial? _parent;
    private GMaterial? _child;
    private readonly Dictionary<string, (GTexture? Texture, Prowl.Graphite.SamplerDescription Sampler)> _textures = [];
    private readonly Dictionary<string, Float4> _values = [];

    /// <summary>The child of <paramref name="parent"/>, made again when the parent changes. Null without a parent.</summary>
    public GMaterial? Of(GMaterial? parent)
    {
        if (parent == null)
        {
            Release();
            return null;
        }
        if (_child == null || _child.IsDisposed || _parent != parent)
        {
            Release();
            _child = parent.CreateChild();
            _parent = parent;
        }
        if (_child.Shader != parent.Shader)
        {
            _child.Shader = parent.Shader;
            _textures.Clear();
            _values.Clear();
        }
        if (_child.CastShadows != parent.CastShadows) _child.CastShadows = parent.CastShadows;
        return _child;
    }

    /// <summary>Gives the child a texture when its shader has the property and the texture or its sampler changed.</summary>
    public void SetTexture(string name, Texture2D? texture)
    {
        if (_child == null) return;
        GTexture? gpu = texture.IsValid() ? texture.GpuIfLoaded : null;
        if (texture.IsValid() && gpu == null && texture.State == AssetState.Unloaded) _ = texture.LoadAsync();
        Prowl.Graphite.SamplerDescription sampler = texture.IsValid() ? texture.SamplerUnchecked : Prowl.Graphite.SamplerDescription.Linear;
        if (_textures.TryGetValue(name, out var bound) && bound.Texture == gpu && bound.Sampler.Equals(sampler)) return;
        _textures[name] = (gpu, sampler);
        if (_child.Shader.FindProperty(name) >= 0) _child.SetTexture(name, gpu, sampler);
    }

    /// <summary>Gives the child a colour when its shader has the property and the colour changed.</summary>
    public void SetColor(string name, Color color)
    {
        Float4 value = new(color.R, color.G, color.B, color.A);
        if (Unchanged(name, value)) return;
        _child!.SetColor(name, value);
    }

    /// <summary>Gives the child a two component value when its shader has the property and the value changed.</summary>
    public void SetVector(string name, Float2 value)
    {
        if (Unchanged(name, new Float4(value.X, value.Y, 0, 0))) return;
        _child!.SetFloat2(name, value);
    }

    private bool Unchanged(string name, Float4 value)
    {
        if (_child == null) return true;
        if (_values.TryGetValue(name, out Float4 bound) && bound == value) return true;
        _values[name] = value;
        return _child.Shader.FindProperty(name) < 0;
    }

    /// <summary>Frees the child once the frames using it finish.</summary>
    public void Release()
    {
        Graphics.DeferDispose(_child);
        _child = null;
        _parent = null;
        _textures.Clear();
        _values.Clear();
    }
}

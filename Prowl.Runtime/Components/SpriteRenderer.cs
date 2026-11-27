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

    public Sprite? Sprite { get => sprite; set { sprite = value; Wake(); } }
    [SerializeField] private Sprite? sprite;

    /// <summary>Tint multiplied into the sprite, given to the shader as the object colour.</summary>
    public Color Color { get => color; set { color = value; Wake(); } }
    [SerializeField] private Color color = Color.White;

    public bool FlipX { get => flipX; set { flipX = value; Wake(); } }
    [SerializeField] private bool flipX;
    public bool FlipY { get => flipY; set { flipY = value; Wake(); } }
    [SerializeField] private bool flipY;

    /// <summary>Optional material override. Defaults to the built-in unlit alpha-blended sprite material.</summary>
    public Material? Material { get => material; set { material = value; Wake(); } }
    [SerializeField] private Material? material;

    /// <summary>The layer it draws in. Later layers draw in front, whatever the render queue or depth.</summary>
    public SortingLayer SortingLayer { get => sortingLayer; set { sortingLayer = value; Wake(); } }
    [SerializeField] private SortingLayer sortingLayer;

    /// <summary>Higher values draw in front of lower ones in the same sorting layer. Clamped to -32768 to 32767.</summary>
    public int SortingOrder { get => sortingOrder; set { sortingOrder = value; Wake(); } }
    [SerializeField] private int sortingOrder;

    public ShadowCasting ShadowCasting { get => shadowCasting; set { shadowCasting = value; Wake(); } }
    [SerializeField] private ShadowCasting shadowCasting = ShadowCasting.Off;

    [System.NonSerialized] private Mesh? _mesh;
    [System.NonSerialized, NotHeld] private readonly RenderableState _renderable = new();
    [System.NonSerialized, NotHeld] private readonly OwnedMaterial _material = new();
    [System.NonSerialized, NotHeld] private readonly GMaterial[] _gpuMaterials = new GMaterial[1];
    [System.NonSerialized, NotHeld] private readonly HashSet<string> _secondaryBound = [];
    [System.NonSerialized, NotHeld] private readonly List<string> _secondaryStale = [];
    [System.NonSerialized] private uint _worldVersion, _transformVersion;

    // Snapshot of the inputs the baked mesh was built from, so we only rebuild when something changes.
    [System.NonSerialized] private Sprite? _bakedSprite;
    [System.NonSerialized] private int _bakedContent;
    [System.NonSerialized] private bool _bakedFlipX, _bakedFlipY;

    private static Material? s_defaultMaterial;

    /// <summary>The shared built-in unlit, alpha-blended, tinted sprite material.</summary>
    public static Material DefaultSpriteMaterial =>
        s_defaultMaterial.IsValid() ? s_defaultMaterial : (s_defaultMaterial = new Material(Shader.LoadDefault(DefaultShader.Sprite)));

    /// <summary>The renderable in the scene's world, invalid while nothing draws.</summary>
    public RenderableHandle Renderable => _renderable.Handle;

    // Syncs it again next frame, for a setting that changed
    internal void Wake() => SceneRendering.Wake(this, GameObject);

    internal TransparentOrder SortOrder() => SortingGroup.OrderOf(GameObject, SortingLayer, SortingOrder);

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
        BindSecondaryTextures(sprite);
        _gpuMaterials[0] = gpu;

        if (_worldVersion != Transform.WorldVersion)
        {
            _worldVersion = Transform.WorldVersion;
            _transformVersion++;
        }

        Color color = Color;
        _renderable.Sync(world, new RenderableState.Desc
        {
            Mesh = mesh.GpuIfLoaded,
            Materials = _gpuMaterials,
            Transform = (Double4x4)Transform.LocalToWorldMatrix,
            TransformVersion = _transformVersion,
            Flags = RenderableState.FlagsFor(GameObject, false, false) | ShadowCasting.ToFlags(),
            LayerMask = RenderableState.LayerBit(GameObject.LayerIndex),
            ObjectId = (uint)InstanceID,
            Sort = SortOrder(),
            Color = new Float4(color.R, color.G, color.B, color.A),
        });
        return material.IsLoaded && tex.IsLoaded;
    }

    void IRenderSync.Detach(RenderWorld world) => Detach(world);

    private void Detach(RenderWorld world)
    {
        _renderable.Detach(world);
        _material.Release();
        _secondaryBound.Clear();
    }

    // Binds the sprite's secondary textures and unbinds the ones it no longer has
    private void BindSecondaryTextures(Sprite sprite)
    {
        _secondaryStale.Clear();
        foreach (string name in _secondaryBound)
            if (!sprite.SecondaryTextures.TryGetValue(name, out Texture2D? texture) || texture.IsNotValid())
                _secondaryStale.Add(name);
        foreach (string name in _secondaryStale)
        {
            _material.ClearTexture(name);
            _secondaryBound.Remove(name);
        }
        foreach (KeyValuePair<string, Texture2D> secondary in sprite.SecondaryTextures)
        {
            if (secondary.Value.IsNotValid()) continue;
            _material.SetTexture(secondary.Key, secondary.Value);
            _secondaryBound.Add(secondary.Key);
        }
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
        for (int i = 0; i < n; i++)
        {
            verts[i] = new Float3(srcVerts[i].X * flipX, srcVerts[i].Y * flipY, 0f);
            uvs[i] = srcUV[i];
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
        _mesh.Indices = idx;
        _mesh.RecalculateBounds();
        _mesh.Upload();

        Snapshot(sprite);
    }

    private bool NeedsRebuild(Sprite sprite) =>
        _mesh.IsNotValid() ||
        !ReferenceEquals(_bakedSprite, sprite) || _bakedContent != sprite.ContentVersion ||
        _bakedFlipX != FlipX || _bakedFlipY != FlipY;

    private void Snapshot(Sprite sprite)
    {
        _bakedSprite = sprite;
        _bakedContent = sprite.ContentVersion;
        _bakedFlipX = FlipX; _bakedFlipY = FlipY;
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
/// Draws every renderer below it together: they sort as one item at this GameObject's position by the group's sorting
/// layer and order, then among themselves by their own. A group inside another sorts as one item among the outer's members.
/// </summary>
[AddComponentMenu("Rendering/Sorting Group")]
public class SortingGroup : Component
{
    /// <summary>The layer the group draws in, as one item.</summary>
    public SortingLayer SortingLayer { get => sortingLayer; set { sortingLayer = value; WakeMembers(); } }
    [SerializeField] private SortingLayer sortingLayer;

    /// <summary>The group's order in its layer. Clamped to -32768 to 32767.</summary>
    public int SortingOrder { get => sortingOrder; set { sortingOrder = value; WakeMembers(); } }
    [SerializeField] private int sortingOrder;

    [System.NonSerialized] private SortGroupLevel[]? _chain, _outerChain;
    [System.NonSerialized] private SortGroupLevel _level;

    public override void OnEnable() => WakeMembers();

    public override void OnDisable() => WakeMembers();

    /// <summary>Where a renderer on a GameObject draws among transparent ones: its own layer and order, inside every enabled group above it.</summary>
    public static TransparentOrder OrderOf(GameObject gameObject, SortingLayer layer, int order)
        => new(TagLayerManager.SortingOrderOf(layer.Id, order), ChainAt(gameObject));

    /// <summary>The nearest enabled group on the GameObject or above it, or null.</summary>
    public static SortingGroup? Nearest(GameObject? gameObject)
    {
        for (; gameObject.IsValid(); gameObject = gameObject.Parent)
            if (gameObject.GetComponent<SortingGroup>() is { } group && group.IsValid() && group.EnabledInHierarchy)
                return group;
        return null;
    }

    /// <summary>The chain of the nearest enabled group on the GameObject or above it, or null.</summary>
    public static SortGroupLevel[]? ChainAt(GameObject? gameObject)
    {
        SortingGroup? group = Nearest(gameObject);
        return group.IsValid() ? group.Chain() : null;
    }

    /// <summary>This group and every enabled group above it, outermost first. The same array while none of them changes.</summary>
    public SortGroupLevel[] Chain()
    {
        SortGroupLevel[]? outer = ChainAt(GameObject.Parent);
        Float3 anchor = Transform.Position;
        SortGroupLevel level = new(TagLayerManager.SortingOrderOf(sortingLayer.Id, sortingOrder), (uint)InstanceID, new Double3(anchor.X, anchor.Y, anchor.Z));
        if (_chain != null && level == _level && ReferenceEquals(outer, _outerChain))
            return _chain;
        _level = level;
        _outerChain = outer;
        _chain = outer == null ? [level] : [.. outer, level];
        return _chain;
    }

    private void WakeMembers()
    {
        if (GameObject.IsNotValid()) return;
        foreach (Component component in GameObject.GetComponentsInChildren<Component>())
            if (component is IRenderSync member)
                SceneRendering.Wake(member, component.GameObject);
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

    /// <summary>Drops the child's texture, so the parent's or the shader default draws.</summary>
    public void ClearTexture(string name)
    {
        if (_child == null || !_textures.Remove(name)) return;
        _child.ClearOverride(name);
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

// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Generic;

using Prowl.Runtime.Rendering;
using Prowl.Runtime.Resources;
using Prowl.Vector;

namespace Prowl.Runtime;

/// <summary>
/// Draws a <see cref="Sprite"/> in world space on the XY plane at its native size (pixels-per-unit). It uses
/// the sprite's own geometry, so a tight-mesh sprite renders tight. (Nine-slice / tiled / filled are UI
/// concerns handled by <c>UIImage</c>, not by world sprites.)
/// </summary>
[AddComponentMenu("Rendering/Sprite Renderer")]
[ComponentIcon("")] // Image
public class SpriteRenderer : MonoBehaviour
{
    /// <summary>Sorting-order spacing along Z (world units) so higher orders sort in front for the transparent queue.</summary>
    private const float SortBias = 0.0001f;

    public AssetRef<Sprite> Sprite;

    /// <summary>Tint multiplied into the sprite. Baked into vertex color.</summary>
    public Color Color = Color.White;

    public bool FlipX;
    public bool FlipY;

    /// <summary>Optional material override. Defaults to the built-in unlit alpha-blended sprite material.</summary>
    public AssetRef<Material> Material;

    /// <summary>Higher values render in front of lower ones (applied as a small Z bias for the transparent queue).</summary>
    public int SortingOrder;

    [System.NonSerialized] private Mesh? _mesh;
    [System.NonSerialized] private PropertyState? _props;

    // Snapshot of the inputs the baked mesh was built from, so we only rebuild when something changes.
    [System.NonSerialized] private Sprite? _bakedSprite;
    [System.NonSerialized] private bool _bakedFlipX, _bakedFlipY;
    [System.NonSerialized] private Color _bakedColor;

    private static Material? s_defaultMaterial;

    /// <summary>The shared built-in unlit, alpha-blended, tinted sprite material.</summary>
    public static Material DefaultSpriteMaterial =>
        s_defaultMaterial ??= new Material(Shader.LoadDefault(DefaultShader.Sprite));

    public override void OnRenderCollect(Camera camera, List<IRenderable> renderables, List<IRenderableLight> lights)
    {
        Sprite? sprite = Sprite.Res;
        if (sprite == null) return;

        Texture2D? tex = sprite.Texture.Res;
        if (tex == null || tex.Width == 0 || tex.Height == 0) return;
        if (sprite.Rect.Width <= 0 || sprite.Rect.Height <= 0) return;

        EnsureMesh(sprite, tex);
        if (_mesh == null) return;

        Material mat = Material.Res ?? DefaultSpriteMaterial;

        _props ??= new PropertyState();
        _props.Clear();
        _props.SetTexture("_MainTex", tex);

        // Bind any secondary maps (e.g. "_NormalMap", "_MaskMap") so custom sprite materials can sample them.
        if (sprite.SecondaryTextures.Count > 0)
        {
            foreach (var kv in sprite.SecondaryTextures)
            {
                Texture2D? secondary = kv.Value.Res;
                if (secondary != null)
                    _props.SetTexture(kv.Key, secondary);
            }
        }

        Float4x4 world = Transform.LocalToWorldMatrix;
        if (SortingOrder != 0)
            world *= Float4x4.CreateTranslation(new Float3(0, 0, SortingOrder * SortBias));

        renderables.Add(new MeshRenderable(_mesh, mat, world, GameObject.LayerIndex, _props));
    }

    private void EnsureMesh(Sprite sprite, Texture2D tex)
    {
        if (!NeedsRebuild(sprite)) return;

        Float2[] srcVerts = sprite.Vertices;
        Float2[] srcUV = sprite.UV;
        ushort[] srcIdx = sprite.Indices;
        int n = srcVerts.Length;
        if (n < 3 || srcIdx.Length < 3 || srcUV.Length != n)
            return; // nothing valid to draw; keep the previous mesh (if any)

        _mesh ??= new Mesh();

        // Flip mirrors the UVs within the sprite's atlas sub-rect.
        float u0 = sprite.Rect.X / (float)tex.Width, u1 = sprite.Rect.MaxX / (float)tex.Width;
        float v0 = sprite.Rect.Y / (float)tex.Height, v1 = sprite.Rect.MaxY / (float)tex.Height;

        var verts = new Float3[n];
        var uvs = new Float2[n];
        var colors = new Color32[n];
        Color32 tint = (Color32)Color;
        for (int i = 0; i < n; i++)
        {
            verts[i] = new Float3(srcVerts[i].X, srcVerts[i].Y, 0f);
            Float2 uv = srcUV[i];
            if (FlipX) uv.X = u0 + u1 - uv.X;
            if (FlipY) uv.Y = v0 + v1 - uv.Y;
            uvs[i] = uv;
            colors[i] = tint;
        }
        var idx = new uint[srcIdx.Length];
        for (int i = 0; i < srcIdx.Length; i++) idx[i] = srcIdx[i];

        _mesh.Vertices = verts; // sets first so length-matched arrays below validate
        _mesh.UV = uvs;
        _mesh.Colors32 = colors;
        _mesh.Indices = idx;
        _mesh.RecalculateBounds();
        _mesh.Upload();

        Snapshot(sprite);
    }

    private bool NeedsRebuild(Sprite sprite) =>
        _mesh == null ||
        !ReferenceEquals(_bakedSprite, sprite) ||
        _bakedFlipX != FlipX || _bakedFlipY != FlipY ||
        !_bakedColor.Equals(Color);

    private void Snapshot(Sprite sprite)
    {
        _bakedSprite = sprite;
        _bakedFlipX = FlipX; _bakedFlipY = FlipY;
        _bakedColor = Color;
    }

    public override void DrawGizmosSelected()
    {
        Sprite? sprite = Sprite.Res;
        if (sprite == null) return;

        float ppu = sprite.PixelsPerUnit > 0 ? sprite.PixelsPerUnit : 100f;
        float w = sprite.Rect.Width / ppu, h = sprite.Rect.Height / ppu;
        Float2 pivot = sprite.Pivot;
        float x0 = -pivot.X * w, y0 = -pivot.Y * h, x1 = (1f - pivot.X) * w, y1 = (1f - pivot.Y) * h;

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

    public override void OnDispose()
    {
        _mesh?.Dispose();
        _mesh = null;
    }
}

// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Echo;
using Prowl.Graphite.Rendering;
using Prowl.Runtime.Rendering;
using Prowl.Runtime.Resources;
using Prowl.Vector;

namespace Prowl.Runtime;

/// <summary>
/// Projects textures onto the surfaces inside a box: its <see cref="Size"/> around the object, Forward pointing out of
/// the surfaces it lands on like their normal. Albedo alpha is coverage, and surfaces turned away from Forward fade out.
/// </summary>
[AddComponentMenu("Rendering/Decal")]
[ComponentIcon("")] // PaintBrush
public sealed class Decal : Component, IRenderSync
{
    /// <summary>The box the decal fills: width along Right, height along Up and depth along Forward.</summary>
    public Float3 Size = new(1f, 1f, 0.5f);

    /// <summary>Albedo with alpha as coverage. Without one the tint covers the whole box.</summary>
    public Texture2D? Albedo;

    /// <summary>Tangent space normal map, none to leave the surface's normals alone.</summary>
    public Texture2D? Normal;

    /// <summary>Occlusion in R, roughness in G and metallic in B, none to leave them alone.</summary>
    public Texture2D? Mask;

    /// <summary>Tint, alpha scales coverage.</summary>
    public Color Color = Color.White;

    [Range(0f, 2f)]
    public float NormalStrength = 1f;

    /// <summary>Surfaces turned away from Forward fade out below this cosine to it, 0 for none.</summary>
    [Range(0f, 1f)]
    public float AngleFade = 0.2f;

    /// <summary>Higher draws over lower where decals overlap.</summary>
    public int Priority;

    /// <summary>The layers of the renderers it lands on.</summary>
    public LayerMask LayerMask = LayerMask.Everything;

    [System.NonSerialized, NotHeld] private DecalHandle _handle;
    [System.NonSerialized, NotHeld] private DecalDesc _last;
    [System.NonSerialized, NotHeld] private RenderWorld? _world;

    /// <summary>The decal as Graphite describes it, from the settings and the transform.</summary>
    public DecalDesc Describe()
    {
        Float3 size = new(Maths.Max(Size.X, 0.001f), Maths.Max(Size.Y, 0.001f), Maths.Max(Size.Z, 0.001f));
        var desc = new DecalDesc
        {
            Transform = (Double4x4)(Transform.LocalToWorldMatrix * Float4x4.CreateScale(size)),
            Albedo = Gpu(Albedo),
            Normal = Gpu(Normal),
            Mask = Gpu(Mask),
            Color = new Float4(Color.R, Color.G, Color.B, Color.A),
            NormalStrength = NormalStrength,
            AngleFade = Maths.Clamp(AngleFade, 0f, 1f),
            Priority = Priority,
            LayerMask = LayerMask.Mask,
            Static = GameObject.IsStatic,
        };
        Texture2D? sampled = Albedo.IsValid() ? Albedo : Normal.IsValid() ? Normal : Mask;
        if (sampled.IsValid() && sampled.IsLoaded) desc.Sampler = sampled.SamplerUnchecked;
        return desc;
    }

    // A texture's GPU copy once loaded, starting the load of one that has not
    private static Graphite.Texture? Gpu(Texture2D? texture)
    {
        if (texture.IsNotValid()) return null;
        if (texture.State == AssetState.Unloaded) _ = texture.LoadAsync();
        return texture.IsLoaded ? texture.GpuIfLoaded : null;
    }

    bool IRenderSync.Sync(RenderWorld world)
    {
        if (_world != null && _world != world) Remove();

        DecalDesc desc = Describe();
        if (!_handle.IsValid || !world.IsAlive(_handle))
        {
            _handle = world.AddDecal(desc);
            _world = world;
            _last = desc;
        }
        else if (!Same(desc, _last))
        {
            world.SetDecal(_handle, desc);
            _last = desc;
        }
        return GameObject.IsStatic && Ready(Albedo) && Ready(Normal) && Ready(Mask);
    }

    private static bool Ready(Texture2D? texture) => texture.IsNotValid() || texture.IsLoaded;

    void IRenderSync.Detach(RenderWorld world) => Remove();

    private void Remove()
    {
        if (_world != null && _handle.IsValid && _world.IsAlive(_handle))
            _world.RemoveDecal(_handle);
        _handle = default;
        _world = null;
    }

    private static bool Same(in DecalDesc a, in DecalDesc b)
        => a.Transform.Equals(b.Transform) && a.Albedo == b.Albedo && a.Normal == b.Normal && a.Mask == b.Mask
        && Equals(a.Sampler, b.Sampler) && a.Color == b.Color && a.NormalStrength == b.NormalStrength && a.AngleFade == b.AngleFade
        && a.Priority == b.Priority && a.LayerMask == b.LayerMask && a.Static == b.Static;

    public override void DrawGizmosSelected()
    {
        Float4x4 box = Transform.LocalToWorldMatrix * Float4x4.CreateScale(Size);
        Debug.PushMatrix(box);
        Debug.DrawWireCube(Float3.Zero, new Float3(0.5f), Color.Cyan);
        Debug.DrawArrow(Float3.Zero, Float3.UnitZ * 0.5f, Color.Cyan);
        Debug.PopMatrix();
    }
}

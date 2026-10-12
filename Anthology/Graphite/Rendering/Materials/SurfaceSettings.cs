using Prowl.Graphite.ShaderDef;

namespace Prowl.Graphite.Rendering;

public enum SurfaceType : byte
{
    Opaque,

    /// <summary>Opaque where the alpha test passes, discarded elsewhere.</summary>
    Cutout,

    /// <summary>Blended over what is behind it, drawn back to front after the opaques, without writing depth.</summary>
    Transparent,
}

/// <summary>How a transparent surface combines with what is behind it.</summary>
public enum SurfaceBlend : byte
{
    Alpha,

    /// <summary>The colour already multiplied by its alpha, so alpha 0 can still add light.</summary>
    Premultiplied,
    Additive,
    Multiply,
}

public enum SurfaceCull : byte
{
    Back,
    Front,

    /// <summary>Both sides drawn, back faces shaded with a flipped normal.</summary>
    Off,
}

/// <summary>
/// How a material's surface draws: its type, blend and culling. A material with settings derives from them its render
/// queue, its render state, whether it draws as transparent, and the _SURFACE axis of shaders that declare it, so every
/// shader of the standard family and any shader following the same axis agree on what Transparent means.
/// </summary>
public readonly record struct SurfaceSettings(SurfaceType Type = SurfaceType.Opaque, SurfaceBlend Blend = SurfaceBlend.Alpha, SurfaceCull Cull = SurfaceCull.Back)
{
    public const string SurfaceAxis = "_SURFACE";

    public const int OpaqueQueue = 2000;
    public const int CutoutQueue = 2450;
    public const int TransparentQueue = 3000;

    public int RenderQueue => Type switch
    {
        SurfaceType.Cutout => CutoutQueue,
        SurfaceType.Transparent => TransparentQueue,
        _ => OpaqueQueue,
    };

    /// <summary>The value of the _SURFACE axis.</summary>
    public string SurfaceKeyword => Type switch
    {
        SurfaceType.Cutout => "CUTOUT",
        SurfaceType.Transparent => "TRANSPARENT",
        _ => "OPAQUE",
    };

    /// <summary>The render state over every pass of the shader: culling, and blending without depth writes when transparent.</summary>
    public PassState State
    {
        get
        {
            PassState state = new() { Set = PassStateFields.CullMode };
            state.Raster.CullMode = Cull switch
            {
                SurfaceCull.Front => FaceCullMode.Front,
                SurfaceCull.Off => FaceCullMode.None,
                _ => FaceCullMode.Back,
            };
            if (Type != SurfaceType.Transparent)
                return state;

            state.Set |= PassStateFields.BlendEnabled | PassStateFields.SourceColorFactor | PassStateFields.DestinationColorFactor
                | PassStateFields.ColorFunction | PassStateFields.SourceAlphaFactor | PassStateFields.DestinationAlphaFactor
                | PassStateFields.AlphaFunction | PassStateFields.DepthWrite;
            state.DepthStencil.DepthWriteEnabled = false;
            state.Blend.BlendEnabled = true;
            state.Blend.ColorFunction = BlendFunction.Add;
            state.Blend.AlphaFunction = BlendFunction.Add;
            (state.Blend.SourceColorFactor, state.Blend.DestinationColorFactor) = Blend switch
            {
                SurfaceBlend.Premultiplied => (BlendFactor.One, BlendFactor.InverseSourceAlpha),
                SurfaceBlend.Additive => (BlendFactor.SourceAlpha, BlendFactor.One),
                SurfaceBlend.Multiply => (BlendFactor.DestinationColor, BlendFactor.Zero),
                _ => (BlendFactor.SourceAlpha, BlendFactor.InverseSourceAlpha),
            };
            (state.Blend.SourceAlphaFactor, state.Blend.DestinationAlphaFactor) = Blend switch
            {
                SurfaceBlend.Additive => (BlendFactor.Zero, BlendFactor.One),
                SurfaceBlend.Multiply => (BlendFactor.Zero, BlendFactor.One),
                _ => (BlendFactor.One, BlendFactor.InverseSourceAlpha),
            };
            return state;
        }
    }
}

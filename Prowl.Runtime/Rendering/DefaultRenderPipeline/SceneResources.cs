// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Graphite;
using Prowl.Graphite.RenderGraph;

namespace Prowl.Runtime.Rendering;

/// <summary>Graph resource ids shared by the default pipeline's passes.</summary>
internal static class SceneResources
{
    public const string SceneColor   = "_SceneColor";
    public const string SceneDepth   = "_SceneDepth";
    public const string SceneNormals = "_SceneNormals";
    public const string SceneMotion  = "_SceneMotion";
    public const string ShadowAtlas  = "_ShadowAtlas";
    public const string Final        = "_FinalColor";

    public const string GBuffer   = "_SceneGBuffer";
    public const string DepthCopy = "_SceneDepthCopy";

    /// <summary>View-sized MRT backing SceneColor, SceneNormals, SceneMotion and SceneDepth.</summary>
    public static GraphTextureDesc GBufferDesc()
        => GraphTextureDesc.ViewSized(depth: true, 1f,
            PixelFormat.R16_G16_B16_A16_Float,
            PixelFormat.R8_G8_B8_A8_UNorm,
            PixelFormat.R16_G16_B16_A16_Float);

    /// <summary>Depth-only, view-sized texture the gizmo and grid shaders sample.</summary>
    public static GraphTextureDesc DepthCopyDesc()
        => new()
        {
            SizeMode = TextureSizeMode.ViewRelative,
            Scale = 1f,
            ColorFormats = System.Array.Empty<PixelFormat>(),
            EnableDepth = true,
        };

    /// <summary>View-sized RGBA8 target the present pass reads.</summary>
    public static GraphTextureDesc FinalDesc()
        => GraphTextureDesc.ViewSized(depth: false, 1f, PixelFormat.R8_G8_B8_A8_UNorm);
}

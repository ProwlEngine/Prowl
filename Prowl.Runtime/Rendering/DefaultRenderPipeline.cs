// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Vector;


namespace Prowl.Runtime.Rendering;


public struct ViewerData
{
    public Float3 Position;
    public Float3 Forward;
    public Float3 Up;
    public Float3 Right;

    // Camera projection data, used by screen-space and world-space UI canvases.
    public uint PixelWidth;
    public uint PixelHeight;
    public Float4x4 ViewMatrix;
    public Float4x4 ProjectionMatrix;

    public ViewerData(RenderPipeline.CameraSnapshot css) : this()
    {
        Position = css.CameraPosition;
        Forward = css.CameraForward;
        Up = css.CameraUp;
        Right = css.CameraRight;
        PixelWidth = css.PixelWidth;
        PixelHeight = css.PixelHeight;
        ViewMatrix = css.View;
        ProjectionMatrix = css.Projection;
    }

    public ViewerData(Float3 position, Float3 forward, Float3 right, Float3 up) : this()
    {
        Position = position;
        Forward = forward;
        Right = right;
        Up = up;
    }
}


/// <summary>
/// Default graph-driven pipeline. Sets up the standard pass chain (shadows -> opaque -> transparents
/// -> volumetrics -> post-processing); the passes are empty scaffolding that copy textures along the
/// chain to prove the graph plumbing, draw the skybox in the opaque pass, and (in the editor) draw
/// gizmos and the grid. Camera/Scene/editor code falls back to this when a camera has no custom
/// Pipeline set.
/// </summary>
public class DefaultRenderPipeline : RenderPipeline<DrawCommand>
{
    public static DefaultRenderPipeline Default { get; } = new();

    protected override void InitializePasses()
    {
        Culler = new DefaultRenderCuller();

        AddPass(new ShadowsPass());
        AddPass(new OpaquePass());
        AddPass(new TransparentsPass());
        AddPass(new VolumetricsPass());
        AddPass(new PostProcessingPass());
    }
}

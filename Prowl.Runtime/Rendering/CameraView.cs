// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Graphite;
using Prowl.Graphite.RenderGraph;

using Prowl.Runtime.Resources;
using Prowl.Vector;

using RenderTexture = Prowl.Runtime.Resources.RenderTexture;

namespace Prowl.Runtime.Rendering;

public struct RenderingData
{
    /// <summary>Whether to draw gizmos (editor scene view).</summary>
    public bool DisplayGizmos;

    /// <summary>Whether to draw the editor grid.</summary>
    public bool DisplayGrid;

    /// <summary>Whether the render is happening from the Scene View.</summary>
    public bool IsSceneView;

    public bool SkipUI;
}

public sealed class CameraView : IRenderView
{
    /// <summary>The camera being rendered.</summary>
    public Camera Camera;

    public int ViewId => Camera.InstanceID;

    /// <summary>Per-frame render flags (gizmos, grid, scene-view, etc.) for this camera.</summary>
    public RenderingData Data;

    /// <summary>
    /// Where the pipeline's default presenter (<see cref="DefaultPresentPass"/>) blits its final content.
    /// Null presents to the swapchain instead.
    /// </summary>
    public RenderTexture? Target;

    /// <summary>
    /// A sampleable copy of the opaque pass's depth buffer, taken after opaque geometry is drawn and
    /// before its source depth attachment is written to again. Debug overlays (gizmos, grid) that need
    /// to depth-test against the scene sample this instead of the live depth attachment, since a texture
    /// can't be bound as a framebuffer's depth target and a shader resource at the same time.
    /// </summary>
    public Texture2D? SceneDepthCopy;

    /// <summary>
    /// The shader-side <c>Frame</c> parameter block (see ShaderVariables.slang) for this camera: view/projection
    /// matrices and their inverses, camera position and projection/screen params, merged with the frame-global
    /// <see cref="GlobalUniforms"/>. Built once in <see cref="From"/>; every pass that records camera-relative
    /// draws binds it with <c>cmd.SetProperties</c>.
    /// </summary>
    public PropertySet FrameProperties = new();

    public uint PixelWidth { get; set; }
    public uint PixelHeight { get; set; }

    /// <summary>Identifies this view to the profiler - the owning camera's GameObject name.</summary>
    public string Name { get; set; } = "";

    /// <summary>Builds the view for one camera's render: refreshes its per-frame pixel/projection data
    /// (<see cref="Camera.UpdateRenderData"/>) and resolves its target.</summary>
    public static CameraView From(Camera camera, in RenderingData data)
    {
        RenderTexture? target = camera.UpdateRenderData();

        var view = new CameraView
        {
            Camera = camera,
            Data = data,
            Target = target,
            PixelWidth = camera.PixelWidth,
            PixelHeight = camera.PixelHeight,
            Name = camera.GameObject.Name,
        };
        view.BuildFrameProperties();
        return view;
    }

    private void BuildFrameProperties()
    {
        PropertySet props = FrameProperties;
        Float4x4 v = Camera.ViewMatrix;
        Float4x4 p = Camera.ProjectionMatrix;
        Float4x4 vp = p * v;
        Float4x4 nonJitteredVP = Camera.NonJitteredProjectionMatrix * v;

        props.SetMatrix("prowl_MatV", v);
        props.SetMatrix("prowl_MatIV", v.Invert());
        props.SetMatrix("prowl_MatP", p);
        props.SetMatrix("prowl_MatVP", vp);
        props.SetMatrix("prowl_MatIP", p.Invert());
        props.SetMatrix("prowl_MatIVP", vp.Invert());
        props.SetMatrix("prowl_MatVP_NonJittered", nonJitteredVP);
        props.SetMatrix("prowl_PrevViewProj", Camera.HasPreviousViewProjectionMatrix ? Camera.PreviousViewProjectionMatrix : nonJitteredVP);

        props.SetFloat3("_WorldSpaceCameraPos", Camera.Transform.Position);
        props.SetFloat4("_ProjectionParams", new Float4(1.0f, Camera.NearClipPlane, Camera.FarClipPlane, 1.0f / Camera.FarClipPlane));
        props.SetFloat4("_ScreenParams", new Float4(PixelWidth, PixelHeight, 1.0f + 1.0f / PixelWidth, 1.0f + 1.0f / PixelHeight));
        props.SetFloat2("_CameraJitter", Float2.Zero);
        props.SetFloat2("_CameraPreviousJitter", Float2.Zero);

        GlobalUniforms.Update();
        props.ApplyOther(GlobalUniforms.Properties);
    }
}

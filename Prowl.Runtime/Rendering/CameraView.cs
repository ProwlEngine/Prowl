// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Graphite.RenderGraph;

using Prowl.Runtime.Resources;

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

    public uint PixelWidth { get; set; }
    public uint PixelHeight { get; set; }

    /// <summary>Builds the view for one camera's render: refreshes its per-frame pixel/projection data
    /// (<see cref="Camera.UpdateRenderData"/>) and resolves its target.</summary>
    public static CameraView From(Camera camera, in RenderingData data)
    {
        RenderTexture? target = camera.UpdateRenderData();

        return new CameraView
        {
            Camera = camera,
            Data = data,
            Target = target,
            PixelWidth = camera.PixelWidth,
            PixelHeight = camera.PixelHeight,
        };
    }
}

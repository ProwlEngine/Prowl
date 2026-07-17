// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Runtime.Resources;

namespace Prowl.Runtime.Rendering;

/// <summary>
/// Provides access to rendering resources for image effects.
/// Passed to effects during rendering to give access to all necessary buffers and state.
/// </summary>
public sealed class RenderContext : IDisposable
{
    /// <summary>Unified prepass. InternalDepth = depth, InternalTextures[0] = view-space normals,
    /// InternalTextures[1] = motion (.rg) + roughness (.b) + metallic (.a).</summary>
    public RenderTexture DepthNormals { get; set; }

    /// <summary>Per-pixel screen-space motion vectors in .rg (roughness/metallic packed in .ba).
    /// This is attachment 1 of the unified prepass and is always produced.</summary>
    public Texture2D MotionVectors { get; set; }

    /// <summary>The scene color buffer (forward-rendered opaques + skybox).</summary>
    public RenderTexture SceneColor { get; set; }

    public Camera Camera { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }

    public RenderStage CurrentStage { get; set; }

    private readonly List<RenderTexture> _replacedRTs = new();

    /// <summary>
    /// Replaces the scene color buffer with a new one (e.g., for HDR to LDR conversion).
    /// Only allowed during PostProcess stage.
    /// </summary>
    public RenderTexture ReplaceSceneColor(RenderTexture newBuffer)
    {
        if (CurrentStage != RenderStage.PostProcess)
            throw new InvalidOperationException("ReplaceSceneColor can only be called during PostProcess stage");

        if (SceneColor != null && !_replacedRTs.Contains(SceneColor))
            _replacedRTs.Add(SceneColor);

        SceneColor = newBuffer;
        return newBuffer;
    }

    internal List<RenderTexture> GetReplacedRTs() => _replacedRTs;

    public void Dispose()
    {
        _replacedRTs.Clear();
    }
}

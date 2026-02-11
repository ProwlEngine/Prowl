// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Runtime.Resources;

namespace Prowl.Runtime.Rendering;

/// <summary>
/// Provides access to rendering resources and targets for image effects.
/// This context is passed to effects during rendering and gives them access to
/// all necessary buffers, textures, and rendering state.
/// </summary>
public sealed class RenderContext : IDisposable
{
    // Core rendering targets
    public RenderTexture GBuffer { get; set; }            // Contains all GBuffer attachments (Albedo, Normal, PBR, Custom) + Depth
    public RenderTexture LightAccumulation { get; set; }  // Light accumulation buffer (before albedo multiply)
    public RenderTexture SceneColor { get; set; }         // Final composed color (albedo * lighting)

    // Camera info
    public Camera Camera { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }

    // Current rendering stage
    public RenderStage CurrentStage { get; set; }

    private readonly List<RenderTexture> _replacedRTs = new();

    /// <summary>
    /// Replaces the scene color buffer with a new one (e.g., for HDR to LDR conversion).
    /// Only allowed during PostProcess stage.
    /// The old buffer is tracked for cleanup by the pipeline. Returns the new buffer.
    /// </summary>
    public RenderTexture ReplaceSceneColor(RenderTexture newBuffer)
    {
        if (CurrentStage != RenderStage.PostProcess)
            throw new InvalidOperationException("ReplaceSceneColor can only be called during PostProcess stage");

        if (SceneColor != null && !_replacedRTs.Contains(SceneColor))
        {
            _replacedRTs.Add(SceneColor);
        }
        SceneColor = newBuffer;
        return newBuffer;
    }

    /// <summary>
    /// Gets the list of replaced render targets that need cleanup by the pipeline.
    /// </summary>
    internal List<RenderTexture> GetReplacedRTs() => _replacedRTs;

    public void Dispose()
    {
        // Note: Replaced RTs are NOT disposed here - the pipeline handles those
        _replacedRTs.Clear();
    }
}

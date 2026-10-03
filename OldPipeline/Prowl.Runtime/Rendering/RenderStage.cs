// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Runtime.Rendering;

/// <summary>
/// Defines the stages in the forward rendering pipeline where image effects can be injected.
/// </summary>
public enum RenderStage
{
    /// <summary>
    /// Runs after opaque rendering but before transparent objects.
    /// Has access to depth, normals, and the opaque scene color.
    /// Perfect for SSR, GTAO, screen-space effects that need opaque geometry.
    /// </summary>
    AfterOpaques,

    /// <summary>
    /// Runs after transparent objects, before world space UI and post-processing. Still HDR scene color.
    /// For effects that should cover everything in the world, like volumetric fog.
    /// </summary>
    AfterTransparents,

    /// <summary>
    /// Runs after all rendering (including transparents) as final post-processing.
    /// Perfect for tonemapping, color grading, bloom, DOF, FXAA, etc.
    /// </summary>
    PostProcess
}

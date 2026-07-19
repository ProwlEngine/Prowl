// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Generic;

namespace Prowl.Runtime.Rendering;

/// <summary>Ordering applied to a set of draw commands relative to the camera.</summary>
public enum SortMode
{
    /// <summary>No distance sort; commands keep collection order.</summary>
    None,

    /// <summary>Nearest first. Optimal for opaques (early-Z rejection).</summary>
    FrontToBack,

    /// <summary>Farthest first. Required for correct transparent alpha blending.</summary>
    BackToFront
}

/// <summary>
/// A filter + sort request handed to <see cref="IRenderCuller{TDrawCommand}.GetDrawCommands"/> to
/// pull a specific slice of the culled scene (e.g. the opaque, shadow-caster, or transparent draws).
/// </summary>
public struct DrawCommandQuery
{
    /// <summary>Shader tag key to match (e.g. "RenderOrder"). Null/empty matches every command.</summary>
    public string Tag;

    /// <summary>Shader tag value to match (e.g. "Opaque", "Transparent", "ShadowCaster").</summary>
    public string TagValue;

    /// <summary>How the returned commands are ordered relative to the camera.</summary>
    public SortMode Sort;

    /// <summary>Optional layer filter; when set, only commands on these layers are returned.</summary>
    public LayerMask? LayerMask;
}

/// <summary>
/// Turns the scene's <see cref="IRenderable"/>/<see cref="IRenderableLight"/> into pipeline-specific
/// draw commands. The pipeline pipes the collected scene in via <see cref="Cull"/> once per camera;
/// passes then pull the slices they need via <see cref="GetDrawCommands"/>. The draw-command type is
/// opaque to the framework so each pipeline can define whatever payload its passes consume.
/// </summary>
public interface IRenderCuller<TDrawCommand>
{
    /// <summary>
    /// Ingests the scene for one camera: frustum/layer culls, sorts/indexes, and builds the draw
    /// commands the queries below will surface. Called once per camera before the passes run.
    /// </summary>
    void Cull(in RenderPipeline.CameraSnapshot camera, IReadOnlyList<IRenderable> renderables, IReadOnlyList<IRenderableLight> lights);

    /// <summary>Returns the draw commands matching <paramref name="query"/> from the last <see cref="Cull"/>.</summary>
    IReadOnlyList<TDrawCommand> GetDrawCommands(in DrawCommandQuery query);

    /// <summary>Number of renderables ingested by the last <see cref="Cull"/>.</summary>
    int RenderablesCollected { get; }

    /// <summary>Number of ingested renderables the last <see cref="Cull"/> culled away.</summary>
    int RenderablesCulled { get; }

    /// <summary>Number of ingested renderables that survived the last <see cref="Cull"/>.</summary>
    int RenderablesVisible { get; }
}

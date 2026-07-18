// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Runtime.Rendering;

/// <summary>
/// Non-generic pass surface the graph solver uses to gather resource declarations without knowing
/// the pipeline's draw-command type. Implementers should implement <see cref="IPass{TDrawCommand}"/>.
/// </summary>
public interface IPass
{
    /// <summary>Human-readable name, used for command-buffer labels and diagnostics.</summary>
    string Name { get; }

    /// <summary>Declares the textures this pass reads and writes on the given builder.</summary>
    void SetupInputs(RenderContextBuilder builder);
}

/// <summary>
/// A single unit of work in a <see cref="RenderPipeline{TDrawCommand}"/>. A pass declares
/// its texture inputs/outputs once (via <see cref="IPass.SetupInputs"/>) so the graph can order it,
/// then does its rendering in <see cref="Render"/> against the resolved resources.
/// </summary>
public interface IPass<TDrawCommand> : IPass
{
    /// <summary>Records this pass's rendering. Resolve declared handles with <c>context.GetTexture</c>.</summary>
    void Render(RenderContext<TDrawCommand> context);
}

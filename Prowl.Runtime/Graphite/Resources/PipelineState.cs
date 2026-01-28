// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Runtime.Graphite;

/// <summary>
/// A pre-baked graphics pipeline state object containing all render state.
/// Pipeline states are immutable after creation - create different pipelines for different state combinations.
/// </summary>
public abstract class PipelineState : GraphiteResource
{
    /// <summary>The primitive topology this pipeline uses.</summary>
    public PrimitiveTopology Topology { get; protected set; }
}

/// <summary>
/// A pre-baked compute pipeline state object.
/// </summary>
public abstract class ComputePipelineState : GraphiteResource
{
}

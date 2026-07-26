using System.Collections.Generic;

namespace Prowl.Runtime.Rendering;

/// <summary>
/// Pushed via <see cref="Prowl.Graphite.CommandBuffer.RecordMetadata"/> right after a shader bind -
/// the pass/variant/material identity Graphite's own <c>PipelineBindInfo</c> doesn't carry, for a
/// profiler that wants to attribute a pipeline switch to a specific engine-side shader pass/material.
/// </summary>
public readonly struct ShaderBindMetadata
{
    public string PassName { get; init; }
    public string Variant { get; init; }
    public IReadOnlyDictionary<string, string>? Tags { get; init; }
    public string MaterialName { get; init; }
}

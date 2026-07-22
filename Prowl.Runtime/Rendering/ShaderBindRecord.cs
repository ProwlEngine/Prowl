using System.Collections.Generic;

namespace Prowl.Runtime.Rendering;

/// <summary>
/// Runtime shader bind data record to pipe additional metadata for a shader bind to a profiler.
/// </summary>
public readonly struct ShaderBindRecord
{
    public string PassName { get; init; }
    public string Variant { get; init; }
    public IReadOnlyDictionary<string, string>? Tags { get; init; }
    public string MaterialName { get; init; }
}

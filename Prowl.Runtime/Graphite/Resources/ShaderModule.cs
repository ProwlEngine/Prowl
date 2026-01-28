// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Runtime.Graphite;

/// <summary>
/// A compiled shader module for a single shader stage.
/// Shader modules are immutable after creation.
/// </summary>
public abstract class ShaderModule : GraphiteResource
{
    /// <summary>The shader stage this module is for.</summary>
    public ShaderStage Stage { get; protected set; }

    /// <summary>The entry point function name.</summary>
    public string EntryPoint { get; protected set; } = "main";
}

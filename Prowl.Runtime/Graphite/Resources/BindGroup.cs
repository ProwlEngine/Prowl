// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Runtime.Graphite;

/// <summary>
/// Describes the layout of resource bindings in a bind group.
/// Bind group layouts are immutable after creation and can be shared across multiple bind groups.
/// </summary>
public abstract class BindGroupLayout : GraphiteResource
{
}

/// <summary>
/// A collection of resources bound together for use in shaders.
/// Bind groups are immutable after creation - create new bind groups for different resource combinations.
/// </summary>
public abstract class BindGroup : GraphiteResource
{
    /// <summary>The layout this bind group conforms to.</summary>
    public BindGroupLayout Layout { get; protected set; } = null!;
}

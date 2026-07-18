// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Runtime.Rendering;

/// <summary>
/// Opaque reference a pass holds onto a graph texture resource. Obtained from
/// <see cref="RenderContextBuilder"/> during setup and resolved to a concrete <c>RenderTexture</c>
/// via <see cref="RenderContext{TDrawCommand}.GetTexture"/> during rendering.
/// </summary>
public readonly struct TextureHandle
{
    /// <summary>The graph resource this handle refers to.</summary>
    public readonly RenderResourceID Id;

    internal TextureHandle(RenderResourceID id) => Id = id;

    /// <summary>False for a <c>default</c> handle that was never obtained from the builder.</summary>
    public bool IsValid => Id.IsValid;
}

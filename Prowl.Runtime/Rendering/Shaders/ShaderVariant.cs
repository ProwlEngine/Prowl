// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Graphite;
using Prowl.Graphite.Variants;

namespace Prowl.Runtime.Rendering.Shaders;

/// <summary>
/// One compiled variant of a <see cref="ShaderPass"/>: the fixed keyword set it was compiled with
/// plus the per-backend reflected program descriptions. Serialized as part of the pass.
/// </summary>
public struct ShaderVariant
{
    public Keyword[] Keywords;
    public (ShaderDescription, GraphicsBackend)[] Backends;
}
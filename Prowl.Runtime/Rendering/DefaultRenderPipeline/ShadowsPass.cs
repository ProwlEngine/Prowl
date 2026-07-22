// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Runtime.Rendering;

/// <summary>First pass in the chain. Empty for now; produces the resource the opaque pass reads.</summary>
public sealed class ShadowsPass : CopyChainPass
{
    public ShadowsPass() : base("Shadows", DefaultChain.Shadows, present: false) { }
}

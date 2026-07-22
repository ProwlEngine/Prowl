// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Runtime.Rendering;

/// <summary>
/// Final pass in the chain. Copies the volumetrics chain into its output and marks it as the main
/// output, so the pipeline presents it to the camera target.
/// </summary>
public sealed class PostProcessingPass : CopyChainPass
{
    public PostProcessingPass() : base("PostProcessing", DefaultChain.Final, present: true, inputId: DefaultChain.Volumetrics) { }
}

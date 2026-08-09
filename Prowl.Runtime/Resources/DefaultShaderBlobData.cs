// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Graphite.ShaderDef;

namespace Prowl.Runtime.Resources;

// Shared between Prowl.Runtime and the Tools/DefaultShaderCompiler tool

/// <summary>A precompiled default shader: the parsed definition plus whichever variants were
/// compiled ahead of time (Vulkan only - see Tools/DefaultShaderCompiler).</summary>
public struct DefaultShaderBlobData
{
    public ShaderDefinition Definition;
    public ShaderSnapshot Snapshot;
}

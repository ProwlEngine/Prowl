// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Graphite.ShaderDef;

namespace Prowl.Runtime.GUI;

// Shared between Prowl.Runtime and the Tools/CompileUIShaders tool

/// <summary>A baked single-pass GUI shader: the parsed definition plus whichever variants were
/// compiled ahead of time (Vulkan only - see Tools/CompileUIShaders).</summary>
public struct UIShaderBlobData
{
    public ShaderDefinition Definition;
    public ShaderSnapshot Snapshot;
}

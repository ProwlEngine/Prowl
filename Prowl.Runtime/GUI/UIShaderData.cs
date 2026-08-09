// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Graphite.ShaderDef;

namespace Prowl.Runtime.GUI;

// Field-for-field identical to Tools/UIShaderCompiler.cs's own UIShaderBlobData. Duplicated rather than
// shared, so the tool has no project reference back to Prowl.Runtime (it compiles the blobs this
// assembly embeds as resources - a real reference would be a build-order cycle). Echo's TypeMode.None
// serializes by field name only, so the two independently-defined types round-trip through each other.

/// <summary>A baked single-pass GUI shader: the parsed definition plus whichever variants were
/// compiled ahead of time (Vulkan only - see Tools/UIShaderCompiler.cs).</summary>
public struct UIShaderBlobData
{
    public ShaderDefinition Definition;
    public ShaderSnapshot Snapshot;
}

// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Runtime;

/// <summary>
/// Identifies every kind of command that can be recorded into a <see cref="CommandBuffer"/>.
///
/// The executor switches on this value to decide how to decode the payload that follows
/// the header. Adding a new opcode requires:
///   1. An entry here.
///   2. A writer method on <see cref="CommandBuffer"/>.
///   3. A case in <see cref="CommandExecutor.Execute"/>.
///
/// Numbering is stable for the lifetime of the process; the wire format is in-memory only,
/// so changing values doesn't break anything as long as encoder and executor agree.
/// </summary>
internal enum CommandOpcode : ushort
{
    // Render target / viewport / clear
    SetRenderTarget = 1,
    SetRenderTargets,
    SetViewport,
    ClearRenderTarget,
    BlitFramebuffer,

    // Pipeline state
    SetRasterState,
    SetShader,

    // Property binding (sticky on the executor)
    SetProperties,
    SetMaterialProperties,
    ClearProperties,
    SetInstanceProperties,
    ClearInstanceProperties,

    // Global property mutation, ordered against draws (the static PropertyState
    // dictionaries are mutated at EXECUTE time so mid-CB set/clear cycles work
    // AND so writes from main-thread setters can't race with render-thread reads).
    SetGlobalTexture,
    ClearGlobalTexture,
    SetGlobalInt,
    SetGlobalFloat,
    SetGlobalVec2,
    SetGlobalVec3,
    SetGlobalVec4,
    SetGlobalColor,
    SetGlobalMatrix,
    SetGlobalMatrices,
    SetGlobalBuffer,
    SetGlobalTexture3D,
    ClearAllGlobals,

    // Per-uniform sugar (applied immediately against the bound shader)
    SetUniformFloat,
    SetUniformInt,
    SetUniformVec2,
    SetUniformVec3,
    SetUniformVec4,
    SetUniformMatrix,
    SetUniformMatrixArray,
    SetUniformTexture,
    SetUniformBuffer,

    // Resource data uploads
    UpdateBuffer,
    UpdateTexture,
    GenerateMipmap,

    // Draws
    DrawIndexed,
    DrawIndexedInstanced,
    DrawArrays,

    // (DrawMesh / DrawMeshInstanced / Blit are encoder sugar on CommandBuffer that
    //  expand inline to lower-level opcodes there's no executor case for them.)

    // Resource lifecycle. Resource constructors encode these into a tiny CB and
    // submit so all GL state mutation routes through the executor. The CPU wrapper
    // is allocated synchronously the GL Handle field is filled in when the opcode
    // executes (immediate under Step 1's sync Submit; deferred under Step 2's render
    // thread). Order is preserved by Submit ordering so subsequent uses of the
    // resource always see a valid handle.
    CreateBuffer,
    DisposeBuffer,
    CreateTexture,
    AllocateTexture2D,
    AllocateTexture3D,
    UpdateTexture3D,
    SetTextureWrap,
    SetTextureFiltersOp,
    GetTextureData,
    GetTextureDataPtr,
    DisposeTexture,
    CreateVertexArrayOp,
    DisposeVertexArray,
    CreateFramebufferOp,
    DisposeFramebuffer,
    CompileShader,
    DisposeShader,

    // Debug markers
    BeginSample,
    EndSample,

    // Render-thread stopwatch timers
    BeginTimer,
    EndTimer,
}

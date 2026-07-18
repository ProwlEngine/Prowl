// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

namespace Prowl.Runtime.Rendering;

/// <summary>
/// Handed to a pass's <see cref="IPass.SetupInputs"/> so it can declare the textures it reads and
/// writes. The graph reads these declarations to allocate resources and to order passes: a pass
/// that reads a resource runs after the pass(es) that write it. A pass may optionally nominate one of
/// its outputs as its <see cref="SetMainOutput">main output</see>; the definition selects which pass's
/// main output is presented to the camera target.
/// </summary>
public sealed class RenderContextBuilder
{
    internal readonly struct ResourceDecl(RenderResourceID id, RenderTextureDesc desc)
    {
        public readonly RenderResourceID Id = id;
        public readonly RenderTextureDesc Desc = desc;
    }

    internal readonly List<ResourceDecl> Inputs = new();
    internal readonly List<ResourceDecl> Outputs = new();
    internal RenderResourceID MainOutput;
    internal bool HasMainOutput;

    internal void Reset()
    {
        Inputs.Clear();
        Outputs.Clear();
        MainOutput = default;
        HasMainOutput = false;
    }

    /// <summary>Declares a texture this pass samples. Creates the resource if no other pass declared it.</summary>
    public TextureHandle GetInputTexture(RenderResourceID id, RenderTextureDesc desc)
    {
        Inputs.Add(new ResourceDecl(id, desc));
        return new TextureHandle(id);
    }

    /// <summary>Declares a texture this pass renders into. Creates the resource if no other pass declared it.</summary>
    public TextureHandle GetOutputTexture(RenderResourceID id, RenderTextureDesc desc)
    {
        Outputs.Add(new ResourceDecl(id, desc));
        return new TextureHandle(id);
    }

    /// <summary>
    /// Optionally nominates one of this pass's outputs as its primary result. If the definition
    /// selects this pass as its output pass, this is what the pipeline blits to the camera target,
    /// so passes never need to know the real target.
    /// </summary>
    public void SetMainOutput(TextureHandle handle)
    {
        if (!handle.IsValid)
            throw new ArgumentException("Main output must be a valid output texture handle.", nameof(handle));

        MainOutput = handle.Id;
        HasMainOutput = true;
    }
}

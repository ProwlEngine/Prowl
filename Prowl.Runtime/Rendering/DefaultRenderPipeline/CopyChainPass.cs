// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Graphite;
using Prowl.Graphite.RenderGraph;
using Prowl.Vector;

using RenderTexture = Prowl.Graphite.RenderTexture;

namespace Prowl.Runtime.Rendering;

/// <summary>
/// Shared resource ids for the default pipeline's pass chain. Each pass reads the previous pass's
/// output and writes its own, so these names form the edges of the dependency graph.
/// </summary>
internal static class DefaultChain
{
    public const string Shadows = "_ShadowsChain";
    public const string Opaque = "_OpaqueChain";
    public const string Transparents = "_TransparentsChain";
    public const string Volumetrics = "_VolumetricsChain";
    public const string Final = "_FinalChain";
}

/// <summary>
/// Base for the default pipeline's passes. Declares an optional input and one output, copies the
/// input into the output when present (the chain hand-off, a raw texture copy - same size/format by
/// construction), then invokes <see cref="OnRender"/> for any extra pass-specific drawing.
/// </summary>
public abstract class CopyChainPass : IPass<CameraView>
{
    private readonly RenderResourceID _inputId;
    private readonly RenderResourceID _outputId;
    private readonly bool _hasInput;

    private TextureHandle _inputHandle;
    private TextureHandle _outputHandle;

    protected CopyChainPass(string name, RenderResourceID outputId, bool present, RenderResourceID inputId = default)
    {
        Name = name;
        _outputId = outputId;
        _inputId = inputId;
        _hasInput = inputId.IsValid;
        // 'present' no longer selects a graph-level main output - Graphite presents through a
        // dedicated IPresentPass instead (BlitPresentPass reads DefaultChain.Final directly). Kept as
        // a ctor param so pass definitions stay self-documenting about which one is the chain's end.
    }

    public string Name { get; }

    public void Setup(RenderContextBuilder builder)
    {
        GraphTextureDesc desc = GraphTextureDesc.ViewSized(depth: true);

        if (_hasInput)
            _inputHandle = builder.GetInputTexture(_inputId);

        _outputHandle = builder.GetOutputTexture(_outputId, desc);
    }

    public void Render(RenderContext<CameraView> context)
    {
        RenderTexture output = context.GetRenderTexture(_outputHandle);
        CommandBuffer cmd = context.GetCommandBuffer(Name);

        // Copy before binding the destination as a framebuffer, so the copy never targets an
        // attachment that's simultaneously bound as an active render target.
        if (_hasInput)
        {
            RenderTexture input = context.GetRenderTexture(_inputHandle);
            cmd.CopyTexture(input.ColorTextures[0], output.ColorTextures[0]);
        }

        cmd.SetFramebuffer(output.Framebuffer);

        if (!_hasInput)
            cmd.ClearColorTarget(0, new Color(0f, 0f, 0f, 1f));

        // Depth is left as-is here (undefined for a fresh transient target); OpaquePass explicitly
        // clears it once opaque geometry is about to depth-test, matching the old pipeline's behavior.

        OnRender(context, cmd, output);

        context.SubmitCommandBuffer(cmd);
    }

    /// <summary>Extra drawing after the chain copy. The output framebuffer is already bound.</summary>
    protected virtual void OnRender(RenderContext<CameraView> context, CommandBuffer cmd, RenderTexture output) { }
}

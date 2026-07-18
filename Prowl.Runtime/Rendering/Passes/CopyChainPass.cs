// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Graphite;

using Prowl.Runtime.Resources;
using Prowl.Vector;

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
/// input into the output when present (the chain hand-off), then invokes <see cref="OnRender"/> for
/// any extra pass-specific drawing. The pass that presents to the camera sets its output as the main
/// output.
/// </summary>
public abstract class CopyChainPass : IPass<DrawCommand>
{
    private readonly RenderResourceID _inputId;
    private readonly RenderResourceID _outputId;
    private readonly bool _hasInput;
    private readonly bool _present;

    private TextureHandle _inputHandle;
    private TextureHandle _outputHandle;

    protected CopyChainPass(string name, RenderResourceID outputId, bool present, RenderResourceID inputId = default)
    {
        Name = name;
        _outputId = outputId;
        _present = present;
        _inputId = inputId;
        _hasInput = inputId.IsValid;
    }

    public string Name { get; }

    public void SetupInputs(RenderContextBuilder builder)
    {
        RenderTextureDesc desc = RenderTextureDesc.CameraSized(true);

        if (_hasInput)
            _inputHandle = builder.GetInputTexture(_inputId, desc);

        _outputHandle = builder.GetOutputTexture(_outputId, desc);

        if (_present)
            builder.SetMainOutput(_outputHandle);
    }

    public void Render(RenderContext<DrawCommand> context)
    {
        RenderTexture output = context.GetTexture(_outputHandle);
        CommandBuffer cmd = context.GetCommandBuffer(Name);

        cmd.SetRenderTarget(output.frameBuffer);
        cmd.SetViewport(0, 0, (uint)output.Width, (uint)output.Height);

        if (_hasInput)
            cmd.Blit(context.GetTexture(_inputHandle), output);
        else
            cmd.ClearRenderTarget(true, true, new Color(0f, 0f, 0f, 1f));

        OnRender(context, cmd, output);

        context.SubmitCommandBuffer(cmd);
    }

    /// <summary>Extra drawing after the chain copy. The output framebuffer is already bound.</summary>
    protected virtual void OnRender(RenderContext<DrawCommand> context, CommandBuffer cmd, RenderTexture output) { }
}

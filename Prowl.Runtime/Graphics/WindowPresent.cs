// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Graphite;
using Prowl.Graphite.ShaderDef;
using Prowl.Runtime.Resources;

using GTexture = Prowl.Graphite.Texture;

namespace Prowl.Runtime;

/// <summary>Copies the window target to the swapchain, last in the frame, which presents it.</summary>
internal sealed class WindowPresent
{
    private ShaderPass? _copy;
    private readonly PropertySet _properties = new();
    private static readonly Keyword Present = new("MODE", "PRESENT");

    public void Add(FrameGraph graph, Framebuffer swapchain, GTexture source)
    {
        TextureHandle target = graph.ImportTarget(swapchain);
        TextureHandle input = graph.Import(source);
        graph.AddPass("Present", pass =>
        {
            pass.Write(target, firstWrite: new TargetLoadStoreOps(new AttachmentOps(LoadAction.DontCare, StoreAction.Store), AttachmentOps.Discard));
            pass.Read(input);
        }, context => Render(context.Command, source));
    }

    private void Render(CommandBuffer cmd, GTexture source)
    {
        if (_copy == null)
        {
            ShaderDefinition definition = ShaderParser.Parse(EmbeddedResources.ReadAllText("Assets/Defaults/Shaders/PaperCanvas.shader"));
            definition.Create(Graphics.Device!, Graphics.Compiler, new Variant(), CompileMode.All);
            _copy = definition.Passes![1];
        }

        _properties.SetTexture("_MainTex", source, Graphics.GetSampler(SamplerDescription.Point));
        _properties.SetFloat("_Offset", 1f);
        cmd.SetShader(_copy, [Present]);
        cmd.SetVertexSource(VertexSource.None);
        cmd.SetProperties(_properties);
        cmd.Draw(3);
    }
}

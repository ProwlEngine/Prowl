// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.Graphite;
using Prowl.Graphite.RenderGraph;
using Prowl.Graphite.ShaderDef;
using Prowl.Runtime.Resources;

using GTexture = Prowl.Graphite.Texture;

namespace Prowl.Runtime;

/// <summary>Copies the window target to the swapchain, last in the frame, which presents it.</summary>
internal sealed class WindowPresent : IDisposable
{
    private readonly RenderPipeline _pipeline;
    private readonly PresentPass _pass = new();
    private readonly int _viewId = Graphics.NewViewId();

    public WindowPresent() => _pipeline = new RenderPipeline([_pass]) { Name = "Window Present" };

    public GraphDispatch Dispatch(Framebuffer swapchain, GTexture source)
        => new(_pipeline, [new PresentView(swapchain, source, _viewId)]);

    private sealed class PresentView(Framebuffer target, GTexture source, int id) : IRenderView
    {
        public GTexture Source { get; } = source;
        public uint PixelWidth => target.Width;
        public uint PixelHeight => target.Height;
        public int ViewId => id;
        public string Name => "Window";
        public Framebuffer? Target => target;
    }

    private sealed class PresentPass : IPass
    {
        private ShaderPass? _copy;
        private readonly PropertySet _properties = new();
        private static readonly Keyword Present = new("MODE", "PRESENT");

        public string Name => "Present";

        public void Setup(RenderContextBuilder builder) => builder.DeclareViewTarget(new TargetLoadStoreOps(new AttachmentOps(LoadAction.DontCare, StoreAction.Store), AttachmentOps.Discard));

        public void Render(RenderContext context, CommandBuffer cmd)
        {
            if (_copy == null)
            {
                ShaderDefinition definition = ShaderParser.Parse(EmbeddedResources.ReadAllText("Assets/Defaults/Shaders/PaperCanvas.shader"));
                definition.Create(Graphics.Device!, Graphics.Compiler, new Variant(), CompileMode.All);
                _copy = definition.Passes![1];
            }

            PresentView view = context.ViewAs<PresentView>();
            _properties.SetTexture("_MainTex", view.Source, Graphics.GetSampler(SamplerDescription.Point));
            _properties.SetFloat("_Offset", 1f);
            cmd.SetShader(_copy, [Present]);
            cmd.SetVertexSource(VertexSource.None);
            cmd.SetProperties(_properties);
            cmd.Draw(3);
        }
    }

    public void Dispose() => _pipeline.Dispose();
}

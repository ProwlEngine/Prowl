using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

using Prowl.Graphite.ShaderDef;
using Prowl.Vector;

namespace Prowl.Graphite.Rendering;

/// <summary>The FrameStart pass: the frame's single scatter into every persistent buffer, with each declared as written.</summary>
internal sealed class UploadPass(Uploader uploader) : IExecutionPass
{
    public FrameBuffers Buffers = null!;

    public string Name => "Graphite.FrameStart";

    public void Setup(RenderContextBuilder builder) => Buffers.Describe(builder, BufferAccess.ShaderWrite);

    public void Render(RenderContext context, CommandBuffer cmd) => uploader.Record(cmd, context.Task);
}

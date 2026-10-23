using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

using Prowl.Graphite.ShaderDef;
using Prowl.Vector;

namespace Prowl.Graphite.Rendering;

/// <summary>The FrameStart pass: the frame's single scatter into every buffer, with each declared as written.</summary>
internal sealed class UploadPass(Uploader uploader, RenderStore store)
{
    public void Add(FrameGraph graph, FrameBuffers buffers)
    {
        graph.AddPass("Graphite.FrameStart", builder => buffers.WriteAll(graph, builder, BufferAccess.ShaderWrite), context =>
        {
            if (store.HasResizes)
                store.RecordResizes(context.Command);
            uploader.Record(context.Command, context.Task);
        });
    }
}

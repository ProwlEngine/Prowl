using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

using Prowl.Graphite.ShaderDef;
using Prowl.Vector;

namespace Prowl.Graphite.Rendering;

/// <summary>A copy to the host the renderer records after a frame's views.</summary>
internal sealed class ReadbackRequest
{
    public Texture? Texture;
    public TextureRegion Region;
    public DeviceBuffer? Buffer;
    public uint Offset, Size;
    public Action<TextureReadback>? TextureDone;
    public Action<BufferReadback>? BufferDone;
    public DeviceBuffer? Staging;
    public ulong Execution;
    public int Attempts;
    public string? Error;
}

/// <summary>Makes the staging buffers of a frame's readbacks and drops the requests whose source was disposed.</summary>
internal sealed class ReadbackStaging(GraphicsDevice device, Action<RendererError> report)
{
    public readonly List<ReadbackRequest> Requests = new();

    public void Prepare(List<ReadbackRequest> requests)
    {
        Requests.Clear();
        for (int i = requests.Count - 1; i >= 0; i--)
        {
            ReadbackRequest request = requests[i];
            if (request.Texture?.IsDisposed == true || request.Buffer?.IsDisposed == true)
            {
                request.Error = "The texture or buffer was disposed before it was copied.";
                report(new RendererError("readback.disposed", "A readback's texture or buffer was disposed before it was copied.", RendererErrorSeverity.Warning, (object?)request.Texture ?? request.Buffer));
                continue;
            }
            uint bytes = request.Texture is { } texture ? request.Region.Width * request.Region.Height * request.Region.Depth * texture.Format.GetSizeInBytes() : request.Size;
            request.Staging ??= device.ResourceFactory.CreateBuffer(new BufferDescription(bytes, BufferUsage.Staging));
            Requests.Add(request);
        }
    }
}

/// <summary>Copies a readback's texture or buffer into its staging buffer.</summary>
internal static class ReadbackPasses
{
    public static void Add(FrameGraph graph, ReadbackRequest request, int index)
    {
        if (request.Texture is { } texture)
        {
            graph.AddPass("Graphite.ReadbackTexture", builder => builder.Read(graph.Import(texture), TextureState.TransferSrc),
                (context, source) => context.Command.CopyTextureToBuffer(context.Get(source).ColorTextures[0], request.Staging!, 0, request.Region));
        }
        else
        {
            graph.AddPass("Graphite.ReadbackBuffer", builder => builder.Read(graph.Import(request.Buffer!), BufferAccess.TransferRead),
                (context, source) => context.Command.CopyBuffer(context.Get(source), request.Offset, request.Staging!, 0, request.Size));
        }
    }
}

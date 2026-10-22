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

/// <summary>Copies the texture a readback instance binds into its staging buffer.</summary>
internal sealed class ReadbackTexturePass : IInstancePass
{
    private TextureHandle _source;

    public string Name => "Graphite.ReadbackTexture";

    public GraphKind Kind => RenderKinds.Readback;

    public bool IsEnabled(GraphInstance instance) => instance.Data is ReadbackRequest { Texture: not null };

    public void Setup(InstanceContextBuilder builder) => _source = builder.DeclareImportSlotTexture(RenderKinds.ReadbackTexture, TextureState.TransferSrc);

    public void Render(InstanceContext context, CommandBuffer cmd)
    {
        ReadbackRequest request = (ReadbackRequest)context.Instance.Data!;
        cmd.CopyTextureToBuffer(context.GetRenderTexture(_source).ColorTextures[0], request.Staging!, 0, request.Region);
    }
}

/// <summary>Copies the buffer a readback instance binds into its staging buffer.</summary>
internal sealed class ReadbackBufferPass : IInstancePass
{
    private BufferHandle _source;

    public string Name => "Graphite.ReadbackBuffer";

    public GraphKind Kind => RenderKinds.Readback;

    public bool IsEnabled(GraphInstance instance) => instance.Data is ReadbackRequest { Buffer: not null };

    public void Setup(InstanceContextBuilder builder) => _source = builder.DeclareImportSlotBuffer(RenderKinds.ReadbackBuffer, BufferAccess.TransferRead);

    public void Render(InstanceContext context, CommandBuffer cmd)
    {
        ReadbackRequest request = (ReadbackRequest)context.Instance.Data!;
        cmd.CopyBuffer(context.GetRenderBuffer(_source), request.Offset, request.Staging!, 0, request.Size);
    }
}

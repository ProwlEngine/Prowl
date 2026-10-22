using System;

namespace Prowl.Graphite;

/// <summary>Declares what a pass reads and writes. Valid only inside the setup callback.</summary>
public sealed class PassBuilder
{
    private readonly FrameGraph _graph;

    internal PassBuilder(FrameGraph graph) => _graph = graph;

    /// <summary>Declares a texture read. Returns the handle it was given.</summary>
    public TextureHandle Read(TextureHandle texture, TextureState usage = TextureState.Sampled, TextureState? depthUsage = null)
    {
        FramePass pass = Pass();
        FrameResource resource = Latest(pass, texture.Index, texture.Id, texture.Version, isTexture: true);
        RequireWritten(pass, resource);
        Record(pass, ResourceAccess.Texture(resource.Id, texture.Index, texture.Version, usage, depthUsage, isOutput: false), resource);
        return texture;
    }

    /// <summary>Declares a texture write. Returns the new version of the handle.</summary>
    public TextureHandle Write(TextureHandle texture, TextureState usage = TextureState.Attachment, TextureState? depthUsage = null, TargetLoadStoreOps? firstWrite = null)
    {
        FramePass pass = Pass();
        FrameResource resource = Latest(pass, texture.Index, texture.Id, texture.Version, isTexture: true);
        Record(pass, ResourceAccess.Texture(resource.Id, texture.Index, resource.Version + 1, usage, depthUsage, isOutput: true, firstWrite), resource);
        return new TextureHandle(resource.Id, texture.Index, NewVersion(pass, resource));
    }

    /// <summary>Declares a buffer read. Returns the handle it was given.</summary>
    public BufferHandle Read(BufferHandle buffer, BufferAccess usage = BufferAccess.AllReads)
    {
        FramePass pass = Pass();
        FrameResource resource = Latest(pass, buffer.Index, buffer.Id, buffer.Version, isTexture: false);
        RequireWritten(pass, resource);
        Record(pass, ResourceAccess.Buffer(resource.Id, buffer.Index, buffer.Version, usage, isOutput: false), resource);
        return buffer;
    }

    /// <summary>Declares a buffer write. Returns the new version of the handle.</summary>
    public BufferHandle Write(BufferHandle buffer, BufferAccess usage = BufferAccess.ShaderRead | BufferAccess.ShaderWrite)
    {
        FramePass pass = Pass();
        FrameResource resource = Latest(pass, buffer.Index, buffer.Id, buffer.Version, isTexture: false);
        Record(pass, ResourceAccess.Buffer(resource.Id, buffer.Index, resource.Version + 1, usage, isOutput: true), resource);
        return new BufferHandle(resource.Id, buffer.Index, NewVersion(pass, resource));
    }

    private FramePass Pass()
        => _graph.Building ?? throw new InvalidOperationException("A pass can only be declared inside its setup callback.");

    private FrameResource Latest(FramePass pass, int index, RenderResourceID id, int version, bool isTexture)
    {
        if (!id.IsValid || index < 0 || index >= _graph.ResourceCount)
            throw new RenderGraphException($"Pass '{pass.Name}' uses a handle that does not belong to this frame.", pass.Name);

        FrameResource resource = _graph.ResourceAt(index);
        if (resource.Id != id || resource.IsTexture != isTexture || version > resource.Version)
            throw new RenderGraphException($"Pass '{pass.Name}' uses a handle that does not belong to this frame.", pass.Name);

        if (version == resource.Version)
            return resource;

        int newer = resource.Writers[version + 1];
        if (_graph.PassAt(newer) == pass)
            return resource;

        throw new RenderGraphException(
            $"Pass '{pass.Name}' uses version {version} of '{resource.Name}', but pass '{_graph.PassAt(newer).Name}' made a newer one.", pass.Name);
    }

    private static void RequireWritten(FramePass pass, FrameResource resource)
    {
        if (resource.IsTransient && !resource.Written)
            throw new RenderGraphException($"Pass '{pass.Name}' reads transient '{resource.Name}' before any pass writes it this frame.", pass.Name);
    }

    private int NewVersion(FramePass pass, FrameResource resource)
    {
        resource.Writers.Add(_graph.PassCount - 1);
        resource.Written = true;
        if (resource.FirstWriter < 0)
            resource.FirstWriter = _graph.PassCount - 1;
        return ++resource.Version;
    }

    private void Record(FramePass pass, ResourceAccess access, FrameResource resource)
    {
        if (access.IsTexture && (access.TextureUsage & TextureState.Storage) != 0)
            RequireStorage(pass, resource);

        resource.LastAccess = _graph.PassCount - 1;
        pass.Accesses.Add(access);
    }

    private static void RequireStorage(FramePass pass, FrameResource resource)
    {
        if (resource.ImportedTexture is { } imported)
        {
            foreach (Texture color in imported.ColorTextures)
            {
                if ((color.Usage & TextureUsage.Storage) == 0)
                    throw new RenderGraphException(
                        $"Pass '{pass.Name}' declares imported texture '{resource.Name}' as Storage, but its color textures were not created with TextureUsage.Storage.", pass.Name);
            }
        }
        else
        {
            resource.Storage = true;
        }
    }
}

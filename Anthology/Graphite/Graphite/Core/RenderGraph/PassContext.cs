using System;

namespace Prowl.Graphite;

/// <summary>What a pass's render callback records with: its command buffer and the resources it declared.</summary>
public sealed class PassContext
{
    private GraphExecution? _execution;
    private FramePass? _pass;

    internal PassContext() { }

    /// <summary>The pass's command buffer, already begun. The graph submits it after the callback returns.</summary>
    public CommandBuffer Command { get; private set; } = null!;

    public ExecutionTask Task => _execution!.Task;

    internal void Begin(GraphExecution execution, FramePass pass, CommandBuffer command)
    {
        _execution = execution;
        _pass = pass;
        Command = command;
    }

    internal void End()
    {
        _execution = null;
        _pass = null;
        Command = null!;
    }

    /// <summary>Resolves a declared texture to its allocated render target.</summary>
    public RenderTexture Get(TextureHandle texture)
    {
        Declared(texture.Index, texture.Id);
        return _execution!.TextureAt(texture.Index);
    }

    /// <summary>Resolves a declared buffer to its allocated device buffer.</summary>
    public DeviceBuffer Get(BufferHandle buffer)
    {
        Declared(buffer.Index, buffer.Id);
        return _execution!.BufferAt(buffer.Index);
    }

    /// <summary>An earlier frame of a history texture, null on its first execution and after it was reallocated.</summary>
    public RenderTexture? History(TextureHandle texture, int framesAgo = 1)
    {
        Declared(texture.Index, texture.Id);
        RenderTexture? history = _execution!.HistoryTexture(texture.Index, framesAgo);
        if (history != null && _execution.Validate)
            _execution.Bindings.Declare(history);
        return history;
    }

    /// <summary>An earlier frame of a history buffer, null on its first execution and after it was reallocated.</summary>
    public DeviceBuffer? History(BufferHandle buffer, int framesAgo = 1)
    {
        Declared(buffer.Index, buffer.Id);
        DeviceBuffer? history = _execution!.HistoryBuffer(buffer.Index, framesAgo);
        if (history != null && _execution.Validate)
            _execution.Bindings.Declared.Add(history.ResourceId);
        return history;
    }

    /// <summary>Allocates a transient uniform buffer range from this execution's bump allocator.</summary>
    public DeviceBufferRange AllocateTransient(uint sizeInBytes) => Task.AllocateTransientInternal(sizeInBytes);

    private void Declared(int index, RenderResourceID id)
    {
        if (_pass == null)
            throw new InvalidOperationException("A pass context can only be used inside its render callback.");
        if (!id.IsValid || index < 0 || !_pass.Declares(index))
            throw new RenderGraphException(
                $"Pass '{_pass.Name}' uses resource '{RenderResourceID.ToString(id)}' without declaring it in setup.", _pass.Name);
    }
}

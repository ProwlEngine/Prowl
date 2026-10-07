using System;
using System.Collections.Generic;

namespace Prowl.Graphite.Vk;

internal sealed class VkExecutionTask : ExecutionTask
{
    private readonly VkGraphicsDevice _gd;
    private readonly ulong _id;
    private readonly uint _ringSlot;

    private readonly VkUniformArena _uniformArena;
    private readonly List<VkCommandBuffer> _queuedCommandBuffers;

    public override ulong Id => _id;
    public override uint RingSlot => _ringSlot;
    public override GraphicsDevice Device => _gd;

    internal VkExecutionTask(
        VkGraphicsDevice gd,
        ulong id,
        uint ringSlot,
        VkUniformArena uniformArena,
        List<VkCommandBuffer> queuedCommandBuffers)
    {
        _gd = gd;
        _id = id;
        _ringSlot = ringSlot;
        _uniformArena = uniformArena;
        _queuedCommandBuffers = queuedCommandBuffers;
    }

    internal VkUniformArena UniformArena => _uniformArena;


    /// <inheritdoc/>
    internal override void SubmitCommandsInternal(CommandBuffer commandList)
    {
        SubmitCommands_CheckEnded(_gd, commandList);
        _queuedCommandBuffers.Add(Util.AssertSubtype<CommandBuffer, VkCommandBuffer>(commandList));
    }


    internal override void SubmitCommandsAheadInternal(CommandBuffer commandList, CommandBuffer before)
    {
        SubmitCommands_CheckEnded(_gd, commandList);
        int index = _queuedCommandBuffers.IndexOf(Util.AssertSubtype<CommandBuffer, VkCommandBuffer>(before));
        if (index < 0)
            throw new InvalidOperationException("The command buffer is no longer queued, so nothing can be queued ahead of it.");

        _queuedCommandBuffers.Insert(index, Util.AssertSubtype<CommandBuffer, VkCommandBuffer>(commandList));
    }


    /// <inheritdoc/>
    internal override void FlushSubmissions()
    {
        if (_queuedCommandBuffers.Count == 0)
            return;

        _gd.SubmitExecutionBatch(_queuedCommandBuffers, _id, isFinal: false);
        _queuedCommandBuffers.Clear();
    }


    /// <summary>Submits whatever is still queued and returns the serial that marks the execution complete.</summary>
    internal ulong FinalSubmit()
    {
        ulong serial = _gd.SubmitExecutionBatch(_queuedCommandBuffers, _id, isFinal: true);
        _queuedCommandBuffers.Clear();
        return serial;
    }


    /// <inheritdoc/>
    internal override DeviceBufferRange AllocateTransientInternal(uint sizeInBytes)
    {
        AllocateTransientMapped(sizeInBytes, out DeviceBufferRange range);
        return range;
    }


    /// <summary>Allocates transient uniform space and returns a span over its mapped memory.</summary>
    internal Span<byte> AllocateTransientMapped(uint sizeInBytes, out DeviceBufferRange range)
    {
        Span<byte> dst = _uniformArena.Allocate(sizeInBytes, out range, out bool grew);
        if (grew)
            CheckCumulativeCaps();
        return dst;
    }


    private void CheckCumulativeCaps()
    {
        ulong cumulative = _uniformArena.CumulativeBytes;

        CheckCumulativeCaps_CheckHardCap(_gd, cumulative, GraphicsDevice.TransientHardCapBytes);
    }
}

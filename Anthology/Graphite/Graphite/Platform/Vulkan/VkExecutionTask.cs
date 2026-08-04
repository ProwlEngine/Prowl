using System;
using System.Collections.Generic;

namespace Prowl.Graphite.Vk;

internal sealed class VkExecutionTask : ExecutionTask
{
    private readonly VkGraphicsDevice _gd;
    private readonly ulong _id;
    private readonly uint _ringSlot;

    private readonly VkFence _slotFenceWrapper;
    private readonly VkUniformArena _uniformArena;
    private readonly List<VkCommandBuffer> _rentedCommandBuffers;

    public override ulong Id => _id;
    public override uint RingSlot => _ringSlot;
    public override Fence CompletionFence => _slotFenceWrapper;
    public override GraphicsDevice Device => _gd;

    internal VkExecutionTask(
        VkGraphicsDevice gd,
        ulong id,
        uint ringSlot,
        VkFence slotFenceWrapper,
        VkUniformArena uniformArena,
        List<VkCommandBuffer> rentedCommandBuffers)
    {
        _gd = gd;
        _id = id;
        _ringSlot = ringSlot;
        _slotFenceWrapper = slotFenceWrapper;
        _uniformArena = uniformArena;
        _rentedCommandBuffers = rentedCommandBuffers;
    }

    internal VkUniformArena UniformArena => _uniformArena;


    /// <inheritdoc/>
    internal override void TrackRentedCommandBuffer(CommandBuffer commandBuffer)
    {
        _rentedCommandBuffers.Add(Util.AssertSubtype<CommandBuffer, VkCommandBuffer>(commandBuffer));
    }


    /// <inheritdoc/>
    internal override void SubmitCommandsInternal(CommandBuffer commandList)
    {
        SubmitCommands_CheckEnded(commandList);
        _gd.SubmitCommandBufferInternal(commandList);

        _gd.Profiler?.RecordSubmit(commandList.ProfilerInfo, isTransfer: false);
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

        CheckCumulativeCaps_CheckHardCap(cumulative, _gd._transientHardCapBytes);

        if (!_gd._transientSoftCapWarned && cumulative > _gd._transientSoftCapBytes)
        {
            _gd._transientSoftCapWarned = true;
            _gd.OnWarning?.Invoke($"[Graphite] Warning: Transient buffer soft cap of {_gd._transientSoftCapBytes} bytes exceeded in execution {_id}.");
        }
    }
}

using System;
using System.Collections.Generic;

namespace Prowl.Graphite.Vk;

internal sealed class VkExecutionTask : ExecutionTask
{
    private readonly VkGraphicsDevice _gd;
    private readonly ulong _id;
    private readonly uint _ringSlot;

    private readonly VkFence _slotFenceWrapper;
    private readonly VkBuffer _transientPrimary;
    private readonly List<VkBuffer> _transientOverflow;
    private uint _transientHead;
    private uint _activeTransientSize;
    private VkBuffer _activeTransientBuffer;

    public override ulong Id => _id;
    public override uint RingSlot => _ringSlot;
    public override Fence CompletionFence => _slotFenceWrapper;
    public override GraphicsDevice Device => _gd;

    internal VkExecutionTask(
        VkGraphicsDevice gd,
        ulong id,
        uint ringSlot,
        VkFence slotFenceWrapper,
        VkBuffer transientPrimary,
        List<VkBuffer> transientOverflow)
    {
        _gd = gd;
        _id = id;
        _ringSlot = ringSlot;
        _slotFenceWrapper = slotFenceWrapper;
        _transientPrimary = transientPrimary;
        _transientOverflow = transientOverflow;

        _activeTransientBuffer = transientPrimary;
        _activeTransientSize = transientPrimary.SizeInBytes;
    }


    /// <inheritdoc/>
    internal override void SubmitCommandsInternal(CommandBuffer commandList)
    {
        SubmitCommands_CheckEnded(commandList);
        _gd.SubmitCommandBufferInternal(commandList);
    }


    /// <inheritdoc/>
    internal override DeviceBufferRange AllocateTransientInternal(uint sizeInBytes)
    {
        uint alignment = _gd.UniformBufferMinOffsetAlignment;
        uint alignedHead = (_transientHead + alignment - 1) & ~(alignment - 1);

        if (alignedHead + sizeInBytes <= _activeTransientSize)
        {
            uint offset = alignedHead;
            _transientHead = alignedHead + sizeInBytes;
            return new DeviceBufferRange(_activeTransientBuffer, offset, sizeInBytes);
        }

        return AllocateFromOverflow(sizeInBytes);
    }


    private DeviceBufferRange AllocateFromOverflow(uint sizeInBytes)
    {
        uint requiredSize = Math.Max(sizeInBytes, _transientPrimary.SizeInBytes * 2);
        VkBuffer overflowBuffer = _gd.CreateTransientBuffer(requiredSize);
        _transientOverflow.Add(overflowBuffer);

        _activeTransientBuffer = overflowBuffer;
        _activeTransientSize = overflowBuffer.SizeInBytes;
        _transientHead = 0;

        CheckCumulativeCaps();

        uint offset = 0;
        _transientHead = sizeInBytes;
        return new DeviceBufferRange(overflowBuffer, offset, sizeInBytes);
    }


    private void CheckCumulativeCaps()
    {
        ulong cumulative = _transientPrimary.SizeInBytes;
        foreach (VkBuffer buf in _transientOverflow)
            cumulative += buf.SizeInBytes;

        CheckCumulativeCaps_CheckHardCap(cumulative, _gd._transientHardCapBytes);

        if (!_gd._transientSoftCapWarned && cumulative > _gd._transientSoftCapBytes)
        {
            _gd._transientSoftCapWarned = true;
            _gd.OnWarning?.Invoke($"[Graphite] Warning: Transient buffer soft cap of {_gd._transientSoftCapBytes} bytes exceeded in execution {_id}.");
        }
    }
}

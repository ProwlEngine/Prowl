using System;
using System.Collections.Generic;

namespace Prowl.Graphite.D3D11;

internal sealed class D3D11Frame : Frame
{
    private readonly D3D11GraphicsDevice _gd;
    private readonly ulong _frameId;
    private readonly uint _ringSlot;
    private readonly D3D11Fence _fenceWrapper;
    private readonly D3D11Buffer _transientPrimary;
    private readonly List<D3D11Buffer> _transientOverflow;

    private D3D11Buffer _activeTransientBuffer;
    private uint _activeTransientSize;
    private uint _transientHead;
    private bool _activeBufferMapped;
    private IntPtr _activeMappedData;

    public override ulong FrameId => _frameId;
    public override uint RingSlot => _ringSlot;
    public override Fence CompletionFence => _fenceWrapper;
    public override GraphicsDevice Device => _gd;

    internal bool ActiveBufferMapped => _activeBufferMapped;
    internal D3D11Buffer ActiveTransientBuffer => _activeTransientBuffer;

    internal D3D11Frame(
        D3D11GraphicsDevice gd,
        ulong frameId,
        uint ringSlot,
        D3D11Fence fenceWrapper,
        D3D11Buffer transientPrimary,
        List<D3D11Buffer> transientOverflow)
    {
        _gd = gd;
        _frameId = frameId;
        _ringSlot = ringSlot;
        _fenceWrapper = fenceWrapper;
        _transientPrimary = transientPrimary;
        _transientOverflow = transientOverflow;

        _activeTransientBuffer = transientPrimary;
        _activeTransientSize = transientPrimary.SizeInBytes;
    }

    /// <inheritdoc/>
    public override void SubmitCommands(CommandBuffer commandList)
    {
        SubmitCommands_CheckEnded(commandList);
        _gd.SubmitCommandBufferInternal(commandList);
    }

    /// <inheritdoc/>
    public override DeviceBufferRange AllocateTransient(uint sizeInBytes)
    {
        if (!_activeBufferMapped)
        {
            _activeMappedData = _gd.Map(_activeTransientBuffer, MapMode.Write).Data;
            _activeBufferMapped = true;
        }

        uint alignment = _gd.UniformBufferMinOffsetAlignment;
        uint alignedHead = (_transientHead + alignment - 1) & ~(alignment - 1);

        if (alignedHead + sizeInBytes <= _activeTransientSize)
        {
            uint offset = alignedHead;
            _transientHead = alignedHead + sizeInBytes;
            return new DeviceBufferRange(_activeTransientBuffer, offset, sizeInBytes);
        }

        return AllocateFromOverflow(sizeInBytes, alignment);
    }

    internal void UnmapActiveBuffer()
    {
        if (_activeBufferMapped)
        {
            _gd.Unmap(_activeTransientBuffer);
            _activeBufferMapped = false;
            _activeMappedData = IntPtr.Zero;
        }
    }

    internal unsafe void WriteTransient(uint offset, void* src, uint size)
    {
        Buffer.MemoryCopy(src, (byte*)_activeMappedData + offset, size, size);
    }

    private DeviceBufferRange AllocateFromOverflow(uint sizeInBytes, uint alignment)
    {
        UnmapActiveBuffer();

        uint requiredSize = Math.Max(sizeInBytes, _transientPrimary.SizeInBytes * 2);
        D3D11Buffer overflowBuffer = _gd.CreateTransientBuffer(requiredSize);
        _transientOverflow.Add(overflowBuffer);

        _activeTransientBuffer = overflowBuffer;
        _activeTransientSize = overflowBuffer.SizeInBytes;
        _transientHead = 0;

        CheckCumulativeCaps();

        _activeMappedData = _gd.Map(_activeTransientBuffer, MapMode.Write).Data;
        _activeBufferMapped = true;

        uint offset = 0;
        _transientHead = sizeInBytes;
        return new DeviceBufferRange(overflowBuffer, offset, sizeInBytes);
    }

    private void CheckCumulativeCaps()
    {
        ulong cumulative = _transientPrimary.SizeInBytes;
        foreach (D3D11Buffer buf in _transientOverflow)
            cumulative += buf.SizeInBytes;

        CheckCumulativeCaps_CheckHardCap(cumulative, _gd._transientHardCapBytes);

        if (!_gd._transientSoftCapWarned && cumulative > _gd._transientSoftCapBytes)
        {
            _gd._transientSoftCapWarned = true;
            Console.Error.WriteLine($"[Graphite] Warning: Transient buffer soft cap of {_gd._transientSoftCapBytes} bytes exceeded in frame {_frameId}.");
        }
    }
}

using System;
using System.Collections.Generic;

namespace Prowl.Veldrid.OpenGL;

internal sealed class OpenGLFrame : Frame
{
    private readonly OpenGLGraphicsDevice _gd;
    private readonly ulong _frameId;
    private readonly uint _ringSlot;
    private readonly OpenGLFence _fenceWrapper;
    private readonly OpenGLBuffer _transientPrimary;
    private readonly List<OpenGLBuffer> _transientOverflow;

    private OpenGLBuffer _activeTransientBuffer;
    private uint _activeTransientSize;
    private uint _transientHead;

    public override ulong FrameId => _frameId;
    public override uint RingSlot => _ringSlot;
    public override Fence CompletionFence => _fenceWrapper;
    public override GraphicsDevice Device => _gd;

    internal OpenGLFrame(
        OpenGLGraphicsDevice gd,
        ulong frameId,
        uint ringSlot,
        OpenGLFence fenceWrapper,
        OpenGLBuffer transientPrimary,
        List<OpenGLBuffer> transientOverflow)
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
        if (!commandList.HasEnded)
            throw new RenderException("CommandBuffer.End() must be called before submitting.");
        _gd.SubmitCommandBufferInternal(commandList);
    }

    /// <inheritdoc/>
    public override DeviceBufferRange AllocateTransient(uint sizeInBytes)
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
        OpenGLBuffer overflowBuffer = _gd.CreateTransientBuffer(requiredSize);
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
        foreach (OpenGLBuffer buf in _transientOverflow)
            cumulative += buf.SizeInBytes;

        if (cumulative > _gd._transientHardCapBytes)
            throw new RenderException($"Transient buffer hard cap of {_gd._transientHardCapBytes} bytes exceeded.");

        if (!_gd._transientSoftCapWarned && cumulative > _gd._transientSoftCapBytes)
        {
            _gd._transientSoftCapWarned = true;
            Console.Error.WriteLine($"[Veldrid] Warning: Transient buffer soft cap of {_gd._transientSoftCapBytes} bytes exceeded in frame {_frameId}.");
        }
    }
}

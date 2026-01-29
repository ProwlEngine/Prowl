// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Silk.NET.OpenGL;

namespace Prowl.Runtime.Graphite.OpenGL;

/// <summary>
/// OpenGL implementation of a GPU fence using sync objects.
/// </summary>
public class GLFence : Fence
{
    private readonly GLGraphiteDevice _device;
    private nint _syncObject;
    private bool _signaled;

    public override bool IsSignaled
    {
        get
        {
            if (_signaled) return true;
            if (_syncObject == 0) return false;

            // Check sync status without waiting
            int status;
            uint length;
            unsafe
            {
                _device.GL.GetSync(_syncObject, SyncParameterName.SyncStatus, 1, &length, &status);
            }

            if (status == (int)GLEnum.Signaled)
            {
                _signaled = true;
                return true;
            }
            return false;
        }
    }

    internal GLFence(GLGraphiteDevice device, bool signaled)
    {
        _device = device;
        _signaled = signaled;
        _syncObject = 0;
    }

    public override void Reset()
    {
        ThrowIfDisposed();

        if (_syncObject != 0)
        {
            _device.GL.DeleteSync(_syncObject);
            _syncObject = 0;
        }
        _signaled = false;
    }

    public override void Wait()
    {
        ThrowIfDisposed();

        if (_signaled || _syncObject == 0) return;

        // Wait indefinitely
        _device.GL.ClientWaitSync(_syncObject, SyncObjectMask.SyncFlushCommandsBit, ulong.MaxValue);
        _signaled = true;
    }

    public override bool Wait(uint timeoutMs)
    {
        ThrowIfDisposed();

        if (_signaled) return true;
        if (_syncObject == 0) return false;

        ulong timeoutNs = (ulong)timeoutMs * 1_000_000;
        var result = _device.GL.ClientWaitSync(_syncObject, SyncObjectMask.SyncFlushCommandsBit, timeoutNs);

        if (result == GLEnum.AlreadySignaled || result == GLEnum.ConditionSatisfied)
        {
            _signaled = true;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Inserts the fence into the command stream.
    /// </summary>
    internal void InsertFence()
    {
        if (_syncObject != 0)
        {
            _device.GL.DeleteSync(_syncObject);
        }

        _syncObject = _device.GL.FenceSync(SyncCondition.SyncGpuCommandsComplete, SyncBehaviorFlags.None);
        _signaled = false;
    }

    protected override void DisposeResources()
    {
        if (_syncObject != 0)
        {
            _device.GL.DeleteSync(_syncObject);
            _syncObject = 0;
        }
    }
}

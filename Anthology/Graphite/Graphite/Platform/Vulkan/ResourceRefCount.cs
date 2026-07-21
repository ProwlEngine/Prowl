using System;
using System.Threading;

namespace Prowl.Graphite.Vk;

internal partial class ResourceRefCount
{
    private readonly Action _disposeAction;
    private int _refCount;

    /// <summary>
    /// Id of the command-buffer recording that last retained this resource in its staging set. Lets the
    /// recorder retain each distinct resource once per recording instead of re-adding it on every draw.
    /// </summary>
    public ulong StagingMark;

    public ResourceRefCount(Action disposeAction)
    {
        _disposeAction = disposeAction;
        _refCount = 1;
    }

    public int Increment()
    {
        int ret = Interlocked.Increment(ref _refCount);
        Increment_CheckNotDisposed(ret);
        return ret;
    }

    public int Decrement()
    {
        int ret = Interlocked.Decrement(ref _refCount);
        if (ret == 0)
        {
            _disposeAction();
        }

        return ret;
    }
}

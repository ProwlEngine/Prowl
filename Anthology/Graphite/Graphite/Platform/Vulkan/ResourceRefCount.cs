using System;
using System.Threading;

namespace Prowl.Graphite.Vk;

internal partial class ResourceRefCount
{
    private readonly Action _disposeAction;
    private int _refCount;

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

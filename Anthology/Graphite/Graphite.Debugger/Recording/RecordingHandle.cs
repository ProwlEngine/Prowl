using System;
using System.Threading;

namespace Prowl.Graphite.Debugger;

/// <summary>A result that becomes usable when the GPU finishes. Handles are done in the order their recordings began.</summary>
public abstract class RecordingHandle
{
    private readonly object _gate = new();
    private RecordingHandle? _previous;
    private volatile bool _done;

    internal RecordingHandle(RecordingHandle? previous)
    {
        _previous = previous;
    }

    /// <summary>True when every execution it covers has resolved and the handle started before it is done.</summary>
    public bool IsDone
    {
        get
        {
            if (_done)
                return true;

            RecordingHandle? previous = _previous;
            if (previous != null && !previous.IsDone)
                return false;

            lock (_gate)
            {
                if (_done)
                    return true;

                if (!PollReady())
                    return false;

                Finish();
                _previous = null;
                _done = true;
                return true;
            }
        }
    }

    /// <summary>Blocks until the handle started before it, then this one, is done.</summary>
    public void Wait()
    {
        _previous?.Wait();
        while (!IsDone)
        {
            if (!WaitForExecutions())
                Thread.Sleep(1);
        }
    }

    internal abstract bool PollReady();

    internal abstract bool WaitForExecutions();

    internal abstract void Finish();

    private protected void RequireDone()
    {
        if (!IsDone)
            throw new InvalidOperationException("The recording is not done. Check IsDone or call Wait() first.");
    }
}

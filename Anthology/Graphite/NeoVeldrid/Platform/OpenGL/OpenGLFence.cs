using System;
using System.Threading;

namespace Prowl.Veldrid.OpenGL;

internal class OpenGLFence : Fence
{
    private readonly ManualResetEventSlim _mre;
    private bool _disposed;

    public OpenGLFence(bool signaled)
    {
        _mre = new ManualResetEventSlim(signaled);
    }

    public override string Name { get; set; }
    public ManualResetEventSlim ResetEvent => _mre;

    public void Set() => _mre.Set();
    public override void Reset() => _mre.Reset();
    public override bool Signaled => _mre.Wait(0);
    public override bool IsDisposed => _disposed;

    public override void Dispose()
    {
        if (!_disposed)
        {
            _mre.Dispose();
            _disposed = true;
        }
    }

    internal bool Wait(ulong nanosecondTimeout)
    {
        ulong timeout = Math.Min(int.MaxValue, nanosecondTimeout / 1_000_000);
        return _mre.Wait((int)timeout);
    }
}

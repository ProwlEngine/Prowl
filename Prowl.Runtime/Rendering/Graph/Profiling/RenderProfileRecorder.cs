// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace Prowl.Runtime.Rendering;

/// <summary>
/// Builds a <see cref="RenderFrameReport"/> as a pipeline executes its graph. ExecuteGraph drives the
/// frame/pass brackets; passes add nested manual regions through
/// <c>RenderContext.BeginSample</c>/<c>EndSample</c>. This records the timing skeleton (frame time,
/// per-pass time, the sample tree). Population of resources, edges, counters and draw calls is layered
/// on top by the instrumentation pass.
/// </summary>
public sealed class RenderProfileRecorder
{
    private static long s_frameCounter;

    private readonly RenderFrameReport _report = new();
    private readonly Stopwatch _frameSw = new();
    private readonly Stopwatch _passSw = new();

    private PassReport? _currentPass;
    private readonly Stack<SampleScope> _scopeStack = new();
    private readonly Stack<long> _scopeStart = new();

    public RenderProfileRecorder(Camera camera)
    {
        _report.CameraId = camera?.InstanceID ?? 0;
    }

    /// <summary>The report being built. Instrumentation writes resources/edges/counters directly into it.</summary>
    public RenderFrameReport Report => _report;

    public void BeginFrame() => _frameSw.Restart();

    public void BeginPass(string name)
    {
        _currentPass = new PassReport
        {
            Name = name,
            Index = _report.Passes.Count,
            Root = new SampleScope { Name = name },
        };

        _report.Passes.Add(_currentPass);
        _scopeStack.Clear();
        _scopeStart.Clear();
        _passSw.Restart();
    }

    public void EndPass()
    {
        if (_currentPass == null)
            return;

        _passSw.Stop();
        _currentPass.CpuMs = _passSw.Elapsed.TotalMilliseconds;
        _currentPass.Root.CpuMs = _currentPass.CpuMs;
        _currentPass = null;
    }

    /// <summary>Appends a draw call to the pass currently being recorded. No-op outside a pass.</summary>
    public void RecordDrawCall(DrawCallReport call)
    {
        if (_currentPass == null || call == null)
            return;

        _currentPass.DrawCalls.Add(call);
    }

    public void BeginSample(string name)
    {
        if (_currentPass == null)
            return;

        var scope = new SampleScope { Name = name };
        SampleScope parent = _scopeStack.Count > 0 ? _scopeStack.Peek() : _currentPass.Root;
        parent.Children.Add(scope);

        _scopeStack.Push(scope);
        _scopeStart.Push(Stopwatch.GetTimestamp());
    }

    public void EndSample()
    {
        if (_scopeStack.Count == 0)
            return;

        SampleScope scope = _scopeStack.Pop();
        long start = _scopeStart.Pop();
        scope.CpuMs = (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
    }

    public RenderFrameReport Finish()
    {
        _frameSw.Stop();
        _report.FrameIndex = Interlocked.Increment(ref s_frameCounter);
        _report.CpuFrameMs = _frameSw.Elapsed.TotalMilliseconds;
        _report.Counters.Passes = _report.Passes.Count;
        return _report;
    }
}

using System;
using System.Collections.Generic;
using System.Diagnostics;

using Prowl.Editor.Profiling.Scene;
using Prowl.Graphite;
using Prowl.Runtime.Rendering;

namespace Prowl.Editor.Profiling;

/// <summary>
/// Callback A6 registers to service <see cref="IProfiler.Capture"/> requests and produce a
/// <see cref="Snapshot"/>. Returning null means no snapshot was produced for that call.
/// </summary>
public delegate Snapshot? SnapshotCaptureHandler(in PassInfo pass, IReadOnlyList<Framebuffer> passOutputs, TransferCommandBuffer transfer);

public sealed class EditorProfiler : IProfiler
{
    private const int RingSize = 240;

    private readonly TimingCollector _timing = new();
    private readonly CountersCollector _counters = new();
    private readonly PassGraphCollector _passGraph = new();

    public DrawHierarchyCollector DrawHierarchy { get; } = new();
    public SnapshotCapturer SnapshotCapturer { get; } = new();

    private GraphicsDevice? _device;

    private bool _paused;
    private bool _captureArmedNext;
    private bool _captureActiveThisFrame;

    private long _frameIndex = -1;
    private long _sealedCount;

    private readonly Stopwatch _frameStopwatch = new();

    // Frames live here for their whole lifetime - collectors write directly into whichever slot is
    // current, and a slot is only ever reset (never copied) when the write pointer laps back around to
    // it RingSize frames later. The one exception is an explicitly armed capture (see EndFrame), which
    // clones out a fully independent copy since a Snapshot must outlive this ring.
    private readonly ProfiledFrame?[] _ring = new ProfiledFrame?[RingSize];
    private ProfiledFrame? _current;

    private readonly IReadOnlyList<string> _counterNames;
    private readonly Dictionary<string, int> _counterIndex;

    private string _currentView = "";

    private ShaderBindRecord? _pendingShaderBind;

    public EditorProfiler()
    {
        IReadOnlyList<CounterDef> registry = CountersCollector.Registry;
        var names = new List<string>(registry.Count);
        var index = new Dictionary<string, int>(registry.Count);
        for (int i = 0; i < registry.Count; i++)
        {
            names.Add(registry[i].Name);
            index[registry[i].Name] = i;
        }
        _counterNames = names;
        _counterIndex = index;
    }

    // Lifecycle

    public void Attach(GraphicsDevice device)
    {
        _device = device;
        device.SetProfiler(this);
    }

    public void Detach()
    {
        _device?.SetProfiler(null);
        _device = null;
    }

    // Prowl-side markers

    public void BeginFrame()
    {
        if (_paused)
            return;

        _frameIndex++;

        _currentView = "";
        _pendingShaderBind = null;

        _captureActiveThisFrame = _captureArmedNext;
        _captureArmedNext = false;

        int slot = (int)(_frameIndex % RingSize);
        ProfiledFrame frame = _ring[slot] ??= new ProfiledFrame();
        frame.Reset(_frameIndex, _captureActiveThisFrame);
        _current = frame;

        _frameStopwatch.Restart();

        _timing.OnFrameBegin(frame);
        _counters.OnFrameBegin();
        _passGraph.OnFrameBegin(frame, _captureActiveThisFrame);
        DrawHierarchy.OnFrameBegin(frame, _captureActiveThisFrame);
        SnapshotCapturer.OnFrameBegin();
    }

    public void EndFrame()
    {
        if (_paused)
            return;

        _frameStopwatch.Stop();
        double ms = _frameStopwatch.Elapsed.TotalMilliseconds;
        ProfiledFrame frame = _current!;
        frame.FrameMilliseconds = ms;
        frame.Fps = ms > 0.0 ? 1000.0 / ms : 0.0;

        DrawHierarchy.FinalizeFrame();
        _timing.FinalizeFrame(frame);
        _counters.Contribute(frame);
        _passGraph.FinalizeFrame(frame, _timing);

        _sealedCount = frame.FrameIndex + 1;

        if (_captureActiveThisFrame)
        {
            ProfiledFrame snapshotFrame = frame.Clone();
            Snapshot? snapshot = CaptureFinalizeHandler?.Invoke(snapshotFrame);
            if (snapshot != null)
                SnapshotCaptured?.Invoke(snapshot);
        }

        _captureActiveThisFrame = false;
    }

    public void BeginView(string name)
    {
        if (_paused)
            return;

        _currentView = name;
        _timing.OnViewBegin(name);
        DrawHierarchy.OnViewBegin(name);
    }

    public void EndView()
    {
        if (_paused)
            return;

        _timing.OnViewEnd();
        DrawHierarchy.OnViewEnd();
        _currentView = "";
    }

    public void NoteShaderBind(in ShaderBindRecord r)
    {
        if (_paused)
            return;

        _pendingShaderBind = r;
    }

    // Control

    public bool IsPaused => _paused;
    public void Pause() => _paused = true;
    public void Resume() => _paused = false;

    public void RequestCaptureNextFrame() => _captureArmedNext = true;

    public bool RequestExecutionTiming { get; set; }

    public SnapshotCaptureHandler? CaptureHandler { get; set; }

    /// <summary>
    /// A6 registers this to assemble and return the <see cref="Snapshot"/> for a completed capture
    /// once the frame's GPU work has been submitted, per the CONTRACTS.md/A6 ordering requirement
    /// (assemble/raise after the frame, not inside a pass). Invoked from <see cref="EndFrame"/> with
    /// a cloned, fully independent copy of that frame (HasCaptureDepth == true), only on frames
    /// where a capture was active.
    /// </summary>
    public Func<ProfiledFrame, Snapshot?>? CaptureFinalizeHandler { get; set; }

    // Read

    public IReadOnlyList<ProfiledFrame> History
    {
        get
        {
            long start = Math.Max(0, _sealedCount - RingSize);
            var list = new List<ProfiledFrame>((int)(_sealedCount - start));
            for (long i = start; i < _sealedCount; i++)
            {
                ProfiledFrame? f = GetFrame(i);
                if (f != null)
                    list.Add(f);
            }
            return list;
        }
    }

    public ProfiledFrame? Latest => FrameAgo(0);

    public ProfiledFrame? FrameAgo(int n)
    {
        if (n < 0)
            return null;
        long frameIndex = _sealedCount - 1 - n;
        return GetFrame(frameIndex);
    }

    public IReadOnlyList<string> CounterNames => _counterNames;

    public IReadOnlyList<double> CounterHistory(string name)
    {
        if (!_counterIndex.TryGetValue(name, out int index))
            return Array.Empty<double>();

        long start = Math.Max(0, _sealedCount - RingSize);
        var list = new List<double>((int)(_sealedCount - start));
        for (long i = start; i < _sealedCount; i++)
        {
            ProfiledFrame? f = GetFrame(i);
            list.Add(f != null ? f.Counters[index].Value : 0.0);
        }
        return list;
    }

    // Snapshot handoff

    public event Action<Snapshot>? SnapshotCaptured;

    // Internals

    private ProfiledFrame? GetFrame(long frameIndex)
    {
        if (frameIndex < 0 || frameIndex >= _sealedCount)
            return null;
        if (_sealedCount - frameIndex > RingSize)
            return null;

        int slot = (int)(frameIndex % RingSize);
        ProfiledFrame? frame = _ring[slot];
        return frame != null && frame.FrameIndex == frameIndex ? frame : null;
    }

    // Prowl.Graphite.IProfiler - explicit implementation. Graphite calls these; editor/window
    // code never should, so they are kept off EditorProfiler's public surface.

    bool IProfiler.RequestCapture => _captureActiveThisFrame;
    bool IProfiler.RequestExecutionTiming => RequestExecutionTiming;

    void IProfiler.Allocate(AllocBin type, long bytes)
    {
        if (_paused)
            return;
        _counters.OnAllocate(type, bytes);
    }

    void IProfiler.Free(AllocBin type, long bytes)
    {
        if (_paused)
            return;
        _counters.OnFree(type, bytes);
    }

    void IProfiler.AllocateMemory(BufferRoleBin role, long bytes)
    {
        if (_paused)
            return;
        _counters.OnAllocateMemory(role, bytes);
    }

    void IProfiler.FreeMemory(BufferRoleBin role, long bytes)
    {
        if (_paused)
            return;
        _counters.OnFreeMemory(role, bytes);
    }

    void IProfiler.Record(BufferOpBin op, long bytes)
    {
        if (_paused)
            return;
        _counters.OnBufferOp(op, bytes);
    }

    void IProfiler.RecordSwap(SwapBin evt, long bytes)
    {
        if (_paused)
            return;
        _counters.OnSwap(evt, bytes);
    }

    void IProfiler.BeginPass(in PassInfo pass)
    {
        if (_paused)
            return;

        _passGraph.OnPassBegin(_currentView, in pass);
        SnapshotCapturer.OnPassBegin();
    }

    void IProfiler.EndPass(in PassInfo pass)
    {
        if (_paused)
            return;

        _timing.OnPassEnd(in pass);
    }

    void IProfiler.RecordPassRead(in PassInfo pass, RenderResourceID resource, RenderTexture? texture, DeviceBuffer? buffer)
    {
        if (_paused)
            return;
        _passGraph.OnPassRead(_currentView, in pass, resource, texture, buffer);
        SnapshotCapturer.OnPassRead(in pass, resource, texture, buffer);
    }

    void IProfiler.BeginSample(string name)
    {
        if (_paused)
            return;
        _timing.OnSampleBegin(name);
    }

    void IProfiler.EndSample()
    {
        if (_paused)
            return;
        _timing.OnSampleEnd();
    }

    void IProfiler.Capture(in PassInfo pass, IReadOnlyList<Framebuffer> passOutputs, TransferCommandBuffer transfer)
    {
        if (_paused)
            return;

        Snapshot? snapshot = CaptureHandler?.Invoke(in pass, passOutputs, transfer);
        if (snapshot != null)
            SnapshotCaptured?.Invoke(snapshot);
    }

    void IProfiler.RecordDraw(in CommandBufferInfo commandBuffer, in DrawCallInfo info)
    {
        if (_paused)
            return;
        _passGraph.OnCommandBufferSeen(_currentView, in commandBuffer);
        _counters.OnDraw(in info);
        DrawHierarchy.OnDraw(_currentView, in info);
    }

    void IProfiler.RecordDrawBuffers(in CommandBufferInfo commandBuffer, in DrawBufferInfo info)
    {
        if (_paused)
            return;
        SnapshotCapturer.OnDrawBuffers(in info);
        DrawHierarchy.OnDrawBuffers(_currentView, in info);
    }

    void IProfiler.RecordDispatch(in CommandBufferInfo commandBuffer, in DispatchCallInfo info)
    {
        if (_paused)
            return;
        _passGraph.OnCommandBufferSeen(_currentView, in commandBuffer);
        _counters.OnDispatch(in info);
        DrawHierarchy.OnDispatch(_currentView, in info);
    }

    void IProfiler.RecordPipelineSwitch(in CommandBufferInfo commandBuffer, in PipelineBindInfo info)
    {
        if (_paused)
            return;

        _passGraph.OnCommandBufferSeen(_currentView, in commandBuffer);

        string passName = "", variant = "", materialName = "";
        IReadOnlyDictionary<string, string>? tags = null;
        if (_pendingShaderBind is { } pending)
        {
            passName = pending.PassName;
            variant = pending.Variant;
            tags = pending.Tags;
            materialName = pending.MaterialName;
            _pendingShaderBind = null;
        }

        DrawHierarchy.OnPipelineSwitch(_currentView, in commandBuffer, in info, passName, variant, tags, materialName);
    }

    void IProfiler.RecordResourceSetBind(uint setCount)
    {
        if (_paused)
            return;
        _counters.OnResourceSetBind(setCount);
    }

    void IProfiler.RecordBarrier(BarrierBin kind, uint count)
    {
        if (_paused)
            return;
        _counters.OnBarrier(kind, count);
    }

    void IProfiler.RecordSubmit(in ProfilerSubmitInfo info)
    {
        if (_paused)
            return;
        _counters.OnSubmit(in info);
    }

    void IProfiler.RecordExecutionTime(PassInfo? pass, ulong commandBufferId, string bufferName, bool isTransfer, double milliseconds)
    {
        if (_paused)
            return;
        _timing.OnExecutionTime(pass, commandBufferId, bufferName, isTransfer, milliseconds);
    }
}

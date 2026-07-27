using System;
using System.Collections.Generic;
using System.Diagnostics;

using Prowl.Editor.Profiling.Scene;
using Prowl.Graphite;
using Prowl.Runtime;
using Prowl.Runtime.Rendering;

namespace Prowl.Editor.Profiling;


public delegate Snapshot? SnapshotCaptureHandler(in PassInfo pass, IReadOnlyList<Framebuffer> passOutputs, TransferCommandBuffer transfer);


public sealed class EditorProfiler : IProfiler
{
    public const int RingSize = 240;

    private readonly TimingCollector _timing = new();
    private readonly CountersCollector _counters = new();
    private readonly PassGraphCollector _passGraph = new();

    public DrawHierarchyCollector DrawHierarchy { get; } = new();
    public SnapshotCapturer SnapshotCapturer { get; } = new();

    public static EditorProfiler? Instance { get; private set; }

    public static void AttachShared(GraphicsDevice device)
    {
        Instance ??= new EditorProfiler();
        Instance.Attach(device);
    }

    private GraphicsDevice? _device;

    private bool _paused = true;
    private bool _captureArmedNext;
    private bool _captureActiveThisFrame;

    private bool ShouldRecord => !_paused;

    public bool IsCaptureArmed => _captureArmedNext || _captureActiveThisFrame;

    private long _frameIndex = -1;
    private long _sealedCount;

    private readonly Stopwatch _frameStopwatch = new();

    private readonly ProfiledFrame?[] _ring = new ProfiledFrame?[RingSize];
    private ProfiledFrame? _current;

    private readonly IReadOnlyList<string> _counterNames;
    private readonly Dictionary<string, int> _counterIndex;

    private string _currentView = "";

    private ShaderBindMetadata? _pendingShaderBind;

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

        Window.FrameBegin += BeginFrame;
        Window.FrameEnd += EndFrame;

        SnapshotCapturer.Attach(device);
    }

    public void Detach()
    {
        SnapshotCapturer.Detach();
        Window.FrameBegin -= BeginFrame;
        Window.FrameEnd -= EndFrame;
        _device?.SetProfiler(null);
        _device = null;
    }

    // Prowl-side markers

    public void BeginFrame()
    {
        if (!ShouldRecord)
            return;

        _frameIndex++;

        _currentView = "";
        _pendingShaderBind = null;

        _captureActiveThisFrame = _captureArmedNext;
        _captureArmedNext = false;

        int slot = (int)(_frameIndex % RingSize);
        ProfiledFrame frame = _ring[slot] ??= new ProfiledFrame();
        frame.Reset(_frameIndex, _captureActiveThisFrame);
        if (_device != null)
            frame.SetVramBudget(_device.GetMemoryBudget());
        _current = frame;

        _frameStopwatch.Restart();

        _timing.OnFrameBegin();
        _counters.OnFrameBegin();
        _passGraph.OnFrameBegin(frame, _captureActiveThisFrame);
        DrawHierarchy.OnFrameBegin(frame, _captureActiveThisFrame);
        SnapshotCapturer.OnFrameBegin();
    }

    public void EndFrame()
    {
        if (!ShouldRecord)
            return;

        if (_current is null)
            return;

        _frameStopwatch.Stop();
        double ms = _frameStopwatch.Elapsed.TotalMilliseconds;
        ProfiledFrame frame = _current;
        frame.FrameMilliseconds = ms;
        frame.Fps = ms > 0.0 ? 1000.0 / ms : 0.0;

        DrawHierarchy.FinalizeFrame();
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

    // Control

    public bool IsPaused => _paused;
    public void Pause() => _paused = true;
    public void Resume() => _paused = false;

    public void RequestCaptureNextFrame()
    {
        if (!_paused)
            _captureArmedNext = true;
    }

    public SnapshotCaptureHandler? CaptureHandler { get; set; }

    public Func<ProfiledFrame, Snapshot?>? CaptureFinalizeHandler { get; set; }

    // Read

    public IReadOnlyList<ProfiledFrame> History
    {
        get
        {
            long start = Math.Max(0, _sealedCount - (RingSize - 1));
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
        if (n < 0 || n >= RingSize)
            return null;

        long frameIndex = _sealedCount - 1 - n;
        return GetFrame(frameIndex);
    }

    public IReadOnlyList<string> CounterNames => _counterNames;

    public IReadOnlyList<double> CounterHistory(string name)
    {
        if (!_counterIndex.TryGetValue(name, out int index))
            return Array.Empty<double>();
        long start = Math.Max(0, _sealedCount - (RingSize - 1));
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

    bool IProfiler.RequestCapture => _captureActiveThisFrame;

    bool IProfiler.RequestGPUStatistics => !_paused;

    void IProfiler.Allocate(AllocBin type, long bytes) => _counters.OnAllocate(type, bytes);

    void IProfiler.Free(AllocBin type, long bytes) => _counters.OnFree(type, bytes);

    void IProfiler.AllocateMemory(BufferRoleBin role, long bytes) => _counters.OnAllocateMemory(role, bytes);

    void IProfiler.FreeMemory(BufferRoleBin role, long bytes) => _counters.OnFreeMemory(role, bytes);

    void IProfiler.Record(BufferOpBin op, long bytes)
    {
        if (!ShouldRecord)
            return;
        _counters.OnBufferOp(op, bytes);
    }

    void IProfiler.RecordSwap(SwapBin evt, long bytes)
    {
        if (!ShouldRecord)
            return;
        _counters.OnSwap(evt, bytes);
    }

    void IProfiler.BeginView(in ViewInfo view)
    {
        if (!ShouldRecord)
            return;

        _currentView = view.Name;
        _current?.View(view.Name).SetPixelSize(view.PixelWidth, view.PixelHeight);
        DrawHierarchy.OnViewBegin(view.Name);
    }

    void IProfiler.EndView(in ViewInfo view)
    {
        if (!ShouldRecord)
            return;

        DrawHierarchy.OnViewEnd();
        _currentView = "";
    }

    void IProfiler.BeginPass(in PassInfo pass)
    {
        if (!ShouldRecord)
            return;

        _passGraph.OnPassBegin(_currentView, in pass);
        SnapshotCapturer.OnPassBegin();
    }

    void IProfiler.EndPass(in PassInfo pass)
    {
    }

    void IProfiler.RecordPassRead(in PassInfo pass, RenderResourceID resource, RenderTexture? texture, DeviceBuffer? buffer)
    {
        if (!ShouldRecord)
            return;
        _passGraph.OnPassRead(_currentView, in pass, resource, texture, buffer);
        SnapshotCapturer.OnPassRead(in pass, resource, texture, buffer);
    }

    void IProfiler.Capture(in PassInfo pass, IReadOnlyList<Framebuffer> passOutputs, TransferCommandBuffer transfer)
    {
        if (!ShouldRecord)
            return;

        Snapshot? snapshot = CaptureHandler?.Invoke(in pass, passOutputs, transfer);
        if (snapshot != null)
            SnapshotCaptured?.Invoke(snapshot);
    }

    void IProfiler.RecordDraw(in CommandBufferInfo commandBuffer, in DrawCallInfo info)
    {
        if (!ShouldRecord)
            return;
        _passGraph.OnCommandBufferSeen(_currentView, in commandBuffer);
        _counters.OnDraw(in info);
        DrawHierarchy.OnDraw(_currentView, in info);
    }

    void IProfiler.RecordDrawBuffers(in CommandBufferInfo commandBuffer, in DrawBufferInfo info)
    {
        if (!ShouldRecord)
            return;
        SnapshotCapturer.OnDrawBuffers(in info);
        DrawHierarchy.OnDrawBuffers(_currentView, in info);
    }

    void IProfiler.RecordDispatch(in CommandBufferInfo commandBuffer, in DispatchCallInfo info)
    {
        if (!ShouldRecord)
            return;
        _passGraph.OnCommandBufferSeen(_currentView, in commandBuffer);
        _passGraph.OnDispatch(_currentView, in commandBuffer);
        _counters.OnDispatch(in info);
        DrawHierarchy.OnDispatch(_currentView, in info);
    }

    void IProfiler.RecordPipelineSwitch(in CommandBufferInfo commandBuffer, in PipelineBindInfo info)
    {
        if (!ShouldRecord)
            return;

        _passGraph.OnPipelineSwitch(_currentView, in commandBuffer);

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

    bool IProfiler.RequestMetadata => ShouldRecord;

    void IProfiler.RecordPassMetadata(in PassInfo pass, object metadata)
    {
    }

    void IProfiler.RecordDrawMetadata(in CommandBufferInfo commandBuffer, object metadata)
    {
        if (!ShouldRecord)
            return;

        switch (metadata)
        {
            case ShaderBindMetadata shaderBind:
                _pendingShaderBind = shaderBind;
                break;

            case RenderableMetadata renderable:
                DrawHierarchy.OnRenderableMetadata(_currentView, in commandBuffer, in renderable);
                break;
        }
    }

    void IProfiler.RecordResourceSetBind(uint setCount)
    {
        if (!ShouldRecord)
            return;
        _counters.OnResourceSetBind(setCount);
    }

    void IProfiler.RecordBarrier(BarrierBin kind, uint count)
    {
        if (!ShouldRecord)
            return;
        _counters.OnBarrier(kind, count);
    }

    void IProfiler.RecordSubmit(in CommandBufferInfo info, bool isTransfer)
    {
        if (!ShouldRecord)
            return;
        _counters.OnSubmit(isTransfer);
        _passGraph.OnCommandBufferSubmitted(_currentView, in info, isTransfer);
    }

    void IProfiler.RecordExecutionTime(in CommandBufferInfo info, bool isTransfer, double milliseconds)
    {
        if (!ShouldRecord)
            return;
        _timing.OnExecutionTime(info, isTransfer, milliseconds);
    }

    void IProfiler.RecordGpuVertexStats(in CommandBufferInfo info, in GpuVertexStats stats)
    {
        if (!ShouldRecord)
            return;

        _timing.OnGpuVertexStats(info, in stats);
    }
}

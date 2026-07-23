using System.Collections.Generic;

using Prowl.Graphite;
using Prowl.Runtime.Rendering;

namespace Prowl.Editor.Profiling.Scene;

/// <summary>
/// Bridges <see cref="RenderProfilerHooks.Sink"/> (Prowl.Runtime's marker stream) into
/// <see cref="EditorProfiler"/>, and builds the PipelineSwitch/CallingObject/DrawCall layer of the
/// hierarchy directly into the frame's <see cref="ProfiledFrame"/> as events stream in. Only runs when
/// a capture is armed for the frame - on every other frame these hooks are no-ops beyond the always-on
/// object counters, which is what keeps live recording capped at the CommandBuffer depth.
///
/// Correlation: a <see cref="RenderableRecord"/> arrives (see <see cref="Renderable"/>) after the
/// draws it produced have already fired OnDraw/OnDispatch, so this collector buffers draws for the
/// currently-open switch and, once a Renderable arrives, claims every buffered draw since the last
/// claim into a new CallingObject node under that switch.
///
/// Not every draw gets a Renderable, though - a post-process blit, a fullscreen triangle, a
/// user-invoked immediate draw all issue OnDraw/OnDispatch with no correlating marker ever coming.
/// Pending is scoped to "whatever's buffered under the current switch, not yet claimed"; anything
/// still sitting there when the switch changes (or the view/frame ends without one) is flushed
/// straight onto that switch's own <see cref="ProfiledPipelineSwitch.Draws"/> instead of being
/// silently dropped or, worse, wrongly claimed by some later, unrelated Renderable.
/// </summary>
public sealed class DrawHierarchyCollector : IRenderProfilerSink
{
    private sealed class ViewState
    {
        public ProfiledPipelineSwitch? CurrentSwitch;
        public readonly List<ProfiledDrawCall> Pending = new();
        public int Boundary;

        public void Reset()
        {
            CurrentSwitch = null;
            Pending.Clear();
            Boundary = 0;
        }
    }

    private EditorProfiler? _profiler;
    private ProfiledFrame? _frame;
    private bool _armed;
    private readonly Dictionary<string, ViewState> _views = new();
    private string _currentView = "";

    /// <summary>
    /// This collector is owned by the <see cref="EditorProfiler"/> it forwards markers into; call
    /// <see cref="Attach"/> once both objects exist.
    /// </summary>
    public void Attach(EditorProfiler profiler)
    {
        _profiler = profiler;
        RenderProfilerHooks.Sink = this;
    }

    /// <summary>Clears <see cref="RenderProfilerHooks.Sink"/> if it still points at this instance.</summary>
    public void Detach()
    {
        if (ReferenceEquals(RenderProfilerHooks.Sink, this))
            RenderProfilerHooks.Sink = null;
        _profiler = null;
    }

    // IRenderProfilerSink - forwarded straight to EditorProfiler markers.

    void IRenderProfilerSink.BeginFrame() => _profiler?.BeginFrame();
    void IRenderProfilerSink.EndFrame() => _profiler?.EndFrame();
    void IRenderProfilerSink.BeginView(string name) => _profiler?.BeginView(name);
    void IRenderProfilerSink.EndView() => _profiler?.EndView();
    void IRenderProfilerSink.ShaderBind(in ShaderBindRecord r) => _profiler?.NoteShaderBind(in r);

    void IRenderProfilerSink.Renderable(in RenderableRecord r)
    {
        if (_currentView.Length == 0)
            return;

        ViewState state = GetOrCreateView(_currentView);

        _frame?.View(_currentView).AddObjectCounts(r.Registered, r.Culled, r.DrawCallCount);

        if (state.Pending.Count == state.Boundary || state.CurrentSwitch == null)
        {
            state.Boundary = state.Pending.Count;
            return;
        }

        string label = r.MeshName.Length == 0 && r.MaterialName.Length == 0
            ? ""
            : $"{r.MeshName} / {r.MaterialName}";

        ProfiledCallingObject obj = state.CurrentSwitch.AddObject(label, r.MaterialName, r.MeshName, r.Layer, r.Position, r.Registered, r.Culled);
        for (int i = state.Boundary; i < state.Pending.Count; i++)
            obj.AddDraw(state.Pending[i]);

        state.Boundary = state.Pending.Count;
    }

    // Profiler dispatch

    public void OnFrameBegin(ProfiledFrame frame, bool armed)
    {
        _frame = frame;
        _armed = armed;
        foreach (ViewState state in _views.Values)
            state.Reset();
        _currentView = "";
    }

    public void OnViewBegin(string view) => _currentView = view;

    public void OnViewEnd()
    {
        if (_views.TryGetValue(_currentView, out ViewState? state))
            FlushLooseDraws(state);
        _currentView = "";
    }

    /// <summary>Called once per frame from EditorProfiler.EndFrame, after the last view has closed -
    /// catches any switch's leftover pending draws that never got a chance to flush via OnViewEnd
    /// (e.g. a view that never explicitly ends before the frame does).</summary>
    public void FinalizeFrame()
    {
        foreach (ViewState state in _views.Values)
            FlushLooseDraws(state);
    }

    /// <summary>Moves every not-yet-claimed pending draw straight onto the current switch's own
    /// Draws list, then clears Pending/Boundary. Safe to call repeatedly or with nothing pending.</summary>
    private static void FlushLooseDraws(ViewState state)
    {
        if (state.CurrentSwitch != null)
            for (int i = state.Boundary; i < state.Pending.Count; i++)
                state.CurrentSwitch.AddDraw(state.Pending[i]);

        state.Pending.Clear();
        state.Boundary = 0;
    }

    public void OnPipelineSwitch(
        string currentView, in CommandBufferInfo commandBuffer, in PipelineBindInfo info,
        string passName, string variant, IReadOnlyDictionary<string, string>? tags, string materialName)
    {
        if (!_armed || _frame == null || commandBuffer.Pass is not { } pass)
            return;

        ViewState state = GetOrCreateView(currentView);
        FlushLooseDraws(state);

        ProfiledPipelineState? pstate = BuildState(info);

        ProfiledCommandBuffer cb = _frame.View(currentView).Pass(pass.Index, pass.Name).CommandBuffer(commandBuffer.Id, commandBuffer.Name);
        ProfiledPipelineSwitch sw = cb.AddSwitch(info.ShaderName, info.IsCompute, info.Stages, passName, variant, tags, materialName, pstate);

        state.CurrentSwitch = sw;
    }

    public void OnDraw(string currentView, in DrawCallInfo info)
        => AddPending(currentView, new ProfiledDrawCall(info, null, false, System.Array.Empty<ReferenceBuffer>()));

    public void OnDispatch(string currentView, in DispatchCallInfo info)
        => AddPending(currentView, new ProfiledDrawCall(null, info, false, System.Array.Empty<ReferenceBuffer>()));

    private void AddPending(string currentView, ProfiledDrawCall draw)
    {
        if (!_armed)
            return;

        ViewState state = GetOrCreateView(currentView);
        if (state.CurrentSwitch == null)
            return;

        state.Pending.Add(draw);
    }

    public void OnDrawBuffers(string currentView, in DrawBufferInfo info)
    {
        if (!_armed)
            return;

        ViewState state = GetOrCreateView(currentView);
        if (state.Pending.Count == 0)
            return;

        var refs = new List<ReferenceBuffer>();
        foreach (BufferBindingInfo vb in info.VertexBuffers)
            refs.Add(ToReferenceBuffer(vb));
        if (info.IndexBuffer is { } ib)
            refs.Add(ToReferenceBuffer(ib));
        foreach (BufferBindingInfo b in info.BoundBuffers)
            refs.Add(ToReferenceBuffer(b));

        int last = state.Pending.Count - 1;
        state.Pending[last] = state.Pending[last] with { ReferenceBuffers = refs.ToArray() };
    }

    private static ReferenceBuffer ToReferenceBuffer(in BufferBindingInfo b)
    {
        uint id = (uint)b.Buffer.GetHashCode() ^ b.Offset;
        var resource = new SnapshotResourceID(id, b.ContentVersion, true);
        return new ReferenceBuffer(b.Name, b.SizeInBytes, b.ContentVersion, b.ReadOnly, resource);
    }

    private static ProfiledPipelineState? BuildState(in PipelineBindInfo info)
    {
        if (info.Program is GraphicsProgram gp)
            return new ProfiledPipelineState(gp.BlendState, gp.DepthStencilState, gp.RasterizerState, null, null, null);

        if (info.Program is ComputeProgram cp)
            return new ProfiledPipelineState(null, null, null, cp.ThreadGroupSizeX, cp.ThreadGroupSizeY, cp.ThreadGroupSizeZ);

        return null;
    }

    private ViewState GetOrCreateView(string view)
    {
        if (!_views.TryGetValue(view, out ViewState? state))
        {
            state = new ViewState();
            _views[view] = state;
        }
        return state;
    }
}

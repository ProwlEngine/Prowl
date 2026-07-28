using System.Collections.Generic;

using Prowl.Graphite;
using Prowl.Runtime.Rendering;

namespace Prowl.Editor.Profiling.Scene;


public sealed class DrawHierarchyCollector
{
    private sealed class ViewState
    {
        public ProfiledPipeline? CurrentSwitch;
        public readonly List<ProfiledDrawCall> Pending = new();
        public int Boundary;

        public void Reset()
        {
            CurrentSwitch = null;
            Pending.Clear();
            Boundary = 0;
        }
    }

    private ProfiledFrame? _frame;
    private bool _armed;
    private readonly Dictionary<string, ViewState> _views = new();
    private string _currentView = "";


    public void OnRenderableMetadata(string currentView, in CommandBufferInfo commandBuffer, in RenderableMetadata r)
    {
        if (currentView.Length == 0)
            return;

        ViewState state = GetOrCreateView(currentView);

        ProfiledView? view = _frame?.View(currentView);
        view?.AddObjectCounts(r.Registered, r.Culled);
        if (commandBuffer.Pass is { } pass)
        {
            ProfiledPass? passObj = view?.Pass(pass.Index, pass.Name);
            passObj?.AddObjectCounts(r.Registered, r.Culled);
            passObj?.CommandBuffer(commandBuffer.Id, commandBuffer.Name).AddDrawCalls(r.DrawCallCount);
        }

        if (state.Pending.Count == state.Boundary || state.CurrentSwitch == null)
        {
            state.Boundary = state.Pending.Count;
            return;
        }

        string label = r.MeshName.Length == 0 && r.MaterialName.Length == 0
            ? ""
            : $"{r.MeshName} / {r.MaterialName}";

        ProfiledPipeline sw = state.CurrentSwitch;
        int drawStart = sw.Draws.Count;
        for (int i = state.Boundary; i < state.Pending.Count; i++)
            sw.AddDraw(state.Pending[i]);
        int drawEnd = sw.Draws.Count;

        sw.AddObject(label, r.MaterialName, r.MeshName, r.Layer, r.Position, r.Registered, r.Culled, drawStart, drawEnd);

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

    public void FinalizeFrame()
    {
        foreach (ViewState state in _views.Values)
            FlushLooseDraws(state);
    }

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
        ProfiledPipeline sw = cb.AddSwitch(info.ShaderName, info.IsCompute, info.Stages, passName, variant, tags, materialName, pstate);

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

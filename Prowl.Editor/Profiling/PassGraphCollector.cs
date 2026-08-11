using System.Collections.Generic;

using Prowl.Graphite;

namespace Prowl.Editor.Profiling;

/// <summary>
/// Reconstructs the per-view Pass graph (nodes + edges + inputs/outputs) and the CommandBuffer
/// identity layer nested under each pass, writing straight into the frame's live
/// <see cref="ProfiledView"/>/<see cref="ProfiledPass"/>/<see cref="ProfiledCommandBuffer"/> nodes as
/// events arrive - the same nodes <see cref="DrawHierarchyCollector"/> nests PipelineSwitches under.
/// Runs every frame regardless of whether a capture is armed - this is the "up to CommandBuffer" depth
/// every live frame keeps.
///
/// Pass ordering within a view is strictly sequential (a pass fully begins/reads/ends before the next
/// one begins), so producer/consumer edges can be detected the moment a later pass reads a resource an
/// earlier pass in this view already wrote - each view's <see cref="ViewState"/> is the transient
/// bookkeeping that makes that possible; it is not profiler data itself; only the resulting
/// <see cref="ProfiledView.Edges"/> are. View names are a small, stable set, so a ViewState persists
/// across frames (just Reset() each frame) instead of being discarded and rebuilt.
///
/// GPU time and pipeline-statistics per command buffer round-trip from the GPU well after this frame has
/// sealed (see <see cref="TimingCollector"/>), so this collector doesn't wait for them: every command
/// buffer node is registered with <see cref="TimingCollector.Track"/> the moment it's touched, and
/// <see cref="TimingCollector"/> stamps the result onto that exact node whenever it actually arrives -
/// however many frames later that is - instead of this collector doing an end-of-frame stamping pass.
/// </summary>
public sealed class PassGraphCollector
{
    private sealed class ViewState
    {
        public readonly Dictionary<uint, int> ProducedBy = new();
        public readonly Dictionary<uint, uint> TextureLastWrittenVersion = new();

        public void Reset()
        {
            ProducedBy.Clear();
            TextureLastWrittenVersion.Clear();
        }
    }

    private readonly TimingCollector _timing;
    private readonly Dictionary<string, ViewState> _viewStates = new();
    private readonly HashSet<string> _touchedViews = new();

    private ProfiledFrame? _frame;
    private bool _armed;

    public PassGraphCollector(TimingCollector timing)
    {
        _timing = timing;
    }

    public void OnFrameBegin(ProfiledFrame frame, bool armed)
    {
        _frame = frame;
        _armed = armed;
        foreach (ViewState state in _viewStates.Values)
            state.Reset();
        _touchedViews.Clear();
    }

    public void OnPassBegin(string currentView, in PassInfo p)
    {
        if (_frame == null)
            return;

        _touchedViews.Add(currentView);
        ProfiledPass pass = _frame.View(currentView).Pass(p.Index, p.Name);

        foreach (RenderResourceID id in p.Inputs.Span)
            pass.AddInputPlaceholder(new ResourceRef((uint)id.GetHashCode(), "", ResourceRefKind.Unknown, SnapshotResourceID.Invalid));
        foreach (RenderResourceID id in p.Outputs.Span)
            pass.AddOutputPlaceholder(new ResourceRef((uint)id.GetHashCode(), "", ResourceRefKind.Unknown, SnapshotResourceID.Invalid));
    }

    public void OnPassRead(string currentView, in PassInfo p, RenderResourceID id, RenderTexture? texture, DeviceBuffer? buffer)
    {
        if (_frame == null)
            return;

        ProfiledView view = _frame.View(currentView);
        ProfiledPass pass = view.Pass(p.Index, p.Name);

        uint hashId = (uint)id.GetHashCode();
        bool referencedAsOutput = pass.HasOutput(hashId);
        bool referencedAsInput = pass.HasInput(hashId);

        ViewState state = GetOrCreateViewState(currentView);

        if (texture != null)
        {
            string name = texture.Framebuffer.Name;

            if (referencedAsOutput)
            {
                SnapshotResourceID outId = _armed ? new SnapshotResourceID(hashId, (uint)p.Index, true) : SnapshotResourceID.Invalid;
                pass.UpsertOutput(hashId, name, ResourceRefKind.Texture, outId);
                if (_armed)
                    state.TextureLastWrittenVersion[hashId] = (uint)p.Index;
                state.ProducedBy[hashId] = p.Index;
            }
            if (referencedAsInput)
            {
                uint version = state.TextureLastWrittenVersion.TryGetValue(hashId, out uint v) ? v : 0;
                SnapshotResourceID inId = _armed ? new SnapshotResourceID(hashId, version, true) : SnapshotResourceID.Invalid;
                ResourceRef updated = pass.UpsertInput(hashId, name, ResourceRefKind.Texture, inId);
                AddEdgeIfProduced(view, state, hashId, p.Index, updated);
            }
        }
        else if (buffer != null)
        {
            string name = buffer.Name;
            SnapshotResourceID bufId = _armed ? new SnapshotResourceID(hashId, buffer.ContentVersion, true) : SnapshotResourceID.Invalid;

            if (referencedAsOutput)
            {
                pass.UpsertOutput(hashId, name, ResourceRefKind.Buffer, bufId);
                state.ProducedBy[hashId] = p.Index;
            }
            if (referencedAsInput)
            {
                ResourceRef updated = pass.UpsertInput(hashId, name, ResourceRefKind.Buffer, bufId);
                AddEdgeIfProduced(view, state, hashId, p.Index, updated);
            }
        }
    }

    private static void AddEdgeIfProduced(ProfiledView view, ViewState state, uint resourceId, int toPass, ResourceRef resource)
    {
        if (state.ProducedBy.TryGetValue(resourceId, out int fromPass) && fromPass != toPass)
            view.AddEdge(new PassEdge(fromPass, toPass, resource));
    }

    /// <summary>Records that a CommandBuffer exists, whenever any event that carries CommandBufferInfo
    /// fires (switch/draw/dispatch). Runs regardless of armed - CommandBuffer identity is always-on, up
    /// to this depth every live frame keeps.</summary>
    public void OnCommandBufferSeen(string currentView, in CommandBufferInfo cb)
    {
        if (_frame == null || cb.Pass is not { } pass)
            return;

        _touchedViews.Add(currentView);
        ProfiledCommandBuffer node = _frame.View(currentView).Pass(pass.Index, pass.Name).CommandBuffer(cb.Id, cb.Name);
        _timing.Track(cb.Id, _frame.FrameIndex, node);
    }


    public void OnCommandBufferSubmitted(string currentView, in CommandBufferInfo cb, bool isTransfer)
    {
        if (_frame == null)
            return;

        if (cb.Pass is { } pass)
        {
            _touchedViews.Add(currentView);
            ProfiledCommandBuffer node = _frame.View(currentView).Pass(pass.Index, pass.Name).CommandBuffer(cb.Id, cb.Name);
            _timing.Track(cb.Id, _frame.FrameIndex, node);
        }
        else
        {
            ProfiledCommandBuffer node = _frame.FreeCommandBuffer(cb.Id, cb.Name);
            _timing.Track(cb.Id, _frame.FrameIndex, node);
        }
    }

    /// <summary>Bumps DispatchCallCount on the view/pass a dispatch landed in - always-on, unlike
    /// DrawCallCount which comes from scene RenderableMetadata (dispatches aren't scene renderables).</summary>
    public void OnDispatch(string currentView, in CommandBufferInfo cb)
    {
        if (_frame == null || cb.Pass is not { } pass)
            return;

        _touchedViews.Add(currentView);
        ProfiledView view = _frame.View(currentView);
        ProfiledPass passObj = view.Pass(pass.Index, pass.Name);
        ProfiledCommandBuffer node = passObj.CommandBuffer(cb.Id, cb.Name);
        node.AddDispatchCount();
        passObj.AddDispatchCount();
        view.AddDispatchCount();
        _timing.Track(cb.Id, _frame.FrameIndex, node);
    }

    /// <summary>Bumps the switch count on the command buffer a pipeline bind landed on - always-on,
    /// unlike DrawHierarchyCollector.OnPipelineSwitch which only builds the capture-tier
    /// ProfiledPipelineSwitch (shader/material identity) when a capture is armed.</summary>
    public void OnPipelineSwitch(string currentView, in CommandBufferInfo cb)
    {
        if (_frame == null || cb.Pass is not { } pass)
            return;

        _touchedViews.Add(currentView);
        ProfiledCommandBuffer node = _frame.View(currentView).Pass(pass.Index, pass.Name).CommandBuffer(cb.Id, cb.Name);
        node.IncrementPipelineSwitchCount();
        _timing.Track(cb.Id, _frame.FrameIndex, node);
    }

    private ViewState GetOrCreateViewState(string name)
    {
        if (!_viewStates.TryGetValue(name, out ViewState? state))
        {
            state = new ViewState();
            _viewStates[name] = state;
        }
        return state;
    }
}

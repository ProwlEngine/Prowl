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
/// The one thing that can't be known until the whole frame is over is GPU time per command buffer
/// (round-trips from the GPU, see <see cref="TimingCollector"/>), so <see cref="FinalizeFrame"/> does a
/// single end-of-frame pass to stamp that onto each command buffer node.
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

    private readonly Dictionary<string, ViewState> _viewStates = new();
    private readonly HashSet<string> _touchedViews = new();

    private ProfiledFrame? _frame;
    private bool _armed;

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
        _frame.View(currentView).Pass(pass.Index, pass.Name).CommandBuffer(cb.Id, cb.Name);
    }

    public void FinalizeFrame(ProfiledFrame frame, TimingCollector timing)
    {
        // Pass/View GpuMilliseconds are computed properties (sum of their CommandBuffers/Passes), so
        // there's nothing to roll up here beyond stamping the leaf-level number onto each command buffer.
        foreach (string viewName in _touchedViews)
        {
            ProfiledView view = frame.View(viewName);
            foreach (ProfiledPass pass in view.Passes)
                foreach (ProfiledCommandBuffer cb in pass.CommandBuffers)
                    cb.SetGpuMs(timing.GetCommandBufferGpuMs(cb.Id));
        }
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

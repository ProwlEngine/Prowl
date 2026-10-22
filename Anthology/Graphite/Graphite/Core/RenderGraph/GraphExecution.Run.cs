using System;
using System.Collections.Generic;
using System.Diagnostics;

using Prowl.Graphite.Debugging;

namespace Prowl.Graphite;

internal sealed partial class GraphExecution
{
    private readonly List<int> _openGroups = new();
    private readonly List<int> _chain = new();
    private Dictionary<int, GraphCapture>? _captures;

    public void Run(GraphLayout layout, PassContext context)
    {
        IGraphProfiler? profiler = Task.Profilers.Graph;
        try
        {
            for (int i = 0; i < Graph.PassCount; i++)
            {
                FramePass pass = Graph.PassAt(i);
                EnterGroups(layout, profiler, pass.Group);
                RunPass(layout, context, profiler, pass, i);
            }
        }
        finally
        {
            LeaveGroups(layout, profiler, 0);
        }
    }

    private void EnterGroups(GraphLayout layout, IGraphProfiler? profiler, int group)
    {
        _chain.Clear();
        for (int g = group; g >= 0; g = layout.Groups[g].ParentIndex)
            _chain.Add(g);
        _chain.Reverse();

        int common = 0;
        while (common < _openGroups.Count && common < _chain.Count && _openGroups[common] == _chain[common])
            common++;

        LeaveGroups(layout, profiler, common);
        for (int i = common; i < _chain.Count; i++)
        {
            _openGroups.Add(_chain[i]);
            GroupInfo info = layout.Groups[_chain[i]];
            profiler?.BeginGroup(in info);
        }
    }

    private void LeaveGroups(GraphLayout layout, IGraphProfiler? profiler, int keep)
    {
        while (_openGroups.Count > keep)
        {
            GroupInfo info = layout.Groups[_openGroups[^1]];
            _openGroups.RemoveAt(_openGroups.Count - 1);
            profiler?.EndGroup(in info);
        }
    }

    private void RunPass(GraphLayout layout, PassContext context, IGraphProfiler? profiler, FramePass pass, int index)
    {
        PassInfo passInfo = layout.PassInfos[index];
        GraphCapture? capture = CaptureOf(layout, pass.Group);
        CommandBuffer? passCommands = null;
        bool begun = false, submitted = false, ended = false;
        try
        {
            profiler?.BeginPass(passInfo);
            begun = true;
            capture?.BeginPass(index);
            TransitionForAccesses(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(pass.Accesses));
            passCommands = BeginPassCommandBuffer(pass.Name, passInfo, System.Runtime.InteropServices.CollectionsMarshal.AsSpan(pass.Accesses));
            passCommands.PushDebugGroup(pass.Name);
            BindPass(passCommands, pass, index);
            long recordStart = Stopwatch.GetTimestamp();
            context.Begin(this, pass, passCommands);
            pass.Render.Invoke(context);
            context.End();
            passCommands.PopDebugGroup();
            PassStats stats = passCommands.Stats;
            submitted = true;
            EndCommandBuffer(passCommands);
            AdoptPassStates(passCommands);
            stats = stats with { CpuMilliseconds = Stopwatch.GetElapsedTime(recordStart).TotalMilliseconds };
            MarkAttachmentWrites(pass);
            capture?.EndPass(index, passCommands);
            ended = true;
            profiler?.EndPass(passInfo, stats);
        }
        catch
        {
            context.End();
            if (passCommands != null && !submitted)
            {
                EndCommandBuffer(passCommands);
                AdoptPassStates(passCommands);
            }
            if (begun && !ended)
                profiler?.EndPass(passInfo, passCommands?.Stats ?? default);
            throw;
        }
    }

    private void BindPass(CommandBuffer cmd, FramePass pass, int index)
    {
        PassBindings bindings = Bindings;
        bindings.Reset(pass.Name);
        int single = -1;
        bool several = false;
        foreach (ResourceAccess access in pass.Accesses)
        {
            FrameResource resource = Graph.ResourceAt(access.Resource);
            if (Validate)
            {
                if (access.IsTexture)
                    bindings.Declare(TextureAt(access.Resource));
                else
                    bindings.Declared.Add(BufferAt(access.Resource).ResourceId);
            }

            if (!access.IsTexture || !access.IsOutput || access.Entry != TextureState.Attachment)
                continue;

            RenderTexture texture = TextureAt(access.Resource);
            TargetLoadStoreOps ops = OpsFor(pass, access, resource, index);
            bindings.SetOps(texture.Framebuffer, ops);
            if (single >= 0 && single != access.Resource)
                several = true;
            single = access.Resource;
        }

        cmd.Bindings = bindings;
        if (single < 0 || several)
            return;

        Framebuffer framebuffer = TextureAt(single).Framebuffer;
        TargetLoadStoreOps declared = bindings.TakeOps(framebuffer) ?? OpsFor(pass, FirstAttachment(pass, single), Graph.ResourceAt(single), index);
        cmd.SetFramebuffer(framebuffer, declared);
    }

    private static ResourceAccess FirstAttachment(FramePass pass, int resource)
    {
        foreach (ResourceAccess access in pass.Accesses)
        {
            if (access.IsTexture && access.IsOutput && access.Resource == resource && access.Entry == TextureState.Attachment)
                return access;
        }
        throw new InvalidOperationException("The pass has no attachment write to that resource.");
    }

    private TargetLoadStoreOps OpsFor(FramePass pass, in ResourceAccess access, FrameResource resource, int passIndex)
    {
        TargetLoadStoreOps ops = TargetLoadStoreOps.ForLifetime(persistent: true);
        if (resource.FirstWriter == passIndex && IsFirstWriteOf(pass, access))
            ops = access.FirstWrite ?? TargetLoadStoreOps.ForLifetime(persistent: !resource.IsTransient);

        StoreAction store = !resource.IsTransient || resource.LastAccess > passIndex ? StoreAction.Store : StoreAction.DontCare;
        ops.Color.Store = store;
        ops.Depth.Store = store;
        return ops;
    }

    private static bool IsFirstWriteOf(FramePass pass, in ResourceAccess target)
    {
        foreach (ResourceAccess access in pass.Accesses)
        {
            if (access.IsTexture && access.IsOutput && access.Resource == target.Resource)
                return access.Version == target.Version;
        }
        return false;
    }

    private void MarkAttachmentWrites(FramePass pass)
    {
        foreach (ResourceAccess access in pass.Accesses)
        {
            if (!access.IsTexture || !access.IsOutput)
                continue;

            RenderTexture texture = TextureAt(access.Resource);
            if ((access.TextureUsage & TextureState.Attachment) != 0)
            {
                foreach (Texture color in texture.ColorTextures)
                    color.MarkContentChanged();
            }

            if (texture.DepthTexture != null && (access.DepthAllowed & TextureState.Attachment) != 0)
                texture.DepthTexture.MarkContentChanged();
        }
    }

    private GraphCapture? CaptureOf(GraphLayout layout, int group)
    {
        ICaptureProfiler? hook = Task.Profilers.Capture;
        if (hook == null)
            return null;

        _captures ??= new Dictionary<int, GraphCapture>();
        if (_captures.TryGetValue(group, out GraphCapture? capture))
            return capture;

        List<int> members = new();
        for (int i = 0; i < Graph.PassCount; i++)
        {
            if (Graph.PassAt(i).Group == group)
                members.Add(i);
        }

        capture = new GraphCapture(hook, this, layout, members.ToArray());
        capture.DescribeGroup(group);
        _captures[group] = capture;
        return capture;
    }

    public bool WroteTarget(FrameResource resource) => resource.IsTarget && resource.Written;
}

using System;
using System.Collections.Generic;
using System.Diagnostics;

using Prowl.Graphite.Debugging;

namespace Prowl.Graphite;

public abstract partial class GraphicsDevice
{
    /// <summary>
    /// Runs a graph as one execution: the root's passes, then every instance after its parent. Instances that wrote a swapchain target present.
    /// </summary>
    /// <param name="graph">Graph to run.</param>
    /// <param name="instances">Instances to run it for.</param>
    /// <param name="profilers">Profilers for this execution only. Empty creates one from each <see cref="GlobalProfilers"/> factory.</param>
    public ExecutionTask DispatchGraph(RenderGraph graph, GraphInstances instances, params IProfiler[] profilers)
    {
        ValidationHelpers.RequireNotNull(this, graph, nameof(graph), nameof(DispatchGraph));
        ValidationHelpers.RequireNotNull(this, instances, nameof(instances), nameof(DispatchGraph));
        foreach (GraphInstance instance in instances.All)
        {
            if (instance.Parent == null)
                continue;

            if (!graph.Contains(instance.Kind))
                throw new ArgumentException($"Instance '{instance.Name}' has kind '{instance.Kind.Name}', which graph '{graph.Name}' does not declare.", nameof(instances));

            GraphKind expected = graph.ParentOf(instance.Kind);
            if (instance.Parent.Kind != expected)
                throw new ArgumentException(
                    $"Instance '{instance.Name}' of kind '{instance.Kind.Name}' runs under kind '{instance.Parent.Kind.Name}', but graph '{graph.Name}' declares '{expected.Name}' as its parent.", nameof(instances));
        }

        ExecutionTask task = BeginExecution(graph.Name, profilers);
        GraphExecution execution = new(this, task, graph, instances);
        List<Swapchain>? presents = null;
        try
        {
            try
            {
                graph.Store.Sweep(task.Id);
                execution.RecordResizes();
                Run(execution, instances.Root, ref presents);
            }
            finally
            {
                execution.RestoreRestingStates("Execution");
            }
        }
        catch
        {
            CompleteExecution(task);
            throw;
        }

        return Finish(task, presents);
    }

    private static void Run(GraphExecution execution, GraphInstance instance, ref List<Swapchain>? presents)
    {
        RenderContext context = execution.ContextOf(instance);
        IGraphProfiler? profiler = execution.Task.Profilers.Graph;
        var info = new InstanceInfo(instance.Name, instance.Index, instance.Parent?.Index ?? -1, instance.Key, instance.Extent.Width, instance.Extent.Height);

        bool wroteTarget;
        profiler?.BeginInstance(info);
        try
        {
            wroteTarget = RunPasses(context, instance);
        }
        finally
        {
            profiler?.EndInstance(info);
        }

        if (wroteTarget && instance.Target?.OwningSwapchain is { } swapchain)
        {
            presents ??= [];
            if (!presents.Contains(swapchain))
                presents.Add(swapchain);
        }

        foreach (GraphInstance child in instance.Children)
            Run(execution, child, ref presents);
    }

    private static bool RunPasses(RenderContext context, GraphInstance instance)
    {
        RenderGraph graph = context.Graph;
        IReadOnlyList<RenderGraph.PassNode> kindNodes = graph.PassesOf(instance.Kind);
        context.ValidateReads(kindNodes);
        List<RenderGraph.PassNode> nodes = new(kindNodes.Count);
        foreach (RenderGraph.PassNode candidate in kindNodes)
        {
            if (context.IsEnabled(candidate))
                nodes.Add(candidate);
        }

        IGraphProfiler? profiler = context.Task.Profilers.Graph;
        bool hasTarget = context.HasInstanceTarget;
        bool wroteTarget = false;
        GraphCapture? capture = BeginCapture(context, nodes, hasTarget);

        int index = 0;
        foreach (RenderGraph.PassNode node in nodes)
        {
            if (node.WritesInstanceTarget && !hasTarget)
            {
                profiler?.SkipPass(node.Pass.Name, instance.Index, PassSkipReason.NoTarget);
                continue;
            }

            var passInfo = new PassInfo(node.Pass.Name, index, instance.Index, node.PublicAccesses);
            CommandBuffer? passCommands = null;
            bool begun = false, submitted = false, ended = false;
            try
            {
                profiler?.BeginPass(passInfo);
                begun = true;
                capture?.BeginPass(index);
                context.SetCurrentPass(passInfo, node.Accesses, node.Pass.Name);
                context.Execution.TransitionForAccesses(context, node.Accesses);
                passCommands = context.BeginPassCommandBuffer(node.Pass.Name);
                passCommands.PushDebugGroup(node.Pass.Name);
                context.BindDeclaredTarget(passCommands, node.Accesses);
                long recordStart = Stopwatch.GetTimestamp();
                if (node.Pass is IInstancePass instancePass)
                    instancePass.Render((InstanceContext)context, passCommands);
                else
                    ((IExecutionPass)node.Pass).Render(context, passCommands);
                passCommands.PopDebugGroup();
                PassStats stats = passCommands.Stats;
                submitted = true;
                context.EndCommandBuffer(passCommands);
                context.Execution.AdoptPassStates(passCommands);
                stats = stats with { CpuMilliseconds = Stopwatch.GetElapsedTime(recordStart).TotalMilliseconds };
                context.MarkAttachmentWrites(node.Accesses);
                context.SetCurrentPass(null);
                capture?.EndPass(index, passCommands);
                wroteTarget |= node.WritesInstanceTarget;
                index++;
                ended = true;
                profiler?.EndPass(passInfo, stats);
            }
            catch
            {
                if (passCommands != null && !submitted)
                {
                    context.EndCommandBuffer(passCommands);
                    context.Execution.AdoptPassStates(passCommands);
                }
                context.SetCurrentPass(null);
                if (begun && !ended)
                    profiler?.EndPass(passInfo, passCommands?.Stats ?? default);
                throw;
            }
        }

        return wroteTarget;
    }

    private static GraphCapture? BeginCapture(RenderContext context, IReadOnlyList<RenderGraph.PassNode> candidates, bool hasTarget)
    {
        ICaptureProfiler? hook = context.Task.Profilers.Capture;
        if (hook == null)
            return null;

        List<RenderGraph.PassNode> nodes = new();
        foreach (RenderGraph.PassNode node in candidates)
        {
            if (!node.WritesInstanceTarget || hasTarget)
                nodes.Add(node);
        }

        int instance = context.GraphInstance.Index;
        PassInfo[] infos = new PassInfo[nodes.Count];
        for (int i = 0; i < infos.Length; i++)
            infos[i] = new PassInfo(nodes[i].Pass.Name, i, instance, nodes[i].PublicAccesses);

        GraphCapture capture = new(hook, context, context.Graph, nodes.ToArray(), infos);
        capture.DescribeInstance(instance);
        return capture;
    }

    private ExecutionTask Finish(ExecutionTask task, List<Swapchain>? presents)
    {
        CompleteExecution(task);

        if (presents != null)
        {
            foreach (Swapchain swapchain in presents)
                SwapBuffers(swapchain);
        }

        return task;
    }
}

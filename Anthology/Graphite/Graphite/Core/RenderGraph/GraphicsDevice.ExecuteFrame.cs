using System;
using System.Collections.Generic;

namespace Prowl.Graphite;

public abstract partial class GraphicsDevice
{
    internal ExecutionTask ExecuteFrame(FrameGraph graph, GraphLayout layout, PassContext context, IProfiler[] profilers)
    {
        ValidationHelpers.RequireNotNull(this, profilers, nameof(profilers), nameof(FrameGraph.Execute));

        ExecutionTask task = BeginExecution(graph.Name, profilers);
        GraphExecution execution = new(this, task, graph);
        try
        {
            try
            {
                execution.Prepare();
                execution.Run(layout, context);
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

        CompleteExecution(task);
        PresentTargets(graph, execution);
        return task;
    }

    private void PresentTargets(FrameGraph graph, GraphExecution execution)
    {
        List<Swapchain>? presents = null;
        for (int i = 0; i < graph.ResourceCount; i++)
        {
            FrameResource resource = graph.ResourceAt(i);
            if (!execution.WroteTarget(resource) || resource.TargetFramebuffer!.OwningSwapchain is not { } swapchain)
                continue;

            presents ??= [];
            if (!presents.Contains(swapchain))
                presents.Add(swapchain);
        }

        if (presents == null)
            return;

        foreach (Swapchain swapchain in presents)
            SwapBuffers(swapchain);
    }
}

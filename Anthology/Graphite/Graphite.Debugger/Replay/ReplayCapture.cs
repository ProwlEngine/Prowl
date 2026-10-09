using System;
using System.Collections.Generic;
using Prowl.Graphite.Debugger.Trace;
using Prowl.Graphite.Debugging;

namespace Prowl.Graphite.Debugger;

internal sealed class ReplayCapture(string passName, HashSet<TraceResourceId> wanted, Dictionary<ResourceId, TraceResourceId> ids) : ICaptureProfiler
{
    public readonly List<(TraceResourceId Resource, CaptureCopy Copy)> Copies = new();
    public string? Failure;

    public void BeginExecution(ulong executionId) { }

    public void EndExecution() { }

    public void OnViewBegin(in ViewCaptureInfo view) { }

    public void OnViewEnd() { }

    public void OnPassEnd(in PassInfo pass, ReadOnlySpan<PassReference> references, ICaptureContext capture)
    {
        if (pass.Name != passName)
            return;

        foreach (PassReference reference in references)
        {
            if (!ids.TryGetValue(reference.First.Resource, out TraceResourceId id) || !wanted.Contains(id))
                continue;

            try
            {
                Copies.Add((id, capture.Copy(in reference, CopyPlacement.AfterPass)));
            }
            catch (NotSupportedException ex)
            {
                Failure ??= $"{reference.Name} cannot be read back: {ex.Message}";
            }
        }
    }
}

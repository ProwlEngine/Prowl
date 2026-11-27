using System;
using System.Collections.Generic;
using System.Linq;


namespace Prowl.Graphite.Samples.PBRRenderer;


/// <summary>Averages GPU milliseconds per pass over a number of frames and prints them, for --profile.</summary>
internal sealed class PassTimer
{
    private readonly object _lock = new();
    private readonly Dictionary<string, double> _gpu = new();
    private readonly Dictionary<string, double> _cpu = new();
    private readonly Dictionary<string, (ulong Triangles, ulong Fragments)> _stats = new();
    private int _frames;

    public IProfiler Begin() => new Frame(this);

    public void Print(int minFrames)
    {
        lock (_lock)
        {
            if (_frames < minFrames)
                return;
            double total = _gpu.Values.Sum();
            Console.WriteLine($"--- {_frames} frames, GPU {total / _frames:0.00} ms, CPU record {_cpu.Values.Sum() / _frames:0.00} ms");
            foreach (var pair in _gpu.OrderByDescending(p => p.Value).Take(30))
            {
                (ulong triangles, ulong fragments) = _stats.GetValueOrDefault(pair.Key);
                string counts = triangles == 0 ? "" : $"  {triangles / (double)_frames / 1e6,7:0.00}M triangles {fragments / (double)_frames / 1e6,6:0.00}M fragments";
                Console.WriteLine($"  {pair.Value / _frames,7:0.000} ms  {pair.Key}{counts}");
            }
            _gpu.Clear();
            _stats.Clear();
            _cpu.Clear();
            _frames = 0;
        }
    }

    private sealed class Frame(PassTimer owner) : IGraphProfiler, IGpuStatsProfiler
    {
        public void BeginExecution(ulong executionId, string graphName)
        {
        }

        public void EndExecution()
        {
            lock (owner._lock)
                owner._frames++;
        }

        public void BeginGroup(in GroupInfo group)
        {
        }

        public void EndGroup(in GroupInfo group)
        {
        }

        public void BeginPass(in PassInfo pass)
        {
        }

        public void EndPass(in PassInfo pass, in PassStats stats)
        {
            lock (owner._lock)
                owner._cpu[pass.Name] = owner._cpu.GetValueOrDefault(pass.Name) + stats.CpuMilliseconds;
        }

        public void RecordExecutionTime(in CommandBufferInfo commandBuffer, double milliseconds)
        {
            string name = commandBuffer.Pass?.Name ?? commandBuffer.Name;
            lock (owner._lock)
                owner._gpu[name] = owner._gpu.GetValueOrDefault(name) + milliseconds;
        }

        public void RecordGpuVertexStats(in CommandBufferInfo commandBuffer, in GpuVertexStats stats)
        {
            string name = commandBuffer.Pass?.Name ?? commandBuffer.Name;
            lock (owner._lock)
            {
                (ulong triangles, ulong fragments) = owner._stats.GetValueOrDefault(name);
                owner._stats[name] = (triangles + stats.InputAssemblyPrimitives, fragments + stats.FragmentShaderInvocations);
            }
        }
    }
}

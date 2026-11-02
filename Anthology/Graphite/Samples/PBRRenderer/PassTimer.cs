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
            foreach (var pair in _gpu.OrderByDescending(p => p.Value).Take(14))
                Console.WriteLine($"  {pair.Value / _frames,7:0.000} ms  {pair.Key}");
            _gpu.Clear();
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
        }
    }
}

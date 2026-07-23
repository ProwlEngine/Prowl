using System.Collections.Generic;
using System.Diagnostics;

using Prowl.Graphite;

namespace Prowl.Editor.Profiling;

/// <summary>
/// Writes CPU/GPU timing and profiling samples to the currently building frame.
/// </summary>
public sealed class TimingCollector
{
    // Scratch for one open CPU scope
    private sealed class CpuNode
    {
        public string Name = "";
        public long StartTimestamp;
        public readonly List<TimeSample> Children = new();
    }

    private readonly Stack<CpuNode> _cpuStack = new();
    private readonly Stack<CpuNode> _cpuNodePool = new();

    // Keyed by pass name (a small, stable set), so the outer dictionary and each inner list persist
    // across frames - only their contents are cleared - instead of being discarded and rebuilt.
    private readonly List<string> _gpuGroupOrder = new();
    private readonly Dictionary<string, List<TimeSample>> _gpuGroups = new();
    private bool _hasGpuData;

    private readonly Dictionary<ulong, double> _commandBufferGpuMs = new();

    private ProfiledFrame? _frame;
    private string _currentView = "";

    public void OnFrameBegin(ProfiledFrame frame)
    {
        _frame = frame;

        _cpuStack.Clear();
        PushCpu("Frame");

        _gpuGroupOrder.Clear();
        foreach (List<TimeSample> leaves in _gpuGroups.Values)
            leaves.Clear();
        _hasGpuData = false;

        _commandBufferGpuMs.Clear();

        _currentView = "";
    }

    public void OnViewBegin(string view)
    {
        _currentView = view;
        PushCpu(view);
    }

    public void OnViewEnd()
    {
        TimeSample sample = PopCpuMeasured();
        if (_currentView.Length > 0 && _frame != null)
            _frame.View(_currentView).SetCpuMilliseconds(sample.InclusiveMilliseconds);
        _currentView = "";
    }

    public void OnPassBegin(in PassInfo p) => PushCpu(p.Name);

    public void OnPassEnd(in PassInfo p)
    {
        TimeSample sample = PopCpuMeasured();
        if (_frame != null)
            _frame.View(_currentView).Pass(p.Index, p.Name).SetCpuTiming(sample.InclusiveMilliseconds, sample.Children);
    }

    public void OnSampleBegin(string name) => PushCpu(name);
    public void OnSampleEnd() => PopCpu();

    public void OnExecutionTime(PassInfo? p, ulong commandBufferId, string bufferName, bool isTransfer, double ms)
    {
        string key = p.HasValue ? p.Value.Name : "Transfer";
        if (!_gpuGroups.TryGetValue(key, out List<TimeSample>? leaves))
        {
            leaves = new List<TimeSample>();
            _gpuGroups[key] = leaves;
        }

        // leaves persists across frames and is only Clear()'d at OnFrameBegin, so an empty list here
        // means this key hasn't been touched yet this frame - not necessarily that it's brand new.
        if (leaves.Count == 0)
            _gpuGroupOrder.Add(key);
        leaves.Add(new TimeSample(bufferName, ms, isTransfer, []));
        _hasGpuData = true;

        if (commandBufferId != 0)
        {
            _commandBufferGpuMs.TryGetValue(commandBufferId, out double existing);
            _commandBufferGpuMs[commandBufferId] = existing + ms;
        }
    }

    public double GetCommandBufferGpuMs(ulong commandBufferId) => _commandBufferGpuMs.TryGetValue(commandBufferId, out double ms) ? ms : 0.0;

    public void FinalizeFrame(ProfiledFrame frame)
    {
        while (_cpuStack.Count > 1)
            PopCpu();

        if (_cpuStack.Count == 1)
        {
            CpuNode root = _cpuStack.Pop();
            frame.SetCpuRoot(FinalizeCpu(root));
        }

        if (!_hasGpuData)
            return;

        var groups = new List<TimeSample>(_gpuGroupOrder.Count);
        double total = 0.0;
        foreach (string key in _gpuGroupOrder)
        {
            List<TimeSample> leaves = _gpuGroups[key];
            double sum = 0.0;
            foreach (TimeSample leaf in leaves)
                sum += leaf.InclusiveMilliseconds;
            groups.Add(new TimeSample(key, sum, key == "Transfer", leaves.ToArray()));
            total += sum;
        }

        frame.SetGpuRoot(new TimeSample("GPU", total, false, groups.ToArray()));
    }

    private void PushCpu(string name)
    {
        CpuNode node = _cpuNodePool.Count > 0 ? _cpuNodePool.Pop() : new CpuNode();
        node.Name = name;
        node.StartTimestamp = Stopwatch.GetTimestamp();
        node.Children.Clear();
        _cpuStack.Push(node);
    }

    private void PopCpu()
    {
        if (_cpuStack.Count <= 1)
            return;

        CpuNode node = _cpuStack.Pop();
        _cpuStack.Peek().Children.Add(FinalizeCpu(node));
    }

    private static readonly TimeSample s_emptySample = new("", 0.0, false, System.Array.Empty<TimeSample>());

    private TimeSample PopCpuMeasured()
    {
        if (_cpuStack.Count <= 1)
            return s_emptySample;

        CpuNode node = _cpuStack.Pop();
        TimeSample sample = FinalizeCpu(node);
        _cpuStack.Peek().Children.Add(sample);
        return sample;
    }

    private TimeSample FinalizeCpu(CpuNode node)
    {
        double ms = ElapsedMs(node.StartTimestamp);
        var sample = new TimeSample(node.Name, ms, false, node.Children.ToArray());
        _cpuNodePool.Push(node);
        return sample;
    }

    private static double ElapsedMs(long startTimestamp)
    {
        long elapsed = Stopwatch.GetTimestamp() - startTimestamp;
        return elapsed * 1000.0 / Stopwatch.Frequency;
    }
}

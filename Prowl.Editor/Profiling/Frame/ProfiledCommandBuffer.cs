using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

using Prowl.Graphite;

namespace Prowl.Editor.Profiling;


public sealed class ProfiledCommandBuffer
{
    public ulong Id { get; private set; }
    public string Name { get; private set; }
    public double GpuMilliseconds { get; private set; }

    public ulong InputAssemblyVertices { get; private set; }
    public ulong InputAssemblyPrimitives { get; private set; }
    public ulong ClippingInvocations { get; private set; }
    public ulong ClippingPrimitives { get; private set; }

    public int PipelineSwitchCount { get; private set; }

    public IReadOnlyList<ProfiledPipeline> Switches => _switches;

    private readonly List<ProfiledPipeline> _switches = new();

    internal ProfiledCommandBuffer(ulong id, string name)
    {
        Id = id;
        Name = name;
    }

    internal void ResetForReuse(ulong id, string name)
    {
        Id = id;
        Name = name;
        _switches.Clear();
        GpuMilliseconds = 0;
        InputAssemblyVertices = 0;
        InputAssemblyPrimitives = 0;
        ClippingInvocations = 0;
        ClippingPrimitives = 0;
        PipelineSwitchCount = 0;
    }

    internal void SetName(string name) => Name = name;
    public void SetGpuMs(double ms) => GpuMilliseconds = ms;
    public void SetPipelineSwitchCount(int count) => PipelineSwitchCount = count;
    internal void IncrementPipelineSwitchCount() => PipelineSwitchCount++;

    public void SetGpuVertexStats(in GpuVertexStats stats)
    {
        InputAssemblyVertices = stats.InputAssemblyVertices;
        InputAssemblyPrimitives = stats.InputAssemblyPrimitives;
        ClippingInvocations = stats.ClippingInvocations;
        ClippingPrimitives = stats.ClippingPrimitives;
    }

    /// <summary>Appends an already-built switch - used by SnapshotSerializer.</summary>
    internal void AddSwitchInstance(ProfiledPipeline sw) => _switches.Add(sw);

    public ProfiledPipeline AddSwitch(
        string shaderName, bool isCompute, ShaderStages stages,
        string passName, string variant, IReadOnlyDictionary<string, string>? tags, string materialName,
        ProfiledPipelineState? state)
    {
        var sw = new ProfiledPipeline(shaderName, isCompute, stages, passName, variant, tags, materialName, state);
        _switches.Add(sw);
        return sw;
    }

    internal ProfiledCommandBuffer Clone()
    {
        var clone = new ProfiledCommandBuffer(Id, Name);
        clone.SetGpuMs(GpuMilliseconds);
        clone.SetGpuVertexStats(new GpuVertexStats(InputAssemblyVertices, InputAssemblyPrimitives, ClippingInvocations, ClippingPrimitives));
        clone.SetPipelineSwitchCount(PipelineSwitchCount);
        foreach (ProfiledPipeline sw in _switches)
            clone._switches.Add(sw.Clone());
        return clone;
    }
}
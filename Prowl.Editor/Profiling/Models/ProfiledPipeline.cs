using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

using Prowl.Graphite;

namespace Prowl.Editor.Profiling;


public sealed class ProfiledPipeline
{
    public string ShaderName { get; }
    public string ShaderPassName { get; }
    public string Variant { get; }
    public ProfiledPipelineState? State { get; }


    public string MaterialName { get; }
    public IReadOnlyDictionary<string, string>? Tags { get; }


    public bool IsCompute { get; }
    public ShaderStages Stages { get; }

    public IReadOnlyList<ProfiledCallingObject> Objects => _objects;
    public IReadOnlyList<ProfiledDrawCall> Draws => _draws;

    private readonly List<ProfiledCallingObject> _objects = new();
    private readonly List<ProfiledDrawCall> _draws = new();


    internal ProfiledPipeline(
        string shaderName, bool isCompute, ShaderStages stages,
        string passName, string variant, IReadOnlyDictionary<string, string>? tags, string materialName,
        ProfiledPipelineState? state)
    {
        ShaderName = shaderName;
        IsCompute = isCompute;
        Stages = stages;
        ShaderPassName = passName;
        Variant = variant;
        Tags = tags;
        MaterialName = materialName;
        State = state;
    }

    public ProfiledCallingObject AddObject(
        string label, string materialName, string meshName, int layer, Vector.Float3 position,
        bool culled, int drawStart, int drawEnd)
    {
        var obj = new ProfiledCallingObject(label, materialName, meshName, layer, position, culled, drawStart, drawEnd);
        _objects.Add(obj);
        return obj;
    }


    internal void AddObjectInstance(ProfiledCallingObject obj) => _objects.Add(obj);
    public void AddDraw(ProfiledDrawCall draw) => _draws.Add(draw);

    internal ProfiledPipeline Clone()
    {
        var clone = new ProfiledPipeline(ShaderName, IsCompute, Stages, ShaderPassName, Variant, Tags, MaterialName, State);
        clone._objects.AddRange(_objects);
        clone._draws.AddRange(_draws);
        return clone;
    }
}
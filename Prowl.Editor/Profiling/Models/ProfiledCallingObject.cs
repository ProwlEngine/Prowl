using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

using Prowl.Graphite;

namespace Prowl.Editor.Profiling;


public sealed class ProfiledCallingObject
{
    public string Label { get; }
    public string MaterialName { get; }
    public string MeshName { get; }
    public int Layer { get; }
    public Vector.Float3 Position { get; }
    public bool Culled { get; }

    /// <summary>Range in the owning ProfiledPipelineSwitch.Draws this object claims - see
    /// <see cref="ProfiledPipeline.GetDraws"/>.</summary>
    public int DrawStart { get; }
    public int DrawEnd { get; }

    internal ProfiledCallingObject(
        string label, string materialName, string meshName, int layer, Vector.Float3 position,
        bool culled, int drawStart, int drawEnd)
    {
        Label = label;
        MaterialName = materialName;
        MeshName = meshName;
        Layer = layer;
        Position = position;
        Culled = culled;
        DrawStart = drawStart;
        DrawEnd = drawEnd;
    }
}
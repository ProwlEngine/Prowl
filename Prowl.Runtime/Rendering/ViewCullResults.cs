// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Vector;

namespace Prowl.Runtime.Rendering;

/// <summary>
/// One camera's visible set for the frame: indices into <see cref="SceneCuller.Renderables"/> split by render order, plus the lights it sees.
/// </summary>
public sealed class ViewCullResults
{
    public List<int> Opaque = new();
    public List<int> Transparent = new();
    public List<int> UI = new();
    public List<int> Culled = new();
    public AABB[] WorldBounds = Array.Empty<AABB>();
    public bool[] Renderable = Array.Empty<bool>();
    public List<IRenderableLight> Lights = new();
    public IRenderableLight? Directional;
    public Frustum Frustum;
    public Float3 ShadowFocus;

    internal float[] SortKeys = Array.Empty<float>();
    internal readonly DistanceComparer FrontToBack = new(descending: false);
    internal readonly DistanceComparer BackToFront = new(descending: true);

    internal void Reset(int renderableCount)
    {
        Opaque.Clear();
        Transparent.Clear();
        UI.Clear();
        Culled.Clear();
        Lights.Clear();
        Directional = null;

        if (WorldBounds.Length < renderableCount)
        {
            int size = Math.Max(renderableCount, WorldBounds.Length * 2);
            WorldBounds = new AABB[size];
            Renderable = new bool[size];
            SortKeys = new float[size];
        }

        FrontToBack.Keys = SortKeys;
        BackToFront.Keys = SortKeys;
    }

    internal sealed class DistanceComparer(bool descending) : IComparer<int>
    {
        public float[] Keys = Array.Empty<float>();

        public int Compare(int a, int b)
            => descending ? Keys[b].CompareTo(Keys[a]) : Keys[a].CompareTo(Keys[b]);
    }
}

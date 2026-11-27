using System;

using Prowl.Vector;

namespace Prowl.Graphite.Rendering;

public sealed partial class RenderWorld
{
    private volatile (FeatureGroup Group, Func<Double3, bool> Where)[] _suppressed = [];

    /// <summary>
    /// Leaves the features of a group out of every view of the world whose camera is where the test says, for a
    /// feature that takes their place there, as water does the fog's under its surface. The test runs on the render
    /// thread each frame. Host thread.
    /// </summary>
    public void Suppress(FeatureGroup group, Func<Double3, bool> where)
    {
        lock (this)
            _suppressed = [.. _suppressed, (group, where)];
    }

    /// <summary>Ends a <see cref="Suppress"/> of the same group and test.</summary>
    public void Unsuppress(FeatureGroup group, Func<Double3, bool> where)
    {
        lock (this)
            _suppressed = Array.FindAll(_suppressed, entry => entry.Group != group || entry.Where != where);
    }

    internal bool Suppresses(FeatureGroup group, Double3 camera)
    {
        foreach ((FeatureGroup suppressed, Func<Double3, bool> where) in _suppressed)
        {
            if (suppressed == group && where(camera))
                return true;
        }
        return false;
    }
}

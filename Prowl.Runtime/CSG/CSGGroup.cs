// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Echo;
using Prowl.Vector.Geometry;

namespace Prowl.Runtime.CSG;

/// <summary>A CSG group node. Its descendants combine into one solid, which folds into the enclosing group by <see cref="Operation"/>.</summary>
[AddComponentMenu("CSG/CSG Group")]
public sealed class CSGGroup : Component
{
    [SerializeField] private CSGOperation operation = CSGOperation.Additive;
    [SerializeField] private bool passThrough;

    public CSGOperation Operation
    {
        get => operation;
        set
        {
            if (operation == value) return;
            operation = value;
            Version++;
        }
    }

    /// <summary>Treat this as a plain transform rather than a CSG boundary, so its children fold into the enclosing group directly.</summary>
    public bool PassThrough
    {
        get => passThrough;
        set
        {
            if (passThrough == value) return;
            passThrough = value;
            Version++;
        }
    }

    /// <summary>Clicking any brush inside this group selects the group instead.</summary>
    public bool HandleAsOne;

    /// <summary>Made by the editor to hold the brushes of one generated shape, such as a staircase.</summary>
    public bool HoldsShape;

    /// <summary>Bumped by any edit that changes the CSG result.</summary>
    public int Version { get; private set; } = 1;

    public override void OnValidate() => Version++;
}

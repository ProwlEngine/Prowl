// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.Vector;

namespace Prowl.Runtime;

/// <summary>Status of a calculated path (matches Unity's NavMeshPathStatus).</summary>
public enum NavMeshPathStatus
{
    /// <summary>The path reaches the destination.</summary>
    PathComplete,
    /// <summary>The path is valid but cannot reach the destination; it leads to the closest reachable point.</summary>
    PathPartial,
    /// <summary>No path exists (or the endpoints are off the navmesh).</summary>
    PathInvalid,
}

/// <summary>
/// A calculated navigation path: world-space corner points plus a status. Reusable — pass the
/// same instance to repeated <see cref="NavMesh.CalculatePath(Float3, Float3, int, NavMeshPath)"/>
/// calls to avoid reallocating.
/// </summary>
public sealed class NavMeshPath
{
    private Float3[] _corners = [];
    private int _cornerCount;

    // The polygons the corners were derived from, so NavMeshAgent.SetPath can hand the crowd the
    // route itself. Corners cannot express one: two different polygon paths can share them.
    private long[] _polys = [];
    private int _polyCount;

    internal Span<long> Polys => _polys.AsSpan(0, _polyCount);

    /// <summary>The state of the path.</summary>
    public NavMeshPathStatus Status { get; internal set; } = NavMeshPathStatus.PathInvalid;

    /// <summary>The corner points of the path. Allocates a fresh array; use
    /// <see cref="GetCornersNonAlloc"/> on hot paths.</summary>
    public Float3[] Corners
    {
        get
        {
            Float3[] result = new Float3[_cornerCount];
            Array.Copy(_corners, result, _cornerCount);
            return result;
        }
    }

    /// <summary>Number of valid corners.</summary>
    public int CornerCount => _cornerCount;

    /// <summary>The point the path actually reaches, which for a partial path is not the requested
    /// destination. Callers must check <see cref="CornerCount"/> first.</summary>
    internal Float3 LastCorner => _corners[_cornerCount - 1];

    /// <summary>Copy up to <paramref name="results"/>.Length corners into the given array,
    /// returning the number written.</summary>
    public int GetCornersNonAlloc(Float3[] results)
    {
        ArgumentNullException.ThrowIfNull(results);
        int n = Math.Min(results.Length, _cornerCount);
        Array.Copy(_corners, results, n);
        return n;
    }

    /// <summary>Erase all corner points and reset the status to invalid.</summary>
    public void ClearCorners()
    {
        _cornerCount = 0;
        _polyCount = 0;
        Status = NavMeshPathStatus.PathInvalid;
    }

    internal void SetPolys(ReadOnlySpan<long> polys)
    {
        if (_polys.Length < polys.Length)
            _polys = new long[polys.Length];
        polys.CopyTo(_polys);
        _polyCount = polys.Length;
    }

    internal void SetCorners(ReadOnlySpan<Float3> corners, NavMeshPathStatus status)
    {
        if (_corners.Length < corners.Length)
            _corners = new Float3[Math.Max(corners.Length, 16)];
        corners.CopyTo(_corners);
        _cornerCount = corners.Length;
        Status = status;
    }
}

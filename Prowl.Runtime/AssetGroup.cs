// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace Prowl.Runtime;

/// <summary>A set of assets loading together, held loaded until the group is disposed.</summary>
public sealed class AssetGroup : IDisposable
{
    private readonly long[] _weights;
    private readonly long _totalWeight;
    private bool _disposed;

    public IReadOnlyList<Asset> Assets { get; }
    public Task Completion { get; }

    internal AssetGroup(List<Asset> assets)
    {
        Assets = assets;
        _weights = assets.Select(a => Math.Max(1, AssetDatabase.Backend?.GetEstimatedSize(a.AssetID) ?? 1)).ToArray();
        _totalWeight = Math.Max(1, _weights.Sum());

        var loads = new List<Task>();
        foreach (Asset asset in assets)
        {
            AssetDatabase.Hold(asset, this);
            loads.Add(asset.LoadAsync());
        }
        Completion = Task.WhenAll(loads);
    }

    /// <summary>How much of the group is loaded, from 0 to 1, weighted by each asset's size.</summary>
    public float Progress
    {
        get
        {
            long done = 0;
            for (int i = 0; i < Assets.Count; i++)
                if (Assets[i].State is not (AssetState.Unloaded or AssetState.Loading)) done += _weights[i];
            return (float)((double)done / _totalWeight);
        }
    }

    public bool IsDone => Assets.All(a => a.State is not (AssetState.Unloaded or AssetState.Loading));

    public int FailedCount => Assets.Count(a => a.State is AssetState.Failed or AssetState.Missing);

    public TaskAwaiter GetAwaiter() => Completion.GetAwaiter();

    /// <summary>Blocks until every asset in the group is loaded.</summary>
    public void Wait()
    {
        foreach (Asset asset in Assets)
            asset.Load();
    }

    /// <summary>
    /// Ends the group's hold. Loads still running finish, since someone else may be waiting on them, and whatever
    /// nothing uses is unloaded by the walk.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        AssetDatabase.ReleaseAll(this);
    }
}

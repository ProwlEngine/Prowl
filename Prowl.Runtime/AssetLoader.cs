// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Prowl.Runtime;

/// <summary>
/// Reads assets on one background thread into staging copies, and moves each copy into its stable object on the
/// main thread at the start of a frame, so nothing ever sees an asset half filled. A blocking load reads on the
/// calling thread instead and publishes at once.
/// </summary>
internal static class AssetLoader
{
    internal sealed class Job
    {
        public required Asset Asset;
        public required int Generation;
        public bool High;
        public bool Started;
        public bool Read;
        public bool NeedsMainThread;
        public bool Published;
        public Asset? Staging;
        public readonly ManualResetEventSlim ReadDone = new(false);
        public readonly TaskCompletionSource Completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private static readonly object s_lock = new();
    private static readonly Dictionary<Asset, Job> s_jobs = new(ReferenceEqualityComparer.Instance);
    private static readonly Queue<Job> s_high = new();
    private static readonly Queue<Job> s_normal = new();
    private static readonly ConcurrentQueue<Job> s_completed = new();
    private static readonly ConcurrentQueue<Job> s_mainThread = new();
    private static readonly SemaphoreSlim s_signal = new(0);

    private static readonly object s_startLock = new();
    private static Thread? s_thread;
    private static volatile bool s_running;

    [ThreadStatic] private static bool t_isLoaderThread;
    private static int s_mainThreadId;

    public static bool IsLoaderThread => t_isLoaderThread;

    /// <summary>The main thread publishes loads. Until one is set every thread publishes its own.</summary>
    public static void SetMainThread() => s_mainThreadId = Environment.CurrentManagedThreadId;

    public static bool IsMainThread => s_mainThreadId == 0 || Environment.CurrentManagedThreadId == s_mainThreadId;

    /// <summary>True while any load is queued, reading or waiting to publish.</summary>
    public static bool IsBusy
    {
        get { lock (s_lock) return s_jobs.Count > 0; }
    }

    public static bool IsInFlight(Asset asset)
    {
        lock (s_lock) return s_jobs.ContainsKey(asset);
    }

    /// <summary>Queues a background load. Returns the job, or null when there is nothing to load.</summary>
    public static Job? Request(Asset asset, bool high = false)
    {
        if (!NeedsLoading(asset, retryFailed: false)) return null;

        Job job;
        lock (s_lock)
        {
            if (!s_jobs.TryGetValue(asset, out job!))
            {
                if (!NeedsLoading(asset, retryFailed: false)) return null;
                job = new Job { Asset = asset, Generation = asset.ReadGeneration, High = high };
                s_jobs[asset] = job;
                asset.SetState(AssetState.Loading);
                (high ? s_high : s_normal).Enqueue(job);
            }
            else if (high && !job.High && !job.Started)
            {
                job.High = true;
                s_high.Enqueue(job);
            }
            else return job;
        }

        EnsureStarted();
        s_signal.Release();
        return job;
    }

    /// <summary>Loads now: reads on this thread unless the loader already is, then publishes.</summary>
    public static void LoadBlocking(Asset asset)
    {
        if (!NeedsLoading(asset, retryFailed: true)) return;
        if (t_isLoaderThread)
        {
            Debug.LogError($"{asset.GetType().Name} '{asset.Name}' was loaded from inside another load. Loading never waits on other assets.");
            return;
        }

        Job job;
        bool readHere = false;
        bool publishing = false;
        lock (s_lock)
        {
            if (!s_jobs.TryGetValue(asset, out job!))
            {
                if (!NeedsLoading(asset, retryFailed: true)) return;
                job = new Job { Asset = asset, Generation = asset.ReadGeneration, High = true };
                s_jobs[asset] = job;
            }

            if (job.Published) publishing = true;
            else
            {
                asset.SetState(AssetState.Loading);
                readHere = !job.Started;
                job.Started = true;
            }
        }

        if (publishing)
        {
            if (!IsMainThread) job.Completion.Task.Wait();
            return;
        }

        if (readHere) Read(job);
        else
        {
            job.ReadDone.Wait();
            if (job.NeedsMainThread && IsMainThread)
            {
                lock (job)
                {
                    if (!job.Read)
                    {
                        job.NeedsMainThread = false;
                        Read(job);
                    }
                }
            }
        }

        if (IsMainThread) Publish(job);
        else
        {
            // Only the main thread publishes, so a read done here waits for the next Pump like the loader's do.
            if (readHere && !job.NeedsMainThread) s_completed.Enqueue(job);
            job.Completion.Task.Wait();
        }
    }

    public static Task LoadAsync(Asset asset, CancellationToken cancel)
    {
        Job? job = Request(asset);
        if (job == null) return Task.CompletedTask;
        return cancel.CanBeCanceled ? job.Completion.Task.WaitAsync(cancel) : job.Completion.Task;
    }

    /// <summary>Drops a queued load nothing has started. A started load finishes and publishes as usual.</summary>
    public static void Cancel(Asset asset)
    {
        lock (s_lock)
        {
            if (!s_jobs.TryGetValue(asset, out Job? job) || job.Started) return;
            job.Started = true;
            job.Published = true;
            s_jobs.Remove(asset);
            asset.SetState(AssetState.Unloaded);
            job.Completion.TrySetCanceled();
        }
    }

    /// <summary>Publishes every finished background load. Main thread, once at the start of each frame.</summary>
    public static void Pump()
    {
        while (s_mainThread.TryDequeue(out Job? job))
        {
            lock (job)
            {
                if (!job.Read)
                {
                    job.NeedsMainThread = false;
                    Read(job);
                }
            }
            Publish(job);
        }

        while (s_completed.TryDequeue(out Job? job))
            Publish(job);
    }

    private static bool NeedsLoading(Asset asset, bool retryFailed)
        => asset.IsFromDatabase && !asset.IsDisposed
           && (asset.State is AssetState.Unloaded or AssetState.Loading || (retryFailed && asset.State == AssetState.Failed));

    private static void Read(Job job)
    {
        Asset asset = job.Asset;
        try
        {
            bool onMain = !t_isLoaderThread && IsMainThread;
            if (!onMain && AssetDatabase.NeedsMainThread(asset.AssetID))
            {
                job.NeedsMainThread = true;
                s_mainThread.Enqueue(job);
                return;
            }

            // Only the main thread imports. A cache that went stale since the check above is read as it is, and the
            // reimport that follows reads it again.
            Asset staging = AssetDatabase.CreateShell(asset.GetType());
            if (AssetDatabase.ReadContent(asset.AssetID, staging, mayImport: onMain))
                job.Staging = staging;

            // An import on this thread happens before the read, so the read is current whatever the import refilled.
            if (onMain) job.Generation = asset.ReadGeneration;
        }
        catch (Exception ex)
        {
            Debug.LogError($"Failed to load {asset.GetType().Name} '{asset.Name}' ({asset.AssetID}): {ex}");
        }
        finally
        {
            if (!job.NeedsMainThread) job.Read = true;
            job.ReadDone.Set();
        }
    }

    private static void Publish(Job job)
    {
        lock (job)
        {
            if (job.Published) return;
            job.Published = true;
        }

        try { PublishRead(job); }
        finally
        {
            lock (s_lock)
            {
                if (s_jobs.TryGetValue(job.Asset, out Job? current) && current == job)
                    s_jobs.Remove(job.Asset);
            }
            job.Completion.TrySetResult();
        }
    }

    private static void PublishRead(Job job)
    {
        // Filled another way, marked missing or retired while this read was in flight, so what it read is out of date.
        if (job.Asset.State is AssetState.Loaded or AssetState.Missing) return;

        // Its source changed after the read, so read it again.
        if (job.Generation != job.Asset.ReadGeneration)
        {
            job.Staging = null;
            Asset staging = AssetDatabase.CreateShell(job.Asset.GetType());
            if (AssetDatabase.ReadContent(job.Asset.AssetID, staging)) job.Staging = staging;
        }

        if (job.Staging != null) AssetDatabase.Publish(job.Asset, job.Staging, ReloadReason.Load);
        else AssetDatabase.PublishFailed(job.Asset);
    }

    private static void EnsureStarted()
    {
        if (s_running) return;
        lock (s_startLock)
        {
            if (s_running) return;
            s_running = true;
            int generation = s_generation;
            s_thread = new Thread(() => Loop(generation)) { IsBackground = true, Name = "Prowl Asset Loader" };
            s_thread.Start();
        }
    }

    // A loop from before a Stop that outlived its join exits on its own rather than running beside the new one.
    private static volatile int s_generation;

    private static void Loop(int generation)
    {
        t_isLoaderThread = true;
        while (s_running && generation == s_generation)
        {
            s_signal.Wait();
            if (generation != s_generation)
            {
                if (s_running) s_signal.Release(); // the wake was meant for the loop that replaced this one
                break;
            }
            if (!s_running) break;

            Job? job = null;
            lock (s_lock)
            {
                while (job == null && (s_high.Count > 0 || s_normal.Count > 0))
                {
                    Job next = s_high.Count > 0 ? s_high.Dequeue() : s_normal.Dequeue();
                    if (next.Started) continue;
                    next.Started = true;
                    job = next;
                }
            }
            if (job == null) continue;

            Read(job);
            if (generation != s_generation) break;
            if (!job.NeedsMainThread) s_completed.Enqueue(job);
        }
    }

    /// <summary>Stops the loader thread, dropping queued loads and releasing anyone waiting on one.</summary>
    public static void Stop()
    {
        if (s_running)
        {
            s_running = false;
            s_generation++;
            s_signal.Release();
            Thread? thread = s_thread;
            s_thread = null;
            try { thread?.Join(2000); } catch { }
        }

        lock (s_lock)
        {
            foreach (Job job in s_jobs.Values)
            {
                if (job.Asset.State == AssetState.Loading) job.Asset.SetState(AssetState.Unloaded);
                job.ReadDone.Set();
                job.Completion.TrySetCanceled();
            }
            s_jobs.Clear();
            s_high.Clear();
            s_normal.Clear();
        }
        s_completed.Clear();
        s_mainThread.Clear();
        s_mainThreadId = 0;
    }
}

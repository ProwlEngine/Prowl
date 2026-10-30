using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;


namespace Prowl.Graphite.ShaderDef;


/// <summary>How soon a queued compile runs, most urgent first.</summary>
public enum CompilePriority
{
    /// <summary>The stand in every shader draws with while its own variants compile.</summary>
    Fallback,

    /// <summary>A variant a visible view draws with.</summary>
    Visible,

    /// <summary>A variant a shadow view draws with.</summary>
    Shadow,

    /// <summary>A variant a reflection or sky capture draws with.</summary>
    Capture,

    /// <summary>A variant asked for ahead of use, like a prewarm or a debug view.</summary>
    Background,
}


/// <summary>One queued compile. Its priority can be raised while it waits.</summary>
public sealed class CompileJob
{
    private readonly Action _work;
    private readonly TaskCompletionSource _done = new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal CompileJob(Action work, CompilePriority priority)
    {
        _work = work;
        Priority = priority;
    }

    public CompilePriority Priority { get; internal set; }

    internal LinkedListNode<CompileJob>? Node;

    /// <summary>Completes when the work ran, faulted when it threw, cancelled when it was dropped before it ran.</summary>
    public Task Done => _done.Task;

    public bool IsCompleted => _done.Task.IsCompleted;

    /// <summary>Moves the job ahead when it waits at a lower priority.</summary>
    public void Raise(CompilePriority priority) => CompileQueue.Raise(this, priority);

    /// <summary>Drops the job when it has not started.</summary>
    public void Cancel() => CompileQueue.Cancel(this);

    internal void Run()
    {
        try
        {
            _work();
            _done.SetResult();
        }
        catch (Exception e)
        {
            _done.SetException(e);
        }
    }

    internal void Drop() => _done.TrySetCanceled();
}


/// <summary>
/// The shader compile threads, one fewer than the cores, each keeping its own compiler state warm. Jobs run most urgent
/// first.
/// </summary>
public static class CompileQueue
{
    private static readonly LinkedList<CompileJob>[] s_queues = CreateQueues();
    private static readonly object s_lock = new();
    private static int s_threads;
    private static int s_idle;

    /// <summary>Compile threads, started as work arrives.</summary>
    public static int ThreadCount { get; } = Math.Max(1, Environment.ProcessorCount - 1);

    /// <summary>Jobs waiting to start. Any thread.</summary>
    public static int Queued
    {
        get
        {
            lock (s_lock)
            {
                int count = 0;
                foreach (LinkedList<CompileJob> queue in s_queues)
                    count += queue.Count;
                return count;
            }
        }
    }

    public static CompileJob Enqueue(Action work, CompilePriority priority)
    {
        CompileJob job = new(work, priority);
        lock (s_lock)
        {
            job.Node = s_queues[(int)priority].AddLast(job);
            if (s_idle > 0)
                Monitor.Pulse(s_lock);
            else if (s_threads < ThreadCount)
                Start();
        }
        return job;
    }

    internal static void Raise(CompileJob job, CompilePriority priority)
    {
        lock (s_lock)
        {
            if (job.Node is not { } node || priority >= job.Priority)
                return;
            s_queues[(int)job.Priority].Remove(node);
            job.Priority = priority;
            job.Node = s_queues[(int)priority].AddLast(job);
        }
    }

    internal static void Cancel(CompileJob job)
    {
        lock (s_lock)
        {
            if (job.Node is not { } node)
                return;
            s_queues[(int)job.Priority].Remove(node);
            job.Node = null;
        }
        job.Drop();
    }

    private static LinkedList<CompileJob>[] CreateQueues()
    {
        LinkedList<CompileJob>[] queues = new LinkedList<CompileJob>[Enum.GetValues<CompilePriority>().Length];
        for (int i = 0; i < queues.Length; i++)
            queues[i] = new LinkedList<CompileJob>();
        return queues;
    }

    private static void Start()
    {
        s_threads++;
        new Thread(Work) { IsBackground = true, Name = $"Shader compile {s_threads}" }.Start();
    }

    private static void Work()
    {
        while (true)
        {
            CompileJob job;
            lock (s_lock)
            {
                CompileJob? next;
                while ((next = Next()) == null)
                {
                    s_idle++;
                    Monitor.Wait(s_lock);
                    s_idle--;
                }
                job = next;
            }
            job.Run();
        }
    }

    private static CompileJob? Next()
    {
        foreach (LinkedList<CompileJob> queue in s_queues)
        {
            if (queue.First is not { } node)
                continue;
            queue.RemoveFirst();
            node.Value.Node = null;
            return node.Value;
        }
        return null;
    }
}

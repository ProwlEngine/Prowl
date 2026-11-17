using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

using Xunit;

namespace Prowl.Graphite.ShaderDef.Tests;


public class CompileQueueTests
{
    // The compile threads never exit, so whatever one copied from the thread that started it would never be freed.
    [Fact]
    public void CompileThreadsRunWithoutTheExecutionContextOfTheThreadThatStartedThem()
    {
        AsyncLocal<object?> local = new() { Value = new object() };

        ExecutionContext? none = null;
        Thread bare = new(() => none = ExecutionContext.Capture());
        bare.UnsafeStart();
        bare.Join();

        // Each job is running before the next is queued, so no thread is idle: every compile thread ends up
        // holding one job and reports its context, whether it started here or in an earlier test.
        using ManualResetEventSlim release = new();
        List<Task> jobs = [];
        int inherited = 0;
        for (int i = 0; i < CompileQueue.ThreadCount; i++)
        {
            ManualResetEventSlim started = new();
            jobs.Add(CompileQueue.Enqueue(() =>
            {
                if (!ReferenceEquals(ExecutionContext.Capture(), none))
                    Interlocked.Increment(ref inherited);
                started.Set();
                release.Wait();
            }, CompilePriority.Fallback).Done);
            Assert.True(started.Wait(TimeSpan.FromSeconds(30)), "a queued job never started");
        }
        release.Set();
        Task.WaitAll(jobs.ToArray());

        local.Value = null;
        Assert.Equal(0, inherited);
    }

    private sealed class GatedCompiler : IShaderCompiler
    {
        public readonly ManualResetEventSlim Gate = new();
        public int SessionVersion => 0;

        public IReadOnlyList<VariantSpace> GetAxes(ShaderPass pass) => [];

        public ShaderDescription Compile(ShaderPass pass, Keyword[] combo, GraphicsBackend backend)
        {
            Gate.Wait();
            return new ShaderDescription();
        }

        public ComputeDescription CompileCompute(ShaderPass pass, Keyword[] combo, GraphicsBackend backend) => throw new NotSupportedException();
    }

    // A queued variant waits behind every compile ahead of it, so whatever it copied from the thread that asked
    // for it would stay alive until the queue got to it.
    [Fact]
    public void AQueuedVariantDoesNotKeepTheAsyncLocalsOfTheThreadThatAskedForIt()
    {
        using GraphicsDevice device = GraphicsDevice.CreateVulkan(new GraphicsDeviceOptions(false));
        GatedCompiler compiler = new();
        ShaderPass pass = new() { State = new PassState(), Source = "a" };
        pass.Bind(device, "Test", 0, [], [], compiler);
        PassVariants variants = pass.Variants(compiler, GraphicsBackend.Vulkan);

        AsyncLocal<object?> local = new();
        WeakReference held = HoldInAsyncLocal(local);
        Assert.Equal(VariantState.Pending, variants.Request(0, CompilePolicy.Async, out _, out _));
        local.Value = null;
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        bool alive = held.IsAlive;
        compiler.Gate.Set();
        variants.WaitPending();
        Assert.False(alive, "a variant still compiling kept an object its requester held in an AsyncLocal");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference HoldInAsyncLocal(AsyncLocal<object?> local)
    {
        object value = new byte[1 << 20];
        local.Value = value;
        return new WeakReference(value);
    }
}

// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

using Prowl.Runtime.Resources;
using Prowl.Runtime.Tasks;
using Prowl.Vector;

using Xunit;

// These tests treat the test thread as the engine's main thread, so they block on workers rather than await,
// which could resume on another thread.
#pragma warning disable xUnit1031

namespace Prowl.Runtime.Test;

/// <summary>Stands in for a user script that overrides OnDispose without calling base.</summary>
public sealed class ForgetsBaseDispose : Component
{
    protected override void OnDispose() { }
}

/// <summary>Runs whatever a test hands it from inside Update, the way gameplay code starts work.</summary>
public sealed class GameplayHook : Component
{
    public static Action? OnUpdate;

    public override void Update() => OnUpdate?.Invoke();
}

/// <summary>Records which async session its enable and disable callbacks ran in.</summary>
public sealed class SessionProbe : Component
{
    public CancellationToken EnabledIn, DisabledIn;
    public bool DisabledInEndedSession;

    public override void OnEnable() => EnabledIn = GameTask.SessionToken;

    public override void OnDisable()
    {
        DisabledIn = GameTask.SessionToken;
        DisabledInEndedSession = DisabledIn.IsCancellationRequested;
    }
}

/// <summary>
/// The main thread model: async sessions on the main thread, moving between threads with GameTask, and the
/// ownership checks that stop another thread from touching a running scene.
/// </summary>
public class ThreadingTests : RuntimeTestBase
{
    /// <summary>
    /// Installs a loop bound to the test's own thread for the length of one test. Installed inside each test
    /// rather than the constructor, since nothing promises the constructor ran on the thread the test runs on.
    /// </summary>
    private sealed class LoopScope : IDisposable
    {
        private readonly SynchronizationContext? _previous = SynchronizationContext.Current;

        public LoopScope() => MainThreadContext.Install();

        public MainThreadContext Context => MainThreadContext.Current!;

        public void Pump() => Context.Pump();

        public void PumpUntil(Func<bool> done, int timeoutMs = 5000)
        {
            var clock = Stopwatch.StartNew();
            while (!done())
            {
                if (clock.ElapsedMilliseconds > timeoutMs) throw new TimeoutException("The loop never got there.");
                Pump();
                Thread.Sleep(1);
            }
        }

        public void Dispose()
        {
            MainThreadContext.Uninstall();
            SynchronizationContext.SetSynchronizationContext(_previous);
        }
    }

    private static T OffThread<T>(Func<T> work) => Task.Run(work).GetAwaiter().GetResult();

    private static void OffThread(Action work) => Task.Run(work).GetAwaiter().GetResult();

    private (Scene scene, GameObject go) LiveObject()
    {
        Scene scene = CreateScene(enable: true);
        GameObject go = CreateGameObject("Live");
        scene.Add(go);
        return (scene, go);
    }

    // ---- sessions and continuations -----------------------------------------------------------------

    [Fact]
    public void AwaitingTheNextFrameResumesInThatFramesPump()
    {
        using var loop = new LoopScope();
        bool resumed = false;

        async Task Wait()
        {
            await GameTask.NextFrame();
            resumed = true;
        }

        Task waiting = Wait();
        Assert.False(resumed);

        loop.Pump();

        Assert.True(resumed);
        Assert.True(waiting.IsCompletedSuccessfully);
    }

    [Fact]
    public void AWaitStartedDuringAPumpWaitsForTheNextOne()
    {
        using var loop = new LoopScope();
        int frames = 0;

        async Task Wait()
        {
            await GameTask.NextFrame();
            frames++;
            await GameTask.NextFrame();
            frames++;
        }

        _ = Wait();
        loop.Pump();
        Assert.Equal(1, frames);
        loop.Pump();
        Assert.Equal(2, frames);
    }

    [Fact]
    public void HoppingToAWorkerAndBackEndsOnTheMainThread()
    {
        using var loop = new LoopScope();
        int main = Environment.CurrentManagedThreadId;
        int worker = 0, back = 0;

        async Task Hop()
        {
            await GameTask.WorkerThread();
            worker = Environment.CurrentManagedThreadId;
            await GameTask.MainThread();
            back = Environment.CurrentManagedThreadId;
        }

        Task hop = Hop();
        loop.PumpUntil(() => hop.IsCompleted);

        Assert.NotEqual(main, worker);
        Assert.Equal(main, back);
    }

    [Fact]
    public void MainThreadAwaitOnTheMainThreadContinuesImmediately()
    {
        using var loop = new LoopScope();
        bool ran = false;

        async Task Stay()
        {
            await GameTask.MainThread();
            ran = true;
        }

        _ = Stay();
        Assert.True(ran);
    }

    [Fact]
    public void DelayCountsGameTimeAndStandsStillWhileTimeDoes()
    {
        using var loop = new LoopScope();
        var time = new TimeData { DeltaTime = 0.1f, UnscaledDeltaTime = 0.1f };
        Time.TimeStack.Push(time);
        try
        {
            Task delay = GameTask.Delay(0.25f);

            loop.Pump();
            loop.Pump();
            Assert.False(delay.IsCompleted);

            time.DeltaTime = 0;
            loop.Pump();
            loop.Pump();
            Assert.False(delay.IsCompleted);

            time.DeltaTime = 0.1f;
            loop.Pump();
            Assert.True(delay.IsCompletedSuccessfully);
        }
        finally
        {
            Time.TimeStack.Pop();
        }
    }

    [Fact]
    public void WaitUntilChecksOncePerFrame()
    {
        using var loop = new LoopScope();
        int checks = 0;
        bool open = false;

        Task wait = GameTask.WaitUntil(() => { checks++; return open; });

        loop.Pump();
        loop.Pump();
        open = true;
        loop.Pump();

        Assert.True(wait.IsCompletedSuccessfully);
        Assert.Equal(4, checks); // once up front, then once per frame
    }

    [Fact]
    public void CancellingAFrameWaitCompletesItWithoutAFrame()
    {
        using var loop = new LoopScope();
        using var cancel = new CancellationTokenSource();

        Task wait = GameTask.NextFrame(cancel.Token);
        cancel.Cancel();

        Assert.True(wait.IsCanceled);
    }

    /// <summary>
    /// The play mode bug: a component awaits something slow, play stops, and the rest of its method used to
    /// run against the edit scene once the wait finished.
    /// </summary>
    [Fact]
    public void AContinuationThatResumesAfterItsSessionEndedNeverRuns()
    {
        using var loop = new LoopScope();
        var slow = new TaskCompletionSource();
        bool resumed = false;

        async Task Play()
        {
            await slow.Task;
            resumed = true;
        }

        _ = Play();
        MainThreadContext.Restart();
        slow.SetResult();

        for (int i = 0; i < 5; i++) loop.Pump();
        Assert.False(resumed);
    }

    [Fact]
    public void EndingASessionDropsQueuedWorkCancelsItsTokenAndItsFrameWaits()
    {
        using var loop = new LoopScope();
        CancellationToken session = GameTask.SessionToken;
        Task frame = GameTask.NextFrame();
        bool posted = false;

        GameTask.Post(() => posted = true);
        MainThreadContext.Restart();
        loop.Pump();

        Assert.True(session.IsCancellationRequested);
        Assert.True(frame.IsCanceled);
        Assert.False(posted);
        Assert.False(GameTask.SessionToken.IsCancellationRequested); // the new session is live
    }

    [Fact]
    public void WorkBlockingOnTheMainThreadCarriesAcrossASessionEnd()
    {
        using var loop = new LoopScope();
        int main = Environment.CurrentManagedThreadId;
        int ranOn = 0;

        Task worker = Task.Run(() => GameTask.Run(() => ranOn = Environment.CurrentManagedThreadId));
        Assert.True(SpinWait.SpinUntil(() => loop.Context.PendingCount > 0, 5000));

        // Ended with the blocked call still queued, so only carrying it over keeps the worker from hanging.
        MainThreadContext.Restart();
        loop.PumpUntil(() => worker.IsCompleted);

        Assert.Equal(main, ranOn);
    }

    [Fact]
    public void RunRethrowsWhatTheWorkThrewOnTheCallingThread()
    {
        using var loop = new LoopScope();

        Task<Exception?> worker = Task.Run<Exception?>(() => Record.Exception(() => GameTask.Run(() => throw new FormatException("boom"))));
        loop.PumpUntil(() => worker.IsCompleted);

        Assert.IsType<FormatException>(worker.Result);
    }

    [Fact]
    public void RunReturnsItsResult()
    {
        using var loop = new LoopScope();

        Task<int> worker = Task.Run(() => GameTask.Run(() => Environment.CurrentManagedThreadId));
        loop.PumpUntil(() => worker.IsCompleted);

        Assert.Equal(Environment.CurrentManagedThreadId, worker.Result);
    }

    [Fact]
    public void SendAfterTheLoopStoppedThrowsInsteadOfRunningOnTheCaller()
    {
        MainThreadContext context;
        using (var loop = new LoopScope()) context = loop.Context;
        bool ran = false;

        Assert.Throws<OperationCanceledException>(() => OffThread(() => context.Send(_ => ran = true, null)));

        Assert.False(ran);
        Assert.True(context.IsEnded);
    }

    [Fact]
    public void WithoutALoopEveryThreadCountsAsTheMainThread()
    {
        MainThreadContext.Uninstall();
        Assert.Null(MainThreadContext.Current);
        Assert.True(OffThread(() => GameTask.IsMainThread));

        bool posted = false;
        GameTask.Post(() => posted = true);
        Assert.True(posted);
        Assert.Equal(CancellationToken.None, GameTask.SessionToken);
    }

    // ---- work that outlives its session -------------------------------------------------------------

    /// <summary>The documented worker pattern, with play stopping while the worker computes.</summary>
    [Fact]
    public void AWorkerHoppingBackAfterItsSessionEndedNeverResumes()
    {
        using var loop = new LoopScope();
        var computing = new ManualResetEventSlim(false);
        var release = new ManualResetEventSlim(false);
        bool applied = false;

        async Task Work()
        {
            await GameTask.WorkerThread();
            computing.Set();
            release.Wait();
            await GameTask.MainThread();
            applied = true;
        }

        using (MainThreadContext.EnterSession()) _ = Work();
        Assert.True(computing.Wait(5000));
        MainThreadContext.Restart();
        release.Set();

        for (int i = 0; i < 20; i++)
        {
            loop.Pump();
            Thread.Sleep(1);
        }
        Assert.False(applied);
    }

    [Fact]
    public void AWorkerStartedInANewSessionStillGetsBack()
    {
        using var loop = new LoopScope();
        MainThreadContext.Restart();
        bool applied = false;

        async Task Work()
        {
            await GameTask.WorkerThread();
            await GameTask.MainThread();
            applied = true;
        }

        Task work = Work();
        loop.PumpUntil(() => work.IsCompleted);
        Assert.True(applied);
    }

    [Fact]
    public void AWorkerSeesItsOwnSessionEndEvenAfterANewOneStarted()
    {
        using var loop = new LoopScope();
        var started = new ManualResetEventSlim(false);
        var release = new ManualResetEventSlim(false);
        bool posted = false;

        Task<bool> worker;
        using (MainThreadContext.EnterSession())
        {
            worker = Task.Run(() =>
            {
                started.Set();
                release.Wait();
                GameTask.Post(() => posted = true);
                return GameTask.SessionToken.IsCancellationRequested;
            });
        }

        Assert.True(started.Wait(5000));
        MainThreadContext.Restart();
        release.Set();

        Assert.True(worker.GetAwaiter().GetResult());
        loop.Pump();
        Assert.False(posted);
    }

    [Fact]
    public void WorkStartedFromAComponentStaysBoundToItsSession()
    {
        var (scene, go) = LiveObject();
        go.AddComponent<GameplayHook>();
        using var loop = new LoopScope();
        var release = new ManualResetEventSlim(false);
        Task<bool>? worker = null;
        bool posted = false;

        GameplayHook.OnUpdate = () => worker ??= Task.Run(() =>
        {
            release.Wait();
            GameTask.Post(() => posted = true);
            return GameTask.SessionToken.IsCancellationRequested;
        });
        try { scene.Update(); }
        finally { GameplayHook.OnUpdate = null; }

        MainThreadContext.Restart();
        release.Set();

        Assert.True(worker!.GetAwaiter().GetResult());
        loop.Pump();
        Assert.False(posted);
    }

    /// <summary>A service thread started outside gameplay is not play session work, so a play toggle leaves it working.</summary>
    [Fact]
    public void AThreadStartedOutsideGameplayFollowsTheCurrentSession()
    {
        using var loop = new LoopScope();
        var release = new ManualResetEventSlim(false);
        bool posted = false, cancelled = true;

        var thread = new Thread(() =>
        {
            release.Wait();
            cancelled = GameTask.SessionToken.IsCancellationRequested;
            GameTask.Post(() => posted = true);
        });
        thread.Start();

        MainThreadContext.Restart();
        release.Set();
        thread.Join();
        loop.Pump();

        Assert.False(cancelled);
        Assert.True(posted);
    }

    [Fact]
    public void RestartingInsideAQueuedContinuationLeavesTheThreadOnTheNewSession()
    {
        using var loop = new LoopScope();

        async Task Restarter()
        {
            await GameTask.NextFrame();
            MainThreadContext.Restart();
        }

        _ = Restarter();
        loop.Pump();
        Assert.Same(MainThreadContext.Current, SynchronizationContext.Current);

        bool resumed = false;
        async Task After()
        {
            await GameTask.NextFrame();
            resumed = true;
        }

        _ = After();
        loop.Pump();
        loop.Pump();
        Assert.True(resumed);
    }

    /// <summary>The runtime puts the thread's context back when async code returns, so the next pump has to fix it.</summary>
    [Fact]
    public void RestartingInsideAsyncCodeIsPutRightByTheNextPump()
    {
        using var loop = new LoopScope();

        async Task RestartThenReturn()
        {
            MainThreadContext.Restart();
            await Task.CompletedTask;
        }

        RestartThenReturn().GetAwaiter().GetResult();
        Assert.NotSame(MainThreadContext.Current, SynchronizationContext.Current);

        loop.Pump();
        Assert.Same(MainThreadContext.Current, SynchronizationContext.Current);
    }

    [Fact]
    public void AfterTheLoopStopsWorkersAreRefusedUntilItIsUninstalled()
    {
        var (_, go) = LiveObject();
        using var loop = new LoopScope();
        MainThreadContext.Stop();
        bool ran = false, posted = false;

        Assert.Throws<OperationCanceledException>(() => OffThread(() => GameTask.Run(() => ran = true)));
        OffThread(() => GameTask.Post(() => posted = true));
        Assert.Throws<InvalidOperationException>(() => OffThread(() => go.Transform.Position = Float3.One));

        Task<bool> hop = Task.Run(async () =>
        {
            await GameTask.MainThread();
            return true;
        });
        Assert.False(hop.Wait(200));
        Assert.True(OffThread(() => GameTask.NextFrame()).IsCanceled);

        Assert.False(ran);
        Assert.False(posted);

        MainThreadContext.Uninstall();
        Assert.True(OffThread(() => GameTask.IsMainThread));
    }

    [Fact]
    public void AWaitWhoseCheckEndsTheSessionIsCancelledRatherThanStranded()
    {
        using var loop = new LoopScope();
        int checks = 0;

        Task until = GameTask.WaitUntil(() =>
        {
            if (++checks == 2) MainThreadContext.Restart();
            return false;
        });
        loop.Pump();

        Assert.True(until.IsCanceled);
    }

    [Fact]
    public void ACancellationThatWasATimeoutIsReportedAndADeliberateOneIsNot()
    {
        using var loop = new LoopScope();
        var errors = new List<string>();
        OnLog capture = (message, _, severity) => { if (severity == LogSeverity.Error) lock (errors) errors.Add(message); };
        Debug.OnLog += capture;
        try
        {
            using var cancel = new CancellationTokenSource();
            cancel.Cancel();

            async void TimesOut()
            {
                await Task.Yield();
                throw new TaskCanceledException("request timed out", new TimeoutException());
            }

            async void StopsOnPurpose()
            {
                await Task.Yield();
                cancel.Token.ThrowIfCancellationRequested();
            }

            TimesOut();
            StopsOnPurpose();
            for (int i = 0; i < 3; i++) loop.Pump();

            Assert.Contains(errors, m => m.Contains("request timed out"));
            Assert.DoesNotContain(errors, m => m.Contains("canceled") && !m.Contains("request timed out"));
        }
        finally
        {
            Debug.OnLog -= capture;
        }
    }

    [Fact]
    public void FrameWaitsCompleteAsCancelledWhenTheirSessionEnds()
    {
        using var loop = new LoopScope();

        Task frames = GameTask.Frames(3);
        Task delay = GameTask.Delay(10);
        Task until = GameTask.WaitUntil(() => false);
        loop.Pump();

        MainThreadContext.Restart();

        Assert.True(frames.IsCanceled);
        Assert.True(delay.IsCanceled);
        Assert.True(until.IsCanceled);
    }

    [Fact]
    public void AFrameWaitOnAnEndedSessionIsCancelledRatherThanThrowing()
    {
        using var loop = new LoopScope();
        MainThreadContext ended = loop.Context;
        MainThreadContext.Restart();

        Task wait = ended.NextFrame();

        Assert.True(wait.IsCanceled);
    }

    [Fact]
    public void FramesCountsFrames()
    {
        using var loop = new LoopScope();

        Task frames = GameTask.Frames(2);
        loop.Pump();
        Assert.False(frames.IsCompleted);
        loop.Pump();
        Assert.True(frames.IsCompletedSuccessfully);
    }

    [Fact]
    public void AWaitConditionThatThrowsFaultsTheWait()
    {
        using var loop = new LoopScope();
        bool thrown = false;

        Task until = GameTask.WaitUntil(() => thrown ? throw new FormatException("bad") : false);
        thrown = true;
        loop.Pump();

        Assert.IsType<FormatException>(until.Exception!.InnerException);
    }

    [Fact]
    public void AnAsyncVoidThatFailsAfterItsSessionEndedIsStillReported()
    {
        using var loop = new LoopScope();
        var slow = new TaskCompletionSource();
        string? logged = null;
        OnLog capture = (message, _, severity) => { if (severity == LogSeverity.Error && message.Contains("late failure")) logged = message; };
        Debug.OnLog += capture;
        try
        {
            async void Fails()
            {
                await slow.Task.ConfigureAwait(false);
                throw new FormatException("late failure");
            }

            Fails();
            MainThreadContext.Restart();
            slow.SetResult();

            Assert.True(SpinWait.SpinUntil(() => logged != null, 5000));
        }
        finally
        {
            Debug.OnLog -= capture;
        }
    }

    [Fact]
    public void BlockedWorkRunsWhenTheLoopStopsWithoutHoldingTheContext()
    {
        MainThreadContext context;
        Task worker;
        bool reentered = false;

        using (var loop = new LoopScope())
        {
            context = loop.Context;

            // The work waits on another thread that needs this same context, which deadlocked while it was held.
            worker = Task.Run(() => context.Send(_ => reentered = Task.Run(() => { context.NextFrame(); }).Wait(5000), null));
            Assert.True(SpinWait.SpinUntil(() => context.PendingCount > 0, 5000));
        }

        Assert.True(worker.Wait(5000));
        Assert.True(reentered);
    }

    [Fact]
    public void InstallingOverALoopCarriesBlockedWorkToTheNewOne()
    {
        using var loop = new LoopScope();
        MainThreadContext first = loop.Context;
        int ranOn = 0;

        Task worker = Task.Run(() => GameTask.Run(() => ranOn = Environment.CurrentManagedThreadId));
        Assert.True(SpinWait.SpinUntil(() => first.PendingCount > 0, 5000));

        MainThreadContext.Install();
        Assert.False(worker.IsCompleted);
        loop.PumpUntil(() => worker.IsCompleted);

        Assert.Equal(Environment.CurrentManagedThreadId, ranOn);
    }

    /// <summary>
    /// The editor ends the session at the scene swap, so the outgoing scene's teardown runs in the session it
    /// belongs to, and the incoming scene starts in the next one.
    /// </summary>
    [Fact]
    public void EndingTheSessionAtASwapSplitsTheTwoScenesBetweenSessions()
    {
        using var loop = new LoopScope();

        Scene first = CreateScene();
        GameObject oldObject = CreateGameObject("Old");
        first.Add(oldObject);
        var outgoing = oldObject.AddComponent<SessionProbe>();
        Scene.Load(first);
        Scene.ProcessPendingLoad();
        CancellationToken firstSession = GameTask.SessionToken;

        Scene second = CreateScene();
        GameObject newObject = CreateGameObject("New");
        second.Add(newObject);
        var incoming = newObject.AddComponent<SessionProbe>();
        Scene.EndSessionOnSwap = true;
        Scene.Load(second);
        Scene.ProcessPendingLoad();

        Assert.Equal(firstSession, outgoing.DisabledIn);
        Assert.False(outgoing.DisabledInEndedSession);
        Assert.True(firstSession.IsCancellationRequested);
        Assert.NotEqual(firstSession, incoming.EnabledIn);
        Assert.False(incoming.EnabledIn.IsCancellationRequested);
        Assert.False(Scene.EndSessionOnSwap);
    }

    [Fact]
    public void ASkippedSwapStillEndsTheSession()
    {
        using var loop = new LoopScope();
        CancellationToken session = GameTask.SessionToken;
        Scene doomed = CreateScene();

        Scene.EndSessionOnSwap = true;
        Scene.Load(doomed);
        doomed.Dispose();
        Scene.ProcessPendingLoad();

        Assert.True(session.IsCancellationRequested);
        Assert.False(Scene.EndSessionOnSwap);
    }

    // ---- ownership checks ---------------------------------------------------------------------------

    [Fact]
    public void DisposingALiveComponentFromAnotherThreadThrowsBeforeTearingAnythingDown()
    {
        var (_, go) = LiveObject();
        var listener = go.AddComponent<PhysicsListener>();
        CancellationToken token = listener.DestroyCancellationToken;
        using var loop = new LoopScope();

        Assert.Throws<InvalidOperationException>(() => OffThread(listener.Dispose));

        Assert.False(listener.IsDisposed);
        Assert.False(token.IsCancellationRequested);
        Assert.Same(listener, go.GetComponent<PhysicsListener>());
    }

    [Fact]
    public void DisposingALiveObjectFromAnotherThreadThrowsBeforeTearingAnythingDown()
    {
        var (scene, root) = LiveObject();
        GameObject child = CreateGameObject("Child");
        scene.Add(child);
        child.SetParent(root);
        var listener = child.AddComponent<PhysicsListener>();
        using var loop = new LoopScope();

        Assert.Throws<InvalidOperationException>(() => OffThread(child.Dispose));
        Assert.Throws<InvalidOperationException>(() => OffThread(root.Dispose));

        Assert.False(child.IsDisposed);
        Assert.False(root.IsDisposed);
        Assert.False(listener.IsDisposed);
        Assert.Contains(child, root.Children);
    }

    [Fact]
    public void DisposingALiveSceneFromAnotherThreadThrows()
    {
        var (scene, _) = LiveObject();
        using var loop = new LoopScope();

        Assert.Throws<InvalidOperationException>(() => OffThread(scene.Dispose));
        Assert.False(scene.IsDisposed);
    }

    [Fact]
    public void ReadingTheCurrentSceneFromAnotherThreadCannotLeaveAnInactiveOneBehind()
    {
        Scene.Shutdown();
        using var loop = new LoopScope();

        Assert.Throws<InvalidOperationException>(() => OffThread(() => Scene.Current));

        Scene current = Scene.Current;
        Assert.True(current.IsActive);
        current.Dispose();
    }

    [Fact]
    public void PhysicsAndAnimationComponentsOnALiveObjectAreChecked()
    {
        var (_, go) = LiveObject();
        var body = go.AddComponent<Rigidbody3D>();
        var box = go.AddComponent<BoxCollider>();
        var animator = go.AddComponent<Animator>();
        float mass = body.Mass;
        using var loop = new LoopScope();

        Assert.Throws<InvalidOperationException>(() => OffThread(() => body.Mass = mass * 2));
        Assert.Throws<InvalidOperationException>(() => OffThread(() => body.LinearVelocity = Float3.UnitX));
        Assert.Throws<InvalidOperationException>(() => OffThread(() => body.AddForce(Float3.UnitY)));
        Assert.Throws<InvalidOperationException>(() => OffThread(() => box.Size = new Float3(3)));
        Assert.Throws<InvalidOperationException>(() => OffThread(() => animator.SetFloat("Speed", 1)));

        Assert.Equal(mass, body.Mass);
    }

    // ---- ownership checks, structural ---------------------------------------------------------------

    [Fact]
    public void MovingALiveObjectFromAnotherThreadThrowsAndChangesNothing()
    {
        var (_, go) = LiveObject();
        using var loop = new LoopScope();

        var error = Assert.Throws<InvalidOperationException>(() => OffThread(() => go.Transform.Position = new Float3(5, 0, 0)));

        Assert.Contains("Position", error.Message);
        Assert.Equal(Float3.Zero, go.Transform.Position);
    }

    [Fact]
    public void EveryStructuralChangeToALiveSceneIsChecked()
    {
        var (scene, go) = LiveObject();
        GameObject other = CreateGameObject("Other");
        scene.Add(other);
        GameObject loose = CreateGameObject("Loose");
        Scene next = CreateScene();
        using var loop = new LoopScope();

        Assert.Throws<InvalidOperationException>(() => OffThread(() => go.Transform.LocalScale = new Float3(2)));
        Assert.Throws<InvalidOperationException>(() => OffThread(() => go.Transform.Rotate(new Float3(0, 90, 0))));
        Assert.Throws<InvalidOperationException>(() => OffThread(() => go.Transform.SetPositionAndRotation(Float3.One, Quaternion.Identity)));
        Assert.Throws<InvalidOperationException>(() => OffThread(() => go.Enabled = false));
        Assert.Throws<InvalidOperationException>(() => OffThread(() => go.SetParent(other)));
        Assert.Throws<InvalidOperationException>(() => OffThread(() => loose.SetParent(go)));
        Assert.Throws<InvalidOperationException>(() => OffThread(() => go.AddComponent<PhysicsListener>()));
        Assert.Throws<InvalidOperationException>(() => OffThread(() => scene.Add(loose)));
        Assert.Throws<InvalidOperationException>(() => OffThread(() => scene.Remove(go)));
        Assert.Throws<InvalidOperationException>(() => OffThread(() => Scene.Load(next)));

        Assert.True(go.Enabled);
        Assert.Null(go.Parent);
        Assert.Null(go.GetComponent<PhysicsListener>());
        Assert.Equal(scene, go.Scene);
    }

    [Fact]
    public void AComponentOnALiveObjectCannotBeToggledFromAnotherThread()
    {
        var (_, go) = LiveObject();
        var listener = go.AddComponent<PhysicsListener>();
        using var loop = new LoopScope();

        Assert.Throws<InvalidOperationException>(() => OffThread(() => listener.Enabled = false));
        Assert.True(listener.Enabled);
    }

    /// <summary>Objects outside a running scene are free to build anywhere, which is how background spawning works.</summary>
    [Fact]
    public void ObjectsBuiltOffSceneOnAWorkerCanJoinTheSceneOnTheMainThread()
    {
        Scene scene = CreateScene(enable: true);
        using var loop = new LoopScope();

        GameObject built = OffThread(() =>
        {
            var root = new GameObject("Built");
            var child = new GameObject("Child");
            child.SetParent(root);
            root.AddComponent<PhysicsListener>();
            root.Transform.Position = new Float3(1, 2, 3);
            return root;
        });

        scene.Add(built);

        Assert.Equal(scene, built.Scene);
        Assert.Equal(new Float3(1, 2, 3), built.Transform.Position);
        Assert.NotNull(built.GetComponent<PhysicsListener>());
        built.Dispose();
    }

    [Fact]
    public void DestroyFromAnotherThreadIsQueuedForTheMainThread()
    {
        var (_, go) = LiveObject();
        using var loop = new LoopScope();

        OffThread(go.Destroy);
        Assert.False(go.IsDisposed);

        EngineObject.ProcessDestroyed();
        Assert.True(go.IsDisposed);
    }

    [Fact]
    public void ReadingWorldTransformsFromAnotherThreadGivesTheRightAnswer()
    {
        var (scene, parent) = LiveObject();
        GameObject child = CreateGameObject("Child");
        scene.Add(child);
        child.SetParent(parent);
        parent.Transform.Position = new Float3(10, 0, 0);
        child.Transform.LocalPosition = new Float3(0, 1, 0);
        using var loop = new LoopScope();

        // Stale on purpose, so a cached read would have to rebuild.
        parent.Transform.Position = new Float3(15, 0, 0);
        uint parentBuilds = parent.Transform.WorldVersion, childBuilds = child.Transform.WorldVersion;

        Float3 seen = OffThread(() => child.Transform.Position);
        Assert.Equal(new Float3(15, 1, 0), seen);

        // Computed without touching the cache the main thread owns.
        Assert.Equal(parentBuilds, parent.Transform.WorldVersion);
        Assert.Equal(childBuilds, child.Transform.WorldVersion);

        // The main thread's cache still follows changes after a read from elsewhere.
        parent.Transform.Position = new Float3(20, 0, 0);
        Assert.Equal(new Float3(20, 1, 0), child.Transform.Position);
    }

    [Fact]
    public void PhysicsQueriesFromAnotherThreadThrow()
    {
        var (scene, _) = LiveObject();
        using var loop = new LoopScope();
        var hits = new List<RaycastHit>();

        Assert.Throws<InvalidOperationException>(() => OffThread(() => scene.Physics.RaycastAll(Float3.Zero, Float3.UnitY, 10, hits)));
        Assert.Throws<InvalidOperationException>(() => OffThread(() => scene.Physics.Raycast(Float3.Zero, Float3.UnitY, 10)));
        Assert.Throws<InvalidOperationException>(() => OffThread(() => scene.Physics.OverlapSphere(Float3.Zero, 1, new List<ShapeCastHit>())));
        Assert.Equal(0, scene.Physics.RaycastAll(Float3.Zero, Float3.UnitY, 10, hits));
    }

    [Fact]
    public void WithoutALoopOtherThreadsAreNotStopped()
    {
        var (_, go) = LiveObject();

        OffThread(() => go.Transform.Position = new Float3(3, 0, 0));

        Assert.Equal(new Float3(3, 0, 0), go.Transform.Position);
    }

    // ---- component lifetime -------------------------------------------------------------------------

    [Fact]
    public void DestroyCancellationTokenCancelsWhenTheComponentIsDestroyed()
    {
        var (_, go) = LiveObject();
        var listener = go.AddComponent<PhysicsListener>();
        CancellationToken token = listener.DestroyCancellationToken;

        listener.Destroy();
        Assert.False(token.IsCancellationRequested);

        EngineObject.ProcessDestroyed();
        Assert.True(token.IsCancellationRequested);
        Assert.True(listener.DestroyCancellationToken.IsCancellationRequested);
    }

    [Fact]
    public void DestroyCancellationTokenCancelsWithItsGameObject()
    {
        var (_, go) = LiveObject();
        CancellationToken token = go.AddComponent<PhysicsListener>().DestroyCancellationToken;

        go.Dispose();

        Assert.True(token.IsCancellationRequested);
    }

    [Fact]
    public void DestroyCancellationTokenCancelsEvenWhenOnDisposeSkipsBase()
    {
        var (_, go) = LiveObject();
        var careless = go.AddComponent<ForgetsBaseDispose>();
        CancellationToken token = careless.DestroyCancellationToken;

        careless.Dispose();

        Assert.True(token.IsCancellationRequested);
    }

    // ---- shared engine state ------------------------------------------------------------------------

    [Fact]
    public void ReadingTimeFromAnotherThreadWhileTheLoopSwapsItNeverThrows()
    {
        TimeData[] saved = Time.TimeStack.ToArray();
        var frame = new TimeData { DeltaTime = 0.5f };
        using var stop = new CancellationTokenSource();

        Task<Exception?> reader = Task.Run<Exception?>(() => Record.Exception(() =>
        {
            while (!stop.IsCancellationRequested)
            {
                float dt = Time.DeltaTime;
                if (dt != 0.5f && dt != 0f && dt != Time.FixedDeltaTime) throw new InvalidOperationException($"Read a value nobody wrote: {dt}");
            }
        }));

        try
        {
            var clock = Stopwatch.StartNew();
            while (clock.ElapsedMilliseconds < 200)
            {
                Time.TimeStack.Clear();
                Time.TimeStack.Push(frame);
            }
        }
        finally
        {
            stop.Cancel();
            Time.TimeStack.Clear();
            for (int i = saved.Length - 1; i >= 0; i--) Time.TimeStack.Push(saved[i]);
        }

        Assert.Null(reader.GetAwaiter().GetResult());
    }
}

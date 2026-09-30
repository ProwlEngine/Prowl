// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Threading;

using Prowl.Editor.Core.Tasks;
using Prowl.Editor.GUI.Panels;
using Prowl.Runtime;
using Prowl.Runtime.Tasks;

using Xunit;

namespace Prowl.Editor.Test;

/// <summary>
/// The editor's side of the main thread model: logs arriving from other threads, and editor waits that must
/// outlive the game's async session.
/// </summary>
public class EditorThreadingTests : IDisposable
{
    private readonly SynchronizationContext? _previous = SynchronizationContext.Current;

    public void Dispose()
    {
        MainThreadContext.Uninstall();
        SynchronizationContext.SetSynchronizationContext(_previous);
    }

    private sealed class Waiter : EditorTask
    {
        public bool Done;
        public Exception? Failure;

        public async void Start(Func<bool> condition)
        {
            try { await IdleOnCondition(condition); }
            catch (Exception e) { Failure = e; }
            Done = true;
        }
    }

    /// <summary>
    /// Many threads logging the same line while the main thread reads the console. Every repeat must be counted,
    /// and the reads must never trip over a list being changed underneath them.
    /// </summary>
    [Fact]
    public void LogsFromManyThreadsAreAllCountedWhileTheConsoleIsRead()
    {
        MainThreadContext.Install();
        ConsolePanel.EnsureSubscribed();
        string line = $"threaded {Guid.NewGuid()}";
        const int threads = 8, each = 250;

        int before = ConsolePanel.LogCounts().info;

        Task[] writers = new Task[threads];
        for (int t = 0; t < threads; t++)
            writers[t] = Task.Run(() => { for (int i = 0; i < each; i++) Debug.Log(line); });

        while (!Task.WhenAll(writers).IsCompleted)
            ConsolePanel.LogCounts();

        Assert.Equal(before + threads * each, ConsolePanel.LogCounts().info);
        Assert.Equal(line, ConsolePanel.LastLog()!.Value.message);
    }

    /// <summary>With no loop every thread counts as the main one, which must not let them write the store together.</summary>
    [Fact]
    public void LogsFromManyThreadsAreAllCountedWithoutALoop()
    {
        MainThreadContext.Uninstall();
        ConsolePanel.EnsureSubscribed();
        string line = $"loopless {Guid.NewGuid()}";
        const int threads = 8, each = 250;

        int before = ConsolePanel.LogCounts().info;

        Task[] writers = new Task[threads];
        for (int t = 0; t < threads; t++)
            writers[t] = Task.Run(() => { for (int i = 0; i < each; i++) Debug.Log(line); });

        Task readers = Task.Run(() => { while (!Task.WhenAll(writers).IsCompleted) ConsolePanel.LogCounts(); });
        while (!Task.WhenAll(writers).IsCompleted) ConsolePanel.LastLog();
        readers.Wait();

        Assert.Equal(before + threads * each, ConsolePanel.LogCounts().info);
    }

    [Fact]
    public void AWorkerLogThatArrivedFirstStaysFirst()
    {
        MainThreadContext.Install();
        ConsolePanel.EnsureSubscribed();
        string tag = Guid.NewGuid().ToString();

        Task.Run(() => Debug.Log($"{tag} from worker")).Wait();
        Debug.Log($"{tag} from main");

        Assert.Equal($"{tag} from main", ConsolePanel.LastLog()!.Value.message);
    }

    [Fact]
    public void AnEditorWaitResumesOnceItsConditionHolds()
    {
        var waiter = new Waiter();
        bool open = false;

        waiter.Start(() => open);
        EditorTask.Poll();
        Assert.False(waiter.Done);

        open = true;
        EditorTask.Poll();
        Assert.True(waiter.Done);
    }

    [Fact]
    public void AnEditorWaitWhoseConditionThrowsRethrowsAtTheAwait()
    {
        var waiter = new Waiter();
        bool broken = false;

        waiter.Start(() => broken ? throw new FormatException("bad condition") : false);
        broken = true;
        EditorTask.Poll();

        Assert.True(waiter.Done);
        Assert.IsType<FormatException>(waiter.Failure);
    }

    /// <summary>Entering or leaving play mode ends the game's session, which must not strand the editor's own waits.</summary>
    [Fact]
    public void AnEditorWaitSurvivesTheGameSessionEnding()
    {
        MainThreadContext.Install();
        var waiter = new Waiter();
        bool open = false;

        waiter.Start(() => open);
        MainThreadContext.Restart();
        open = true;
        EditorTask.Poll();

        Assert.True(waiter.Done);
    }
}

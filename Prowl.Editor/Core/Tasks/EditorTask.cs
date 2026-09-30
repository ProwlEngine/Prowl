// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;

namespace Prowl.Editor.Core.Tasks;

/// <summary> Base class for editor tasks that provides a utility to asynchronously wait until a condition is met. </summary>
public class EditorTask
{
    private static readonly List<IdleAwaitable> s_waiting = new();

    /// <summary>
    /// Resumes on the main thread once the condition holds, checked every editor frame. Unlike awaiting a task,
    /// this survives entering and leaving play mode and script reloads, which end the game's async session.
    /// A condition that throws resumes the wait by rethrowing it.
    /// </summary>
    public IdleAwaitable IdleOnCondition(Func<bool> condition) => new(condition);

    /// <summary>Resumes every wait whose condition now holds. Called once per editor frame on the main thread.</summary>
    internal static void Poll()
    {
        IdleAwaitable[] waiting;
        lock (s_waiting)
        {
            if (s_waiting.Count == 0) return;
            waiting = [.. s_waiting];
        }

        foreach (IdleAwaitable wait in waiting)
        {
            try
            {
                if (!wait.Condition()) continue;
            }
            catch (Exception ex)
            {
                wait.Failure = ExceptionDispatchInfo.Capture(ex);
            }

            lock (s_waiting) s_waiting.Remove(wait);

            try { wait.Continuation!(); }
            catch (Exception ex) { Runtime.Debug.LogError($"[EditorTask] A continuation threw: {ex.Message}\n{ex.StackTrace}"); }
        }
    }

    public sealed class IdleAwaitable : INotifyCompletion
    {
        internal readonly Func<bool> Condition;
        internal Action? Continuation;
        internal ExceptionDispatchInfo? Failure;

        internal IdleAwaitable(Func<bool> condition) => Condition = condition;

        public IdleAwaitable GetAwaiter() => this;

        public bool IsCompleted => Condition();

        public void OnCompleted(Action continuation)
        {
            Continuation = continuation;
            lock (s_waiting) s_waiting.Add(this);
        }

        public void GetResult()
        {
            // An editor flow outlives the gameplay session it may have started in, so it follows the current one.
            Runtime.Tasks.MainThreadContext.LeaveSession();
            Failure?.Throw();
        }
    }
}

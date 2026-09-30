// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Prowl.Editor.Core.Tasks;

/// <summary> Base class for editor tasks that provides a utility to asynchronously wait until a condition is met. </summary>
public class EditorTask
{
    private static readonly List<(Func<bool> Condition, Action Continuation)> s_waiting = new();

    /// <summary>
    /// Resumes on the main thread once the condition holds, checked every editor frame. Unlike awaiting a task,
    /// this survives entering and leaving play mode and script reloads, which end the game's async session.
    /// </summary>
    public IdleAwaitable IdleOnCondition(Func<bool> condition) => new(condition);

    /// <summary>Resumes every wait whose condition now holds. Called once per editor frame.</summary>
    internal static void Poll()
    {
        for (int i = s_waiting.Count - 1; i >= 0; i--)
        {
            var (condition, continuation) = s_waiting[i];

            bool ready;
            try { ready = condition(); }
            catch (Exception ex)
            {
                Runtime.Debug.LogError($"[EditorTask] A wait condition threw: {ex.Message}\n{ex.StackTrace}");
                ready = true;
            }

            if (!ready) continue;

            s_waiting.RemoveAt(i);
            try { continuation(); }
            catch (Exception ex) { Runtime.Debug.LogError($"[EditorTask] A continuation threw: {ex.Message}\n{ex.StackTrace}"); }

            // A continuation can add or finish other waits, so the index may now be past the end.
            if (i > s_waiting.Count) i = s_waiting.Count;
        }
    }

    public readonly struct IdleAwaitable : INotifyCompletion
    {
        private readonly Func<bool> _condition;

        public IdleAwaitable(Func<bool> condition) => _condition = condition;

        public IdleAwaitable GetAwaiter() => this;

        public bool IsCompleted => _condition();

        public void OnCompleted(Action continuation) => s_waiting.Add((_condition, continuation));

        public void GetResult() { }
    }
}

// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Reflection;

using Prowl.Editor.Core;
using Prowl.Editor.Utils;
using Prowl.Editor.GUI.SceneView;
using Prowl.Runtime;

namespace Prowl.Editor;

/// <summary>Mark a static void method to be called when the scene is saved.</summary>
[AttributeUsage(AttributeTargets.Method)]
public class OnSceneSavedAttribute : Attribute { }

/// <summary>Mark a static void method to be called after undo or redo is performed.</summary>
[AttributeUsage(AttributeTargets.Method)]
public class OnUndoRedoAttribute : Attribute { }

/// <summary>
/// Discovers and registers static callback methods marked with editor callback attributes.
/// Call Initialize() once at editor startup; Clear()/Initialize() again around a script
/// hot-reload so delegates bound to the old AssemblyLoadContext's MethodInfos are dropped
/// and callbacks in the freshly-loaded assemblies get (re-)registered.
/// </summary>
public static class EditorCallbacks
{
    private static bool _initialized;
    private static readonly List<Action> _sceneSavedDelegates = new();
    private static readonly List<Action> _undoRedoDelegates = new();

    public static void Initialize()
    {
        if (_initialized) return;
        _initialized = true;

        int sceneSaved = 0, undoRedo = 0;

        foreach (var type in EditorUtils.GetAllTypes())
        {
            foreach (var method in type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (method.ReturnType != typeof(void) || method.GetParameters().Length != 0) continue;

                if (method.GetCustomAttribute<OnSceneSavedAttribute>() != null)
                {
                    var del = (Action)Delegate.CreateDelegate(typeof(Action), method);
                    EditorSceneManager.OnSceneSaved += del;
                    _sceneSavedDelegates.Add(del);
                    sceneSaved++;
                }

                if (method.GetCustomAttribute<OnUndoRedoAttribute>() != null)
                {
                    var del = (Action)Delegate.CreateDelegate(typeof(Action), method);
                    Undo.OnUndoRedo += del;
                    _undoRedoDelegates.Add(del);
                    undoRedo++;
                }
            }
        }

        Debug.Log($"[EditorCallbacks] Registered {sceneSaved} OnSceneSaved, {undoRedo} OnUndoRedo callbacks.");
    }

    /// <summary>
    /// Unsubscribe every callback registered by <see cref="Initialize"/> and allow it to run
    /// again. Call before a script assembly unload any delegate left subscribed here is bound
    /// to a user-script MethodInfo and would pin the dying AssemblyLoadContext alive.
    /// </summary>
    public static void Clear()
    {
        foreach (var del in _sceneSavedDelegates)
            EditorSceneManager.OnSceneSaved -= del;
        _sceneSavedDelegates.Clear();

        foreach (var del in _undoRedoDelegates)
            Undo.OnUndoRedo -= del;
        _undoRedoDelegates.Clear();

        _initialized = false;
    }
}

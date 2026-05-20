// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Reflection;

using Prowl.Editor.Core;
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
/// Call Initialize() once at editor startup.
/// </summary>
public static class EditorCallbacks
{
    private static bool _initialized;

    public static void Initialize()
    {
        if (_initialized) return;
        _initialized = true;

        int sceneSaved = 0, undoRedo = 0;

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            try
            {
                foreach (var type in assembly.GetTypes())
                {
                    foreach (var method in type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                    {
                        if (method.GetCustomAttribute<OnSceneSavedAttribute>() != null)
                        {
                            var del = (Action)Delegate.CreateDelegate(typeof(Action), method);
                            EditorSceneManager.OnSceneSaved += del;
                            sceneSaved++;
                        }

                        if (method.GetCustomAttribute<OnUndoRedoAttribute>() != null)
                        {
                            var del = (Action)Delegate.CreateDelegate(typeof(Action), method);
                            Undo.OnUndoRedo += del;
                            undoRedo++;
                        }
                    }
                }
            }
            catch { /* skip assemblies that can't be reflected */ }
        }

        Debug.Log($"[EditorCallbacks] Registered {sceneSaved} OnSceneSaved, {undoRedo} OnUndoRedo callbacks.");
    }
}

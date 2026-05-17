// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

using Prowl.Runtime;

namespace Prowl.Editor.GUI.SceneView;

/// <summary>
/// Attribute to register an ISceneViewEditor implementation for a specific component type.
/// When a GameObject with the target component is selected, the scene view activates this editor.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public class SceneViewEditorForAttribute : Attribute
{
    public Type ComponentType { get; }
    public SceneViewEditorForAttribute(Type componentType) => ComponentType = componentType;
}

/// <summary>
/// Discovers and manages ISceneViewEditor implementations.
/// Activates/deactivates editors based on the current selection.
/// </summary>
public static class SceneViewEditorRegistry
{
    private struct Entry
    {
        public Type ComponentType;
        public Type EditorType;
        public int Priority;
    }

    private static List<Entry> _entries = [];
    private static readonly Dictionary<Type, ISceneViewEditor> _editorCache = [];
    private static bool _initialized;

    /// <summary>The currently active scene view editor, or null.</summary>
    public static ISceneViewEditor? ActiveEditor { get; private set; }

    /// <summary>The GameObject the active editor is targeting.</summary>
    public static GameObject? ActiveTarget { get; private set; }

    public static void Initialize()
    {
        if (_initialized) return;
        _initialized = true;
        _entries = [];

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            try
            {
                foreach (var type in assembly.GetTypes())
                {
                    if (!typeof(ISceneViewEditor).IsAssignableFrom(type) || type.IsInterface || type.IsAbstract)
                        continue;

                    var attr = type.GetCustomAttribute<SceneViewEditorForAttribute>();
                    if (attr == null) continue;

                    var instance = (ISceneViewEditor)Activator.CreateInstance(type)!;
                    _editorCache[type] = instance;

                    _entries.Add(new Entry
                    {
                        ComponentType = attr.ComponentType,
                        EditorType = type,
                        Priority = instance.Priority,
                    });
                }
            }
            catch { /* skip assemblies that can't be reflected */ }
        }

        _entries.Sort((a, b) => a.Priority.CompareTo(b.Priority));
        Debug.Log($"[SceneViewEditorRegistry] Registered {_entries.Count} scene view editor(s).");
    }

    /// <summary>
    /// Update the active editor based on the current selection.
    /// Call this when selection changes.
    /// </summary>
    public static void UpdateFromSelection()
    {
        if (!_initialized) Initialize();

        var selectedGO = Selection.GetSelected<GameObject>().FirstOrDefault();
        if (selectedGO == null || selectedGO == ActiveTarget)
        {
            if (selectedGO == null && ActiveEditor != null)
                Deactivate();
            return;
        }

        // Check if any registered editor matches a component on this GO
        foreach (var entry in _entries)
        {
            if (selectedGO.GetComponent(entry.ComponentType) != null)
            {
                if (ActiveEditor != null && ActiveEditor.GetType() == entry.EditorType && ActiveTarget == selectedGO)
                    return; // Already active for this GO

                Deactivate();

                ActiveEditor = _editorCache[entry.EditorType];
                ActiveTarget = selectedGO;
                ActiveEditor.OnActivate(selectedGO);
                return;
            }
        }

        // No matching editor deactivate
        if (ActiveEditor != null)
            Deactivate();
    }

    /// <summary>Force deactivate the current editor.</summary>
    public static void Deactivate()
    {
        ActiveEditor?.OnDeactivate();
        ActiveEditor = null;
        ActiveTarget = null;
    }
}

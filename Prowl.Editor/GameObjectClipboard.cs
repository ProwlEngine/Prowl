// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Echo;
using Prowl.Runtime;
using Prowl.Runtime.Resources;

namespace Prowl.Editor;

/// <summary>
/// Handles copy, paste, and duplicate operations for GameObjects using Echo serialization.
/// Copies are placed on the system clipboard as Echo text, integrating with Paper's
/// text field clipboard so that pasting into a text field yields the serialized data.
/// </summary>
public static class GameObjectClipboard
{
    private const string ClipboardHeader = "ProwlGameObjects:";

    /// <summary>
    /// Deep-copy the given GameObjects to the system clipboard as serialized Echo text.
    /// Filters out children whose ancestors are also in the selection to avoid duplicates.
    /// </summary>
    public static void Copy(IEnumerable<GameObject> gameObjects)
    {
        var roots = FilterToRoots(gameObjects);
        var list = new List<EchoObject>();
        foreach (var go in roots)
        {
            var echo = Serializer.Serialize(typeof(object), go);
            if (echo != null)
                list.Add(echo);
        }

        if (list.Count == 0) return;

        // Wrap in a compound with a header tag so we can identify our clipboard content
        var root = EchoObject.NewList();
        foreach (var item in list)
            root.ListAdd(item);

        string text = ClipboardHeader + root.WriteToString();
        Input.Clipboard = text;
    }

    /// <summary>
    /// Paste GameObjects from the system clipboard into the scene.
    /// Returns the list of newly created GameObjects (already added to the scene and selected).
    /// </summary>
    public static List<GameObject> Paste(GameObject? parent = null)
    {
        var results = new List<GameObject>();

        string? text = Input.Clipboard;
        if (string.IsNullOrEmpty(text) || !text.StartsWith(ClipboardHeader))
            return results;

        string echoText = text[ClipboardHeader.Length..];

        try
        {
            var root = EchoObject.ReadFromString(echoText);
            if (root == null || root.TagType != EchoType.List) return results;

            var scene = Scene.Current;
            if (scene == null) return results;

            foreach (var item in root.List)
            {
                var go = Serializer.Deserialize<GameObject>(item);
                if (go == null) continue;

                go.Name = MakeUniqueSiblingName(go.Name, parent, scene);
                scene.Add(go);
                if (parent != null)
                    go.SetParent(parent);
                results.Add(go);
            }

            if (results.Count > 0)
            {
                Selection.Clear();
                foreach (var go in results)
                    Selection.AddToSelection(go);
                EditorSceneManager.IsDirty = true;
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"Failed to paste GameObjects: {ex.Message}");
        }

        return results;
    }

    /// <summary>
    /// Deep-duplicate the given GameObjects in-place using Echo serialization.
    /// Filters out children whose ancestors are also in the selection to avoid duplicates.
    /// Returns the list of newly created duplicates (already added to the scene and selected).
    /// </summary>
    public static List<GameObject> Duplicate(IEnumerable<GameObject> gameObjects)
    {
        var results = new List<GameObject>();
        var scene = Scene.Current;
        if (scene == null) return results;

        foreach (var source in FilterToRoots(gameObjects))
        {
            try
            {
                var echo = Serializer.Serialize(typeof(object), source);
                if (echo == null) continue;

                var clone = Serializer.Deserialize<GameObject>(echo);
                if (clone == null) continue;

                clone.Name = MakeUniqueSiblingName(source.Name, source.Parent, scene);
                scene.Add(clone);
                if (source.Parent != null)
                    clone.SetParent(source.Parent);
                results.Add(clone);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Failed to duplicate '{source.Name}': {ex.Message}");
            }
        }

        if (results.Count > 0)
        {
            Selection.Clear();
            foreach (var go in results)
                Selection.AddToSelection(go);
            EditorSceneManager.IsDirty = true;
        }

        return results;
    }

    /// <summary>
    /// Returns a name that doesn't collide with any of <paramref name="parent"/>'s children
    /// (or <paramref name="scene"/>'s root objects when <paramref name="parent"/> is null).
    /// Strips a trailing " (N)" from <paramref name="desired"/> and increments N instead of
    /// stacking suffixes — e.g. "Cube" → "Cube (1)", "Cube (3)" → "Cube (4)".
    /// </summary>
    private static string MakeUniqueSiblingName(string desired, GameObject? parent, Scene scene)
    {
        string baseName = desired;
        int startNum = 1;

        // Parse a trailing " (N)" so we increment rather than appending again.
        if (desired.EndsWith(")"))
        {
            int openIdx = desired.LastIndexOf(" (");
            if (openIdx > 0)
            {
                string numStr = desired.Substring(openIdx + 2, desired.Length - openIdx - 3);
                if (int.TryParse(numStr, out int parsed) && parsed > 0)
                {
                    baseName = desired.Substring(0, openIdx);
                    startNum = parsed + 1;
                }
            }
        }

        var taken = new HashSet<string>(StringComparer.Ordinal);
        IEnumerable<GameObject> siblings = parent != null ? parent.Children : scene.RootObjects;
        foreach (var s in siblings)
            taken.Add(s.Name);

        // Bare base name is good if it's free (e.g. pasting into a different parent).
        if (!taken.Contains(baseName)) return baseName;

        int n = startNum;
        while (true)
        {
            string candidate = $"{baseName} ({n})";
            if (!taken.Contains(candidate)) return candidate;
            n++;
        }
    }

    /// <summary>
    /// Filter a selection to only include root-level objects objects whose
    /// ancestors are NOT also in the selection. This prevents duplicating a child
    /// that's already included inside a selected parent's hierarchy.
    /// </summary>
    private static List<GameObject> FilterToRoots(IEnumerable<GameObject> gameObjects)
    {
        var set = new HashSet<GameObject>(gameObjects);
        var roots = new List<GameObject>();

        foreach (var go in set)
        {
            bool ancestorSelected = false;
            var parent = go.Parent;
            while (parent != null)
            {
                if (set.Contains(parent))
                {
                    ancestorSelected = true;
                    break;
                }
                parent = parent.Parent;
            }
            if (!ancestorSelected)
                roots.Add(go);
        }

        return roots;
    }
}

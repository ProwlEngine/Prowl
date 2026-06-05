// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Echo;
using Prowl.Editor.Core;
using Prowl.Editor.GUI.SceneView;
using Prowl.Editor.Utils;
using Prowl.Runtime;
using Prowl.Runtime.Resources;

namespace Prowl.Editor.GUI;

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

                go.Name = UniqueNames.ForGameObjectSibling(go.Name, parent, scene);
                scene.Add(go);
                if (parent != null)
                    // Keep the deserialized local transform (see Duplicate); don't preserve world pos.
                    go.SetParent(parent, worldPositionStays: false);
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

                clone.Name = UniqueNames.ForGameObjectSibling(source.Name, source.Parent, scene);
                scene.Add(clone);
                if (source.Parent != null)
                    // Keep the clone's copied local transform; don't preserve world position (the
                    // clone is briefly a root, so that path would reinterpret its local pos as world).
                    clone.SetParent(source.Parent, worldPositionStays: false);
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
    /// Filter a selection to only include root-level objects whose ancestors are
    /// NOT also in the selection. This prevents duplicating a child that's already
    /// included inside a selected parent's hierarchy.
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

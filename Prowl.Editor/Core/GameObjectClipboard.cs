// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Echo;
using Prowl.Echo.Cloning;
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
        List<GameObject> roots = FilterToRoots(gameObjects);
        if (roots.Count == 0) return;

        // One context for the whole selection. A reference from one copied object to another stays
        // inside the data, and a reference to anything else is linked by id so pasting binds it back
        // to that object rather than to a copy of it that belongs to no scene.
        var context = new SerializationContext { ExternalReferences = SceneReferenceResolver.ForTrees(roots) };

        var root = EchoObject.NewList();
        foreach (GameObject go in roots)
        {
            var echo = Serializer.Serialize(typeof(object), go, context);
            if (echo != null)
                root.ListAdd(echo);
        }

        if (root.Count == 0) return;

        Input.Clipboard = ClipboardHeader + root.WriteToString();
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

            // Shared context so a reference between two pasted objects resolves to the pasted one,
            // and a resolver so a reference out of the selection binds to the live scene object.
            var context = new SerializationContext { ExternalReferences = new SceneReferenceResolver() };

            foreach (var item in root.List)
            {
                var go = Serializer.Deserialize<GameObject>(item, context);
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
                EditorSceneManager.MarkDirty();
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

        List<GameObject> roots = FilterToRoots(gameObjects);
        if (roots.Count == 0) return results;

        List<GameObject> clones;
        try
        {
            // Every root in one operation, so a reference from one selected object to another lands on
            // that object's copy rather than staying pointed at the original.
            clones = Cloner.CloneAll(roots);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"Failed to duplicate: {ex.Message}");
            return results;
        }

        for (int i = 0; i < roots.Count; i++)
        {
            GameObject source = roots[i];
            GameObject clone = clones[i];

            clone.Name = UniqueNames.ForGameObjectSibling(source.Name, source.Parent, scene);
            scene.Add(clone);
            if (source.Parent != null)
                // Keep the clone's copied local transform; don't preserve world position (the
                // clone is briefly a root, so that path would reinterpret its local pos as world).
                clone.SetParent(source.Parent, worldPositionStays: false);
            results.Add(clone);
        }

        if (results.Count > 0)
        {
            Selection.Clear();
            foreach (var go in results)
                Selection.AddToSelection(go);
            EditorSceneManager.MarkDirty();
        }

        return results;
    }

    /// <summary>
    /// Filter a selection to only include root-level objects whose ancestors are
    /// NOT also in the selection. This prevents duplicating a child that's already
    /// included inside a selected parent's hierarchy.
    /// </summary>
    /// <summary>
    /// Drop any GameObject that already has an ancestor in the set, so an operation applied to a
    /// selection runs once per subtree rather than once per selected object.
    /// </summary>
    public static List<GameObject> FilterToRoots(IEnumerable<GameObject> gameObjects)
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

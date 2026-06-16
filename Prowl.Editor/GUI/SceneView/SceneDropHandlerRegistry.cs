// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Reflection;

using Prowl.Editor.GUI.Panels;
using Prowl.Editor.Theming;
using Prowl.Runtime;
using Prowl.Runtime.Resources;
using Prowl.Vector;

namespace Prowl.Editor.GUI.SceneView;

/// <summary>
/// Attribute to register a scene view drop handler for a specific asset type.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public class SceneDropHandlerAttribute : Attribute
{
    public Type TargetType { get; }
    public int Order { get; set; }
    public SceneDropHandlerAttribute(Type targetType) => TargetType = targetType;
}

/// <summary>
/// Context provided to drop handlers when an asset is dropped in the scene view.
/// </summary>
public struct SceneDropContext
{
    public Scene Scene;
    public EditorCamera Camera;
    public Float2 MouseLocal;
    public Float2 PanelSize;
}

/// <summary>
/// Interface for handling asset drops in the scene view.
/// </summary>
public interface ISceneDropHandler
{
    /// <summary>Hint text shown while dragging over the scene view.</summary>
    string DropHint { get; }

    /// <summary>Perform the drop action.</summary>
    void Handle(AssetDragPayload payload, SceneDropContext context);
}

/// <summary>
/// Discovers <see cref="ISceneDropHandler"/> implementations and dispatches
/// scene view drag-and-drop operations.
/// </summary>
public static class SceneDropHandlerRegistry
{
    private struct HandlerEntry
    {
        public Type AssetType;
        public int Order;
        public ISceneDropHandler Handler;
    }

    private static readonly List<HandlerEntry> _handlers = [];
    private static bool _initialized;

    public static void Reinitialize() { _initialized = false; Initialize(); }

    /// <summary>Drop cached handlers (which may bind user code) so the script AssemblyLoadContext can be collected.</summary>
    public static void ClearCache()
    {
        _initialized = false;
        _handlers.Clear();
    }

    public static void Initialize()
    {
        if (_initialized) return;
        _initialized = true;

        _handlers.Clear();

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type[] types;
            try { types = assembly.GetTypes(); }
            catch { continue; }

            foreach (var type in types)
            {
                if (type.IsAbstract || !typeof(ISceneDropHandler).IsAssignableFrom(type)) continue;
                var attr = type.GetCustomAttribute<SceneDropHandlerAttribute>();
                if (attr == null) continue;

                var instance = (ISceneDropHandler)Activator.CreateInstance(type)!;
                _handlers.Add(new HandlerEntry
                {
                    AssetType = attr.TargetType,
                    Order = attr.Order,
                    Handler = instance,
                });
            }
        }

        _handlers.Sort((a, b) => a.Order.CompareTo(b.Order));
        Debug.Log($"SceneDropHandlerRegistry: {_handlers.Count} handlers registered.");
    }

    /// <summary>
    /// Find a handler that matches the given asset type.
    /// Returns null if no handler matches.
    /// </summary>
    public static ISceneDropHandler? FindHandler(Type? assetType)
    {
        if (assetType == null) return null;

        foreach (var entry in _handlers)
        {
            if (entry.AssetType.IsAssignableFrom(assetType))
                return entry.Handler;
        }
        return null;
    }
}

// ================================================================
//  Built-in scene drop handlers
// ================================================================

[SceneDropHandler(typeof(Scene), Order = 0)]
internal class SceneAssetDropHandler : ISceneDropHandler
{
    public string DropHint => $"{EditorIcons.ArrowDown}  Drop to open scene";

    public void Handle(AssetDragPayload payload, SceneDropContext context)
    {
        var entry = EditorAssetDatabase.Instance?.GetEntry(payload.AssetGuid);
        if (entry != null)
            EditorSceneManager.OpenScene(entry.Path);
    }
}

[SceneDropHandler(typeof(Material), Order = 10)]
internal class MaterialDropHandler : ISceneDropHandler
{
    public string DropHint => $"{EditorIcons.ArrowDown}  Drop on object to assign material";

    public void Handle(AssetDragPayload payload, SceneDropContext context)
    {
        var hitGO = SceneViewPanel.PickObjectAt(context.Scene, context.Camera, context.MouseLocal, context.PanelSize);
        if (hitGO == null) return;

        var mat = Runtime.AssetDatabase.Get(payload.AssetGuid) as Material;
        if (mat == null) return;

        var meshRenderer = hitGO.GetComponent<MeshRenderer>();
        if (meshRenderer != null)
        {
            meshRenderer.Material = mat;
            EditorSceneManager.IsDirty = true;
        }
        // TODO: assign to model renderer materials
    }
}

[SceneDropHandler(typeof(Model), Order = 20)]
internal class ModelDropHandler : ISceneDropHandler
{
    public string DropHint => $"{EditorIcons.ArrowDown}  Drop to spawn in scene";

    public void Handle(AssetDragPayload payload, SceneDropContext context)
    {
        Float3 dropPos = SceneViewPanel.GetDropPosition(context.Scene, context.Camera, context.MouseLocal, context.PanelSize);
        HierarchyPanel.SpawnAssetInScene(payload, null, dropPos);
    }
}

[SceneDropHandler(typeof(Mesh), Order = 21)]
internal class MeshDropHandler : ISceneDropHandler
{
    public string DropHint => $"{EditorIcons.ArrowDown}  Drop to spawn in scene";

    public void Handle(AssetDragPayload payload, SceneDropContext context)
    {
        Float3 dropPos = SceneViewPanel.GetDropPosition(context.Scene, context.Camera, context.MouseLocal, context.PanelSize);
        HierarchyPanel.SpawnAssetInScene(payload, null, dropPos);
    }
}

[SceneDropHandler(typeof(PrefabAsset), Order = 22)]
internal class PrefabDropHandler : ISceneDropHandler
{
    public string DropHint => $"{EditorIcons.ArrowDown}  Drop to spawn in scene";

    public void Handle(AssetDragPayload payload, SceneDropContext context)
    {
        Float3 dropPos = SceneViewPanel.GetDropPosition(context.Scene, context.Camera, context.MouseLocal, context.PanelSize);
        HierarchyPanel.SpawnAssetInScene(payload, null, dropPos);
    }
}

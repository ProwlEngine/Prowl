// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.
//
// Editor-specific drag payloads. The drag system itself is OrigamiUI.DragDrop.

using System;

using Prowl.Editor.Theming;
using Prowl.OrigamiUI;
using Prowl.Runtime;

namespace Prowl.Editor.Core;

/// <summary>Payload for dragging assets from the Project panel.</summary>
public class AssetDragPayload : DragPayload
{
    public Guid AssetGuid { get; }
    public string AssetName { get; }
    public Type? AssetType { get; }
    public Guid[] AssetGuids { get; }
    public string[] AssetPaths { get; }
    public bool IsMulti => AssetGuids.Length > 1;

    public override string DisplayName => IsMulti ? $"{AssetGuids.Length} assets" : AssetName;
    public override string Icon => EditorIcons.Cube;

    public AssetDragPayload(Guid guid, string name, Type? type)
        : this(guid, name, type, [guid], [name]) { }

    public AssetDragPayload(Guid guid, string name, Type? type, Guid[] allGuids, string[] allPaths)
    {
        AssetGuid = guid;
        AssetName = name;
        AssetType = type;
        AssetGuids = allGuids;
        AssetPaths = allPaths;
    }
}

/// <summary>Payload for dragging GameObjects in the hierarchy.</summary>
public class GameObjectDragPayload : DragPayload
{
    public GameObject[] GameObjects { get; }

    public override string DisplayName => GameObjects.Length == 1
        ? GameObjects[0].Name
        : $"{GameObjects.Length} objects";
    public override string Icon => EditorIcons.Cube;

    public GameObjectDragPayload(GameObject go) : this([go]) { }
    public GameObjectDragPayload(GameObject[] gos) => GameObjects = gos;
}

/// <summary>Payload for dragging a component from the inspector.</summary>
public class ComponentDragPayload : DragPayload
{
    public GameObject GameObject { get; }
    public MonoBehaviour Component { get; }

    public override string DisplayName => Component.GetType().Name;
    public override string Icon => EditorIcons.PuzzlePiece;

    public ComponentDragPayload(GameObject go, MonoBehaviour comp)
    {
        GameObject = go;
        Component = comp;
    }
}

// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.
//
// Editor-specific drag payloads. The drag system itself is OrigamiUI.DragDrop.

using System;

using Prowl.Editor.Theming;
using Prowl.OrigamiUI;
using Prowl.Runtime;

namespace Prowl.Editor.GUI;

public abstract class EditorDragPayload : DragPayload
{
    public override string DisplayName { get; }
    public override string Icon { get; }

    protected EditorDragPayload(string displayName, string icon)
    {
        DisplayName = displayName;
        Icon = icon;
    }
}

/// <summary>Payload for dragging assets from the Project panel.</summary>
public class AssetDragPayload : EditorDragPayload
{
    public Guid AssetGuid { get; }
    public string AssetName { get; }
    public Type? AssetType { get; }
    public Guid[] AssetGuids { get; }
    public string[] AssetPaths { get; }
    public bool IsMulti => AssetGuids.Length > 1;

    public AssetDragPayload(Guid guid, string name, Type? type)
        : this(guid, name, type, [guid], [name]) { }

    public AssetDragPayload(Guid guid, string name, Type? type, Guid[] allGuids, string[] allPaths)
        : base(allGuids.Length > 1 ? $"{allGuids.Length} assets" : name, EditorIcons.Cube)
    {
        AssetGuid = guid;
        AssetName = name;
        AssetType = type;
        AssetGuids = allGuids;
        AssetPaths = allPaths;
    }
}

/// <summary>Payload for dragging GameObjects in the hierarchy.</summary>
public class GameObjectDragPayload : EditorDragPayload
{
    public GameObject[] GameObjects { get; }

    public GameObjectDragPayload(GameObject go) : this([go]) { }
    public GameObjectDragPayload(GameObject[] gos)
        : base(gos.Length == 1 ? gos[0].Name : $"{gos.Length} objects", EditorIcons.Cube)
        => GameObjects = gos;
}

/// <summary>Payload for dragging a component from the inspector.</summary>
public class ComponentDragPayload : EditorDragPayload
{
    public GameObject GameObject { get; }
    public MonoBehaviour Component { get; }

    public ComponentDragPayload(GameObject go, MonoBehaviour comp)
        : base(comp.GetType().Name, EditorIcons.PuzzlePiece)
    {
        GameObject = go;
        Component = comp;
    }
}

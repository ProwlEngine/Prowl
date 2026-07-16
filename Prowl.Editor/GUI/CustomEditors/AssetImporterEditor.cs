using System;

using Prowl.PaperUI;
using Prowl.Runtime;

namespace Prowl.Editor.Inspector;

/// <summary>
/// Attribute to register a custom asset editor for a specific EngineObject type.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public class CustomAssetEditorAttribute : Attribute
{
    public Type TargetType { get; }
    public CustomAssetEditorAttribute(Type targetType) => TargetType = targetType;
}

/// <summary>
/// Base class for custom asset editors shown in the inspector when an asset is selected.
/// </summary>
public abstract class AssetImporterEditor
{
    /// <summary>Draw the asset editor UI.</summary>
    public abstract void OnGUI(Paper paper, string id, AssetEntry entry, EngineObject? asset);
}


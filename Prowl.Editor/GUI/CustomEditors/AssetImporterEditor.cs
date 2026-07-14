using System;
using System.Reflection;

using Prowl.Editor.Utils;
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

/// <summary>
/// Registry for AssetImporterEditor subclasses.
/// </summary>
public static class AssetImporterEditorRegistry
{
    private static readonly EditorTypeRegistry<AssetImporterEditor> _reg = new(
        "AssetImporterEditorRegistry",
        t => t.GetCustomAttribute<CustomAssetEditorAttribute>()?.TargetType);

    [OnAssemblyLoad]
    public static void Reinitialize() => _reg.Reinitialize();

    [OnAssemblyUnload]
    public static void ClearCache() => _reg.ClearCache();

    public static void Initialize() => _reg.Initialize();

    public static AssetImporterEditor? GetEditor(Type assetType) => _reg.GetEditor(assetType);
}

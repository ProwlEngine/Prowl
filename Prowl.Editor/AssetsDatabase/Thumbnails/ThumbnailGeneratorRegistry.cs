// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Reflection;

using Prowl.Runtime;
using Prowl.Runtime.Resources;

namespace Prowl.Editor.Thumbnails;

/// <summary>
/// Attribute to register a custom thumbnail generator for a specific asset type.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public class CustomThumbnailGeneratorAttribute : Attribute
{
    public Type TargetType { get; }
    public CustomThumbnailGeneratorAttribute(Type targetType) => TargetType = targetType;
}

/// <summary>
/// Interface for generating thumbnail pixel data for an asset.
/// </summary>
public interface IThumbnailGenerator
{
    /// <summary>
    /// Generate thumbnail pixels (RGBA, top-down) for the given asset.
    /// Return null if generation is not possible.
    /// </summary>
    byte[]? Generate(EngineObject asset, string? sourceFilePath);
}

/// <summary>
/// Discovers and manages <see cref="IThumbnailGenerator"/> implementations
/// decorated with <see cref="CustomThumbnailGeneratorAttribute"/>.
/// </summary>
public static class ThumbnailGeneratorRegistry
{
    private static readonly Dictionary<Type, IThumbnailGenerator> _generators = new();
    private static bool _initialized;

    [Runtime.OnAssemblyLoad]
    public static void Reinitialize() { _initialized = false; Initialize(); }

    /// <summary>Drop cached generators (keyed by user <see cref="Type"/>) so the script AssemblyLoadContext can be collected.</summary>
    [Runtime.OnAssemblyUnload]
    public static void ClearCache()
    {
        _initialized = false;
        _generators.Clear();
    }

    public static void Initialize()
    {
        if (_initialized) return;
        _initialized = true;

        _generators.Clear();

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type[] types;
            try { types = assembly.GetTypes(); }
            catch { continue; }

            foreach (var type in types)
            {
                if (type.IsAbstract || !typeof(IThumbnailGenerator).IsAssignableFrom(type)) continue;
                var attr = type.GetCustomAttribute<CustomThumbnailGeneratorAttribute>();
                if (attr == null) continue;

                var instance = (IThumbnailGenerator)Activator.CreateInstance(type)!;
                _generators[attr.TargetType] = instance;
            }
        }

        Debug.Log($"ThumbnailGeneratorRegistry: {_generators.Count} generators registered.");
    }

    /// <summary>
    /// Try to generate a thumbnail for the given asset.
    /// Checks exact type first, then walks base types.
    /// </summary>
    public static bool TryGenerate(EngineObject asset, string? sourceFilePath, out byte[]? pixels)
    {
        pixels = null;

        // Exact type match first
        var assetType = asset.GetType();
        if (_generators.TryGetValue(assetType, out var generator))
        {
            pixels = generator.Generate(asset, sourceFilePath);
            return true;
        }

        // Walk base types
        var baseType = assetType.BaseType;
        while (baseType != null && baseType != typeof(object))
        {
            if (_generators.TryGetValue(baseType, out generator))
            {
                pixels = generator.Generate(asset, sourceFilePath);
                return true;
            }
            baseType = baseType.BaseType;
        }

        return false;
    }
}

// ================================================================
//  Built-in thumbnail generators
// ================================================================

[CustomThumbnailGenerator(typeof(Texture2D))]
internal class Texture2DThumbnailGenerator : IThumbnailGenerator
{
    public byte[]? Generate(EngineObject asset, string? sourceFilePath)
        => ThumbnailGenerator.GenerateForTextureFile(sourceFilePath);
}

[CustomThumbnailGenerator(typeof(Sprite))]
internal class SpriteThumbnailGenerator : IThumbnailGenerator
{
    public byte[]? Generate(EngineObject asset, string? sourceFilePath)
        => ThumbnailGenerator.GenerateForSprite((Sprite)asset);
}

[CustomThumbnailGenerator(typeof(Model))]
internal class ModelThumbnailGenerator : IThumbnailGenerator
{
    public byte[]? Generate(EngineObject asset, string? sourceFilePath)
        => ThumbnailGenerator.GenerateFor3D(p => p.SetupForModel((Model)asset));
}

[CustomThumbnailGenerator(typeof(Material))]
internal class MaterialThumbnailGenerator : IThumbnailGenerator
{
    public byte[]? Generate(EngineObject asset, string? sourceFilePath)
        => ThumbnailGenerator.GenerateFor3D(p => p.SetupForMaterial((Material)asset));
}

[CustomThumbnailGenerator(typeof(Mesh))]
internal class MeshThumbnailGenerator : IThumbnailGenerator
{
    public byte[]? Generate(EngineObject asset, string? sourceFilePath)
        => ThumbnailGenerator.GenerateFor3D(p => p.SetupForMesh((Mesh)asset));
}

[CustomThumbnailGenerator(typeof(PrefabAsset))]
internal class PrefabAssetThumbnailGenerator : IThumbnailGenerator
{
    public byte[]? Generate(EngineObject asset, string? sourceFilePath)
        => ThumbnailGenerator.GenerateFor3D(p => p.SetupForPrefab((PrefabAsset)asset));
}

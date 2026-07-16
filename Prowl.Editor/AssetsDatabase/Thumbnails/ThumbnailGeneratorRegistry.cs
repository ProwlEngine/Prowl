using System;

using Prowl.Runtime;
using Prowl.Runtime.Resources;

namespace Prowl.Editor.Thumbnails;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public class CustomThumbnailGeneratorAttribute : Attribute
{
    public Type TargetType { get; }
    public CustomThumbnailGeneratorAttribute(Type targetType) => TargetType = targetType;
}

public interface IThumbnailGenerator
{
    byte[]? Generate(EngineObject asset, string? sourceFilePath);
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

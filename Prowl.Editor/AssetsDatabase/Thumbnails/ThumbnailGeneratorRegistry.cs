using System;

using Prowl.Runtime;
using Prowl.Runtime.Resources;

namespace Prowl.Editor.Thumbnails;

/// <summary> Specifies the type of asset for which the decorated class generates thumbnails. </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public class CustomThumbnailGeneratorAttribute : Attribute
{
    /// <summary> Gets the type of asset this thumbnail generator handles. </summary>
    public Type TargetType { get; }
    /// <summary> Initializes a new instance of CustomThumbnailGeneratorAttribute for the specified target type. </summary>
    public CustomThumbnailGeneratorAttribute(Type targetType) => TargetType = targetType;
}

/// <summary> Defines a method for generating thumbnail images for engine assets. </summary>
public interface IThumbnailGenerator
{
    /// <summary> Generates a thumbnail for the specified asset. Returns the raw image data as a byte array, or null if generation fails. </summary>
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

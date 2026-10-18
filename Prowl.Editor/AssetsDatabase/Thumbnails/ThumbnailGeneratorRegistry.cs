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
    /// <summary> Generates a thumbnail for the specified asset and hands its RGBA pixels to <paramref name="done"/>, or null if generation fails. A generator that renders calls it frames later, once the GPU has drawn it. </summary>
    void Generate(EngineObject asset, string? sourceFilePath, Action<byte[]?> done);
}

// ================================================================
//  Built-in thumbnail generators
// ================================================================

[CustomThumbnailGenerator(typeof(Texture2D))]
internal class Texture2DThumbnailGenerator : IThumbnailGenerator
{
    public void Generate(EngineObject asset, string? sourceFilePath, Action<byte[]?> done)
        => done(ThumbnailGenerator.GenerateForTextureFile(sourceFilePath));
}

[CustomThumbnailGenerator(typeof(Sprite))]
internal class SpriteThumbnailGenerator : IThumbnailGenerator
{
    public void Generate(EngineObject asset, string? sourceFilePath, Action<byte[]?> done)
        => done(ThumbnailGenerator.GenerateForSprite((Sprite)asset));
}

[CustomThumbnailGenerator(typeof(Material))]
internal class MaterialThumbnailGenerator : IThumbnailGenerator
{
    public void Generate(EngineObject asset, string? sourceFilePath, Action<byte[]?> done)
        => ThumbnailGenerator.GenerateFor3D(p => p.SetupForMaterial((Material)asset), done);
}

[CustomThumbnailGenerator(typeof(Mesh))]
internal class MeshThumbnailGenerator : IThumbnailGenerator
{
    public void Generate(EngineObject asset, string? sourceFilePath, Action<byte[]?> done)
        => ThumbnailGenerator.GenerateFor3D(p => p.SetupForMesh((Mesh)asset), done);
}

[CustomThumbnailGenerator(typeof(PrefabAsset))]
internal class PrefabAssetThumbnailGenerator : IThumbnailGenerator
{
    public void Generate(EngineObject asset, string? sourceFilePath, Action<byte[]?> done)
        => ThumbnailGenerator.GenerateFor3D(p => p.SetupForPrefab((PrefabAsset)asset), done);
}

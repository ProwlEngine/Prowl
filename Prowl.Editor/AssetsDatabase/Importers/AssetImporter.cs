using System;

using Prowl.Echo;

namespace Prowl.Editor.Importers;

/// <summary>
/// Attribute to register an importer for specific file extensions.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public class ImporterForAttribute : Attribute
{
    public string[] Extensions { get; }
    public ImporterForAttribute(params string[] extensions) => Extensions = extensions;
}

/// <summary>
/// Base class for all asset importers. Each importer handles specific file types
/// and converts raw files into runtime EngineObjects via the ImportContext.
/// </summary>
public abstract class AssetImporter
{
    /// <summary>Version number for cache invalidation. Bump when import logic changes.</summary>
    public abstract int Version { get; }

    /// <summary>
    /// Import a raw asset file. Push results into ctx via SetMainAsset/AddSubAsset.
    /// </summary>
    /// <returns>True if import succeeded.</returns>
    public abstract bool Import(ImportContext ctx);

    /// <summary>
    /// Returns default settings for this importer type. Override to provide defaults.
    /// </summary>
    public virtual EchoObject? DefaultSettings() => null;
}

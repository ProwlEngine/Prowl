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
/// and converts raw files into runtime EngineObjects.
/// </summary>
public abstract class AssetImporter
{
    /// <summary>Version number for cache invalidation. Bump when import logic changes.</summary>
    public abstract int Version { get; }

    /// <summary>
    /// Import a raw asset file and produce runtime objects.
    /// </summary>
    /// <param name="absolutePath">Full path to the source file.</param>
    /// <param name="settings">Importer-specific settings from the .meta file (may be null).</param>
    /// <returns>Import result with main asset and optional sub-assets.</returns>
    public abstract ImportResult Import(string absolutePath, EchoObject? settings);

    /// <summary>
    /// Returns default settings for this importer type. Override to provide defaults.
    /// </summary>
    public virtual EchoObject? DefaultSettings() => null;
}

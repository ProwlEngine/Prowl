// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Echo;
using Prowl.Editor.Projects;
using Prowl.Runtime;
using Prowl.Runtime.Resources;

namespace Prowl.Editor.Test;

/// <summary>
/// Base class for headless editor tests. Each test gets a fresh throwaway project on disk with a live
/// <see cref="EditorAssetDatabase"/>, and everything is torn down (including the temp directory) when
/// the test finishes.
///
/// This deliberately avoids the editor GUI/graphics layer: never instantiate EditorApplication and
/// never import graphics-bound assets (textures/shaders/models), since <c>Graphics.GL</c> is unguarded.
/// Tests in this assembly run without parallelization (see TestAssemblyConfig) because the editor uses
/// global static state.
/// </summary>
public abstract class EditorTestHarness : IDisposable
{
    private readonly string _root;
    private readonly bool _prevIsEditor;
    private readonly bool _prevIsPlaying;

    /// <summary>The throwaway project for this test.</summary>
    protected Project Project { get; }

    /// <summary>The live asset database for the project.</summary>
    protected EditorAssetDatabase Assets { get; }

    protected EditorTestHarness()
    {
        _prevIsEditor = Application.IsEditor;
        _prevIsPlaying = Application.IsPlaying;
        Application.IsEditor = true;
        Application.IsPlaying = false;

        _root = Path.Combine(Path.GetTempPath(), "ProwlEditorTest", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);

        Project = Project.Create(_root, "TestProject");
        Project.SetActive();

        Assets = new EditorAssetDatabase(Project);
        Assets.Initialize(); // registers itself as AssetDatabase.Current
    }

    /// <summary>Absolute path to a path relative to the project's Assets folder.</summary>
    protected string AssetAbsolutePath(string relativePath) => Path.Combine(Project.AssetsPath, relativePath);

    /// <summary>
    /// Writes a GameObject hierarchy to disk as a .prefab asset and imports it. Returns the asset GUID.
    /// The file is serialized as a GameObject; the PrefabImporter wraps it into a PrefabAsset.
    /// </summary>
    protected Guid CreatePrefabAsset(GameObject source, string relativePath = "Prefab.prefab")
    {
        source.ClearPrefabDataRecursive();

        Guid savedId = source.AssetID;
        source.AssetID = Guid.Empty;
        EchoObject echo = Serializer.Serialize(typeof(object), source);
        source.AssetID = savedId;

        string abs = AssetAbsolutePath(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(abs)!);
        File.WriteAllText(abs, echo.WriteToString());

        return Assets.ImportFile(relativePath);
    }

    /// <summary>Resolve a prefab asset by GUID.</summary>
    protected PrefabAsset? GetPrefab(Guid guid) => AssetDatabase.Get(guid) as PrefabAsset;

    /// <summary>Persist a Scene as a .scene asset and return its GUID.</summary>
    protected Guid CreateSceneAsset(Scene scene, string relativePath = "Scene.scene")
    {
        Assets.CreateAsset(scene, relativePath);
        return scene.AssetID;
    }

    public virtual void Dispose()
    {
        try { if (Scene.Current != null) Scene.Unload(); } catch { }

        Assets.Dispose(); // stops the FileSystemWatcher
        AssetDatabase.Current = null;

        Application.IsEditor = _prevIsEditor;
        Application.IsPlaying = _prevIsPlaying;

        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch { }

        GC.SuppressFinalize(this);
    }
}

// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.IO;
using System.Reflection;

using Prowl.PaperUI;
using Prowl.Runtime;
using Prowl.Runtime.Resources;

namespace Prowl.Player;

/// <summary>The game loop of a built desktop player, configured entirely from its manifest.</summary>
public sealed class DesktopPlayer : Game, IDisposable
{
    private readonly PlayerManifest _manifest;
    private PlayerAssetBackend? _assets;

    public DesktopPlayer(PlayerManifest manifest) => _manifest = manifest;

    public override void Initialize()
    {
        Application.IsPlaying = true;
        Application.IsEditor = false;
        Application.DataPath = AppContext.BaseDirectory;

        // Safe here rather than in Install: by now the engine assembly is loaded, so naming it costs
        // nothing. Doing it earlier would force the load before the resolver could serve it.
        System.Runtime.InteropServices.NativeLibrary.SetDllImportResolver(
            typeof(Game).Assembly, NativeLibraryResolver.ResolveForImport);

        LoadGameAssemblies();

        // Built-in assets first: anything loaded after this may reference them.
        BuiltInAssets.Initialize();

        var backend = new PlayerAssetBackend(_manifest.Packaging, "Content");
        AssetDatabase.Backend = backend;
        _assets = backend;

        string settingsDir = Path.Combine(Application.DataPath, "Content", "Settings");

        // Before anything loads, so the scene wakes up with the project's settings and XR already running. Physics
        // settings reach the scene as it loads.
        PlayerSettingsLoader.Apply(settingsDir);

        // Blocks until the scene and everything it uses is loaded, so the first frame shows all of it.
        if (AssetDatabase.Get<SceneAsset>(_manifest.DefaultSceneGuid) is { IsMissing: false } scene)
            Scene.Load(scene);
        else
            Debug.LogError($"[Player] Failed to load the default scene {_manifest.DefaultSceneGuid}.");
    }

    private void LoadGameAssemblies()
    {
        foreach (string name in _manifest.AssemblyLoadOrder)
        {
            string path = Path.Combine(Application.DataPath, name + ".dll");
            if (File.Exists(path))
                Assembly.LoadFrom(path);
            else
                Debug.LogWarning($"[Player] Game assembly not found: {path}");
        }
    }

    public override void OnUpdate(Scene? scene) => scene?.Update();

    public override void OnRender(Scene? scene) => scene?.Render();

    public override void OnGui(Scene? scene, Paper paper) => scene?.OnGui(paper);

    /// <summary>
    /// Closes the pak archives the backend holds open, once the game loop returns. The backend goes with
    /// it, so a late asset lookup during shutdown finds nothing rather than reading through a disposed archive.
    /// </summary>
    public void Dispose()
    {
        if (ReferenceEquals(AssetDatabase.Backend, _assets))
            AssetDatabase.Backend = null;

        _assets?.Dispose();
        _assets = null;
    }
}

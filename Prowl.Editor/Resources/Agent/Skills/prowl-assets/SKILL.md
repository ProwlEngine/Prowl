---
name: prowl-assets
description: How Prowl assets work for scripts. Use when a script references textures, materials, meshes, prefabs, clips, scenes or other assets, chooses between a plain field and AssetRef, loads assets by path at runtime, creates assets in code, or when assets unload, go missing, or are not in a build.
---

# Prowl assets

## The model

- Every asset derives from `Asset` (an `EngineObject`). A database asset has a GUID (`AssetID`), a path (`AssetPath`) and a `Name` taken from its file name, or the part after `#` for a sub asset.
- **One object per GUID for the whole session.** Unloading frees the data but keeps the object, and loading, reimporting or reverting refills that same object. So a reference you hold never goes stale or gets swapped for another object.
- `asset.State` is an `AssetState` (a top level enum in `Prowl.Runtime`): `Loaded`, `Unloaded`, `Loading`, `Missing` or `Failed`. An unloaded or missing asset is still `IsValid()`. Check `IsLoaded` or `IsMissing` when the content matters.
- Reading an asset's data loads it on demand, blocking. A missing or failed asset reads as empty and logs one warning.
- A runtime asset made with `new Material(shader)`, `new Mesh()` or `Clone()` has no GUID. It is always loaded, never unloaded by the engine, and belongs to whoever made it. Dispose it when done.
- Never call `Dispose()` or `Destroy()` on a database asset, it throws. Use `AssetDatabase.Unload` if you really must.

## Referencing assets from a script

| | Plain field: `public Material Mat;` | `public AssetRef<Material> Mat;` |
| --- | --- | --- |
| Saved as | the GUID | the GUID |
| Keeps the asset loaded | yes, while the object holding it is reachable | no |
| Loaded with the scene | yes | no, call `Load()` or `LoadAsync()` |
| Shipped in builds | yes | yes |

Use a plain field for almost everything. Use `AssetRef<T>` only for things needed maybe later, such as a list of levels or an optional high resolution texture.

```csharp
using Prowl.Runtime;
using Prowl.Runtime.Resources;

public class Spawner : MonoBehaviour
{
    public PrefabAsset Enemy;                // held and loaded with the scene
    public AssetRef<SceneAsset> NextLevel;   // only a reference, loads when asked
    [NotHeld] public Texture2D DebugTex;     // referenced, but allowed to unload

    public override void Start()
    {
        var level = NextLevel.Get();         // never loads, may be unloaded
        var loaded = NextLevel.Load();       // blocks until loaded
    }
}
```

`AssetRef<T>`: `Get()` never loads, `Load()` blocks, `LoadAsync()` is async, `IsEmpty`, `AssetID`, and it converts implicitly from `T`. Wrapping a runtime asset (no GUID) gives an empty ref and a warning.

## What keeps an asset loaded

A background walk starts from every live scene, `DontDestroyOnLoad` objects, held assets, roots added with `AssetDatabase.AddRoot`, and statics marked `[HeldStatic]`. It follows instance fields (collections too). Anything it reaches stays loaded. An asset it does not reach for two walks and longer than the grace period (10 seconds by default) is unloaded.

The walk does **not** see static fields (unless `[HeldStatic]`), lambda captures, delegates or locals in async methods. An asset kept only there will unload. Put it in a field, mark the static `[HeldStatic]`, or call `AssetDatabase.Hold(asset, owner)`, which holds it until `owner` is garbage collected or you call `Release`.

If a field points at an asset that was unloaded, the next read reloads it (blocking) and logs a warning telling you to keep it in a field or hold it.

## AssetDatabase

| Call | Loads? |
| --- | --- |
| `AssetDatabase.Get(guid)`, `Get<T>(guid)` | never |
| `AssetDatabase.Load<T>(guid)` | blocks |
| `await AssetDatabase.LoadAsync<T>(guid)` | async |
| `AssetDatabase.FindResource<T>("Textures/Paint")` | blocks |
| `await AssetDatabase.FindResourceAsync<T>("Sfx/Hit")` | async |
| `AssetDatabase.FindAllResources<T>("Folder")` | blocks, and the results are not held afterwards |
| `AssetDatabase.LoadGroup(assets)`, `Preload(sceneAsset)` | returns an awaitable `AssetGroup` with `Progress` |
| `AssetDatabase.Explain(asset)` | reports state, size and what holds it, for debugging |

## Loading by path at runtime

A load path is the path below the nearest `Resources` folder, without the extension: `Assets/Enemies/Resources/Textures/Paint.png` loads as `Textures/Paint`. Sub assets add `#Name`.

Builds only ship the scenes in the build, everything they reference (plain fields and `AssetRef`), everything inside `Resources` folders, and what those reference. Assets under `Editor` folders are left out. **An asset you only load by path or GUID must be in a `Resources` folder**, or a player build reports it is not in the asset manifest.

## Sub assets, prefabs and scenes

- A model file imports as a `PrefabAsset` with sub assets: meshes, materials, the avatar and animation clips. Address one as `Models/Hero.fbx#Run`. `Library/prowl command asset info --path Models/Hero.fbx` lists them.
- Spawn a prefab with `GameObject.Instantiate(prefab)`, with overloads for position, rotation, parent and scene.
- Prefabs do not nest. A prefab instance inside another prefab is flattened into plain objects when the outer prefab is saved.
- The prefab a model imports as is read only. To change it, instantiate it, edit the instance, and save a new prefab with `prefab create`.
- `SceneAsset` is the saved data of a scene. `Scene` is the live scene. `Scene.Load(sceneAsset)` blocks while it loads everything the scene uses. `Scene.LoadAsync(sceneAsset)` returns an awaitable `SceneLoad` with `Progress` and swaps the scene in at the end of a frame.

## Creating and saving assets (editor code only)

`EditorAssetBackend.Instance.CreateAsset(asset, "Materials/New.mat")` writes the file, imports it and turns that same object into the database asset. `SaveAsset(asset)` writes changes back. Files like `.fbx` and `.png` are imported, not saved, so they can not be written this way. From the CLI use `asset create`, `set`, `material` and `importer` instead.

## Gotchas

- Never use `?.`, `??` or `??=` on an asset or any `EngineObject`. They are compile errors (PROWLEO001, PROWLEO002). Use `x.IsValid()` and `x.IsNotValid()`.
- Never block on a task in a script with `.Result`, `.Wait()` or `GetAwaiter().GetResult()` (PROWLTH001). Loads finish on the main thread, so the wait never returns. `await` instead, or call the blocking `Load()`.
- Blocking loads hitch the frame. Use the async calls or `LoadGroup` for large content.
- `Material.LoadDefault(...)` returns a new copy you own. The other built in `LoadDefault` calls return a shared asset.

---
name: prowl-cli
description: Drive the open Prowl editor from the terminal with the prowl CLI. Use for building or changing scenes, creating and editing assets, writing and compiling scripts, entering play mode to test, simulating input, taking screenshots, and building the game.
---

# Driving the Prowl editor

The editor must be open with this project. Run the CLI from the project root (from a subfolder use the path to it, such as `../../Library/prowl`):

- bash: `Library/prowl <verb> ...`
- cmd or PowerShell: `Library\prowl.cmd <verb> ...`

The verbs are `status`, `command`, `eval` and `eval_file`. **Every command in this guide runs as `Library/prowl command <name> ...`**, for example `Library/prowl command tree --depth 2`. `Library/prowl command` on its own lists every command with its arguments and defaults.

Arguments are positional in declared order, or `--name value`, or `--name=value`. A bool flag needs no value. Everything after `--` is positional. An argument that is just `-` is read from stdin, for example `command set /Player:Stats --values - < stats.json`. Add `--json` for raw JSON and `--timeout <seconds>` for long commands. Exit code 0 is success, 1 is an error with the reason on stderr, 130 is cancelled. Logs written while a command ran come back on stderr too.

## Refs

Every command addresses things the same way:

| Ref | Means |
| --- | --- |
| `/Player/Camera` | GameObject by hierarchy path. `/Enemy[1]` picks the second of two siblings named Enemy |
| `#3f2a...` | Scene object or asset by GUID. Results always include the id |
| `/Player:Rigidbody3D` | A component on that object. `:Type[1]` picks the second of that type |
| `@selection` | The selected GameObject, or all selected ones where a list is accepted |
| `Models/Gun.fbx` | Asset path, relative to the Assets folder |
| `Models/Gun.fbx#Fire` | Sub asset inside a file, such as an animation clip or mesh in a model |

Results describe objects as `{id, path, name}`, components as `{id, ref, type}`, assets as `{guid, path, type}`. Reuse the `path` or `id` from a result in the next command.

## Workflow

1. Orient: `status`, `tree --depth 2`, `logs --level warning`.
2. Look up the API before writing code: `api --search Rigidbody`, `api --type CharacterController`, `api --type Transform --member Rotate`, `--inherited` to include base classes. Prowl is not Unity, so do not guess signatures.
3. Write scripts as `.cs` files under `Assets` (or `script Scripts/Gun --template Component`), then `compile`. It waits for the hot reload and returns errors with file, line and column. Fix them and compile again until `ok` is true.
4. Build the scene with the commands below, then `scene --action save` (or `scene --action saveas --path Scenes/Level.scene`).
5. Check your work: `play --action start`, `input`, `play --action wait --seconds 2`, read state with `get`, `logs --level error`, `screenshot`, then `play --action stop`.

Scene, component, field, material, prefab and graph edits are one undo step each, and `undo` and `undo --redo` step through them outside play mode. File operations (`asset create`, `move`, `delete`, `import`, `importer`, `script`) and `eval` are not undoable, and `asset delete` is permanent.

## Scenes and objects

- `scene --action new` makes a scene with a Main Camera (HDR on, no effects), a Directional Light, and Floor, Cube and Cube (1), none of which have colliders. Delete what you do not need.
- `go create --name Gun --parent /Player --position '[0,1,0]' --components Rigidbody3D,BoxCollider`. Quote JSON arrays, unquoted brackets are a shell glob.
- `go create --prefab Models/Gun.fbx --parent /Player/Hand` instantiates a prefab or model
- `go rename|delete|duplicate|active|transform|parent --target <ref> ...`. Rotation is euler degrees. Positions are local unless `--world`. `go active --target /X --active false` deactivates.
- `component add /Player --type CharacterController --values '{"Height": 1.8}'`
- `component remove /Player:BoxCollider`, `component list /Player`
- `get /Player:CharacterController` shows every saved field and its current value. Always `get` first to learn the real field names.
- `set /Player:CharacterController Height 1.8`, or several at once with `--values '{"Height": 1.8, "Radius": 0.4}'`
- Field paths go inside: `Settings.Radius`, `Points[2]`, `Materials[0]`. Vectors and colors are arrays `[1, 2, 3]`.
- A string sets a reference: `set /Gun:Weapon Muzzle /Gun/Muzzle` for a scene object, `set /Cube:MeshRenderer "Materials[0]" Materials/Red.mat` for an asset.
- `menu --list --path GameObject` lists editor menu items. `menu --path "GameObject/3D Object/Cube"` runs one and returns the new selection. Without `--context`, create items parent the new object under the current selection, so run `select` with no refs first to create at the root. `GameObject/3D Object/*` primitives have only a MeshRenderer and no collider, so add one with `component add`. `--context /Canvas` runs it on an object, for example `menu --path "GameObject/UI/Button" --context /Canvas`.
- `select /Player --frame` selects and frames an object so the person watching can see it.

## Assets

- `asset find --type AnimationClip --query Run`, `asset info --path Models/Gun.fbx` (shows sub assets and dependencies)
- `asset create --path Materials/Metal --type Material` (`asset types` lists creatable types, plus `Folder`)
- After writing or copying files into Assets yourself: `asset import --path <file>` or `asset refresh`
- `asset move --path A.mat --to B.mat`, `asset delete --path A.mat --confirm`, `asset reimport --path ...`
- `material Materials/Metal.mat` lists shader properties. `--values '{"_MainColor": [1, 0, 0], "_Roughness": 0.3, "_MainTex": "Textures/Metal.png"}'` sets them. `--shader <ref>` swaps the shader.
- `importer Models/Gun.fbx` shows import settings and sub assets. `--values` merges keys (dots for nested keys) and reimports. Values keep their JSON type, so pass numbers as numbers: `--values '{"rigType": 2}'` (0 none, 1 generic, 2 humanoid). Animation import settings are in the prowl-animation skill.
- `prefab create /Gun --path Prefabs/Gun`, then `prefab apply|revert|unpack|overrides --target <instance>`. `overrides` lists changed values and additions (components and objects the instance has and the prefab does not). `apply` writes both into the prefab.

Animation graphs are built with the `graph` command. See the prowl-animation skill.

## Play mode and testing

- `play --action start|stop|pause|resume|step|wait|status`. Each returns the errors logged meanwhile.
- `input --action key --keys W,ShiftLeft --seconds 1` holds keys, `input --action mouse --button 0`, `input --action mouse --position '[640,360]'` clicks at that point in game view pixels, as measured on a game screenshot, which presses UI buttons there, `input --action look --delta '[5,0]' --seconds 0.5`. Key names are listed in the prowl-scenes skill.
- `screenshot --view scene` or `--view game` saves a PNG and returns its path. Open the view first if needed with `menu --path Window/General/Game`.
- `logs --since <nextSeq>` returns only what is new since the last call.

## Building

`build --out Builds/Windows [--run] --timeout 1800` builds with the selected pipeline and waits. Each build goes into a new folder under `--out` named after the project, numbered when one exists already, such as `Builds/Windows/Game (1)`, so the last build stays runnable. The result's `output` is the folder. Without a larger `--timeout` the CLI gives up after 120 seconds while the build keeps running. The same goes for a first `compile` that restores NuGet packages and for long `play --action wait` calls. The build scene list must not be empty (see the eval recipes below). `compile` defines `PROWL_EDITOR` for every script, so editor only code that is not guarded with `#if PROWL_EDITOR` only fails in a build.

## eval

`eval '<C#>'` runs code on the editor main thread and returns the value. Statements with `return`, leading `using` lines and `await` work. For multi line code pipe it in with `eval -`. Use it for anything no command covers. It can be turned off in Editor Preferences. Changes made with eval are not undoable and do not mark the scene dirty, so run `scene --action save` yourself afterwards. Recipes for things with no command:

```csharp
// prowl eval: add a tag and name layer 8
using Prowl.Editor.Projects.Settings;
var settings = EditorRegistries.GetSettings<TagsAndLayersSettings>();
if (!settings.Tags.Contains("Enemy")) settings.Tags.Add("Enemy");
settings.Layers[8] = "Weapon";
settings.Apply();
EditorRegistries.SaveSettings();
```

```csharp
// prowl eval: add the open scene to the build scene list
using Prowl.Editor.Projects.Settings;
var build = EditorRegistries.GetSettings<BuildSettings>();
var entry = EditorAssetBackend.Instance!.GetEntry(Prowl.Editor.GUI.SceneView.EditorSceneManager.CurrentScenePath!)!;
if (!build.Scenes.Exists(s => s.SceneGuid == entry.Guid))
    build.Scenes.Add(new SceneBuildEntry { Path = entry.Path, SceneGuid = entry.Guid });
EditorRegistries.SaveSettings();
```

```csharp
// prowl eval: bake the first nav mesh surface in the scene
var surface = Scene.Current.FindObjectsOfType<NavMeshSurface>()[0]!;
return Prowl.Editor.Navigation.NavMeshBakeService.Instance.Start(surface);
```

## Gotchas

- Scene changes made in play mode are lost when play stops, and the CLI warns once per play session. Asset edits (`material`, `set` on an asset, `graph`, `importer`, `asset`) are saved to disk at once and stay. Stop play mode before making lasting scene edits.
- Entering or leaving play mode, every `compile`, and opening or creating a scene clear the undo history.
- `scene --action open` refuses to drop unsaved changes unless `--force`. Save first.
- A new script type exists only after `compile` succeeds. Add the component after that.
- Shell quoting: in bash wrap JSON in single quotes. Windows PowerShell 5.1 strips double quotes from arguments, so escape each one as \" inside single quotes, for example `--values '{\"Height\": 1.8}'`, or pipe a here string into `eval -`. PowerShell 7.3 and later pass single quoted JSON unchanged.

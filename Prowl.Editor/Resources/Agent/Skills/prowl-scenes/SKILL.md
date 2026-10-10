---
name: prowl-scenes
description: How to write Prowl game scripts. Use when writing or reviewing a Component, working with GameObjects, components, Transform, scenes, prefabs, input, time, physics callbacks, async code with GameTask, or editor only script code. Covers where Prowl differs from Unity.
---

# Prowl scripting: scenes, GameObjects and components

Prowl looks like Unity but differs in many details. Check anything not listed here with `Library/prowl command api --type <Name>` before using it.

**Scripts derive from `Component`.** There is no `MonoBehaviour` class, and code written against one will not compile.

## A correct component

```csharp
using System;
using System.Threading;
using Prowl.Runtime;
using Prowl.Runtime.Resources;
using Prowl.Vector;

[RequireComponent(typeof(Rigidbody3D), typeof(BoxCollider))]
[AddComponentMenu("Game/Player Mover")]
public sealed class PlayerMover : Component
{
    public float Speed = 5f;                  // public fields are saved and shown in the inspector
    public PrefabAsset BulletPrefab;          // assigned in the inspector or with the CLI
    private Rigidbody3D _body;                // no constructor, no engine calls in field initializers

    public override void OnEnable() => _body = GetComponent<Rigidbody3D>();

    public override void Start() => Blink();

    public override void Update()
    {
        Float2 move = Input.GetWASD();
        _body.LinearVelocity = new Float3(move.X * Speed, _body.LinearVelocity.Y, move.Y * Speed);   // never move a dynamic body through its Transform

        if (Input.GetKeyDown(KeyCode.Space) && BulletPrefab.IsValid())
            GameObject.Instantiate(BulletPrefab, Transform.Position + Transform.Forward, Transform.Rotation);
    }

    public override void OnCollisionBegin(Collision collision)
    {
        Debug.Log($"{GameObject.Name} hit {collision.GameObject.Name}");
    }

    private async void Blink()
    {
        CancellationToken token = DestroyCancellationToken;
        try
        {
            while (true)
            {
                await GameTask.Delay(1f, token);
                Debug.Log("tick");
            }
        }
        catch (OperationCanceledException) { }
    }
}
```

## Lifecycle

Lifecycle methods are **virtual overrides**: write `public override void Update()`. A method with the right name but no `override` is never called.

- `OnAddedToScene()`, then `OnEnable()` when the object enters an active scene
- `Start()` once, before the first `FixedUpdate` or `Update`
- `FixedUpdate()` (fixed steps), `Update()`, `LateUpdate()` every frame
- `OnDisable()`, then `OnRemovedFromScene()` when it leaves
- `OnDispose()` when destroyed. This replaces Unity's `OnDestroy`. Override it as `protected override void OnDispose()` and call `base.OnDispose()`.
- **There is no `Awake` and no `OnDestroy`.** Use `OnEnable` or `Start`, and `OnDispose`.
- `OnValidate()` runs after a field is changed in the inspector or with the CLI `set` command.
- Physics: `OnCollisionBegin/Stay/End(Collision)`. `OnTriggerEnter/Stay/Exit(Rigidbody3D other)` and `OnCharacterEnter/Stay/Exit(CharacterController)` fire only on a GameObject that has a `TriggerVolume` component. Colliders have no trigger flag. See the prowl-physics skill.
- Drawing helpers: `DrawGizmos()`, `DrawGizmosSelected()`. Runtime UI: `OnGui(Paper paper)`.
- An exception in a callback is logged and the frame continues.

**No constructors.** Components are created before they are attached and before saved values are applied, so a constructor sees no GameObject and its values are overwritten. Analyzers flag constructors (PROWLCO001, PROWLCO002) and field initializers that call engine code (PROWLCO003, PROWLCO004). Do setup in `OnEnable` or `Start`.

Attributes: `[RequireComponent(typeof(A), typeof(B))]` (adds missing components automatically, and can be repeated), `[DisallowMultipleComponent]` (a second one of that type or a subclass is refused, `AddComponent` returns null), `[AddComponentMenu("Path")]`, `[ExecuteAlways]` (run callbacks outside play mode), `[ExecutionOrder(n)]`. Inspector attributes include `[HideInInspector]`, `[Range]`, `[Header]`, `[Space]`, `[Tooltip]`, `[ReadOnly]`, `[TextArea]`, `[ShowIf]`, `[Button]` on methods.

## GameObject

- `new GameObject("Name")` is not in any scene until you call `Scene.Current.Add(go)` or parent it under an object that is in a scene. Until then it never gets `OnEnable` or updates. `GameObject.Instantiate(...)` adds to the scene for you.
- Components: `AddComponent<T>()`, `GetComponent<T>()`, `TryGetComponent<T>(out var c)`, `GetComponents<T>()`, `GetComponentInParent<T>()`, `GetComponentInChildren<T>()` (both include the object itself by default), `RemoveComponent(c)`. `AddComponent` returns null if the component can not be created.
- Hierarchy: `SetParent(parent, worldPositionStays = true)`, pass `null` to detach. `Parent`, `Children`, `GetChildrenDeep()`.
- `Name` (inside a component, `Name` is the component's own name, use `GameObject.Name`), `Tag`, `CompareTag` (ignores case), `Layer`, `Enabled`, `EnabledInHierarchy`, `Identifier` (stable Guid).
- `Find(name)`, `FindGameObjectWithTag(tag)` are **instance** methods that search that object's scene.
- `go.Destroy()` removes it at the end of the frame. Until then `IsValid()` is still true and it keeps updating, so check `IsDestroyQueued` to treat it as gone, or call `Dispose()` to tear it down now. `Dispose()` tears it down immediately. There is no static `Destroy(obj)`.
- Spawning: `GameObject.Instantiate(prefab)`, `(prefab, position, rotation)`, `(prefab, parent)`, and the same overloads with a `GameObject` to copy. With a position the transform is set before `OnEnable` runs.

## Null checks

`==` on engine objects is plain reference equality, so `obj == null` is **false** for a destroyed object. Always use `obj.IsValid()` and `obj.IsNotValid()`. `?.`, `?[]`, `??` and `??=` on any `EngineObject` (GameObject, components, assets, scenes) are compile errors (PROWLEO001, PROWLEO002).

## Transform

`Transform` lives in namespace `Prowl.Vector` (with `Float2`, `Float3`, `Quaternion`). Reach it as `Transform` inside a component or `go.Transform`.

- `Position`, `LocalPosition`, `Rotation`, `LocalRotation`, `EulerAngles`, `LocalEulerAngles` (**degrees**), `LocalScale`, `LossyScale`, `Forward` (+Z), `Right`, `Up`
- `Translate(delta)` is in world space unless you pass a transform. `Rotate(Float3 eulerDegrees, relativeToSelf = true)`, `Rotate(Float3 axis, float degrees, relativeToSelf = true)`, `RotateAround(point, axis, degrees)`, `LookAt(target)`
- `TransformPoint`, `InverseTransformPoint`, `TransformDirection`, `SetPositionAndRotation`, `Find(path)`
- `Quaternion.AxisAngle` takes **radians**. The Transform methods take degrees.

## Scenes

`Scene` is in `Prowl.Runtime.Resources`. There is no SceneManager.

- `Scene.Current` is the live scene and is never null.
- `Scene.Current.Add(go)`, `Remove(go)`, `RootObjects`, `AllObjects`, `FindObjectsOfType<T>()` (instance methods)
- `Scene.Load(sceneAsset)` blocks, `await Scene.LoadAsync(sceneAsset)` loads in the background. Both swap at the end of the frame.
- `Scene.DontDestroyOnLoad(go)` keeps a root object across loads.
- `Scene.Current.Physics` has the raycasts (main thread only, see the prowl-physics skill). `Fog`, `Ambient` and `Skybox` hold environment settings.

## Time, input, logging

- `Time.DeltaTime`, `UnscaledDeltaTime`, `FixedDeltaTime`, `TimeScale`, `FrameCount` (long), `TimeSinceStartup`
- `Input.GetKey/GetKeyDown/GetKeyUp(KeyCode.W)`, `GetMouseButton(0)`, `MousePosition`, `MouseDelta`, `MouseWheelDelta`, `GetWASD()`, `LockCursor()`, `UnlockCursor()`. Key names: `A` to `Z`, `Number0` to `Number9`, `Space`, `Enter`, `Escape`, `ShiftLeft`, `ControlLeft`, `Up`, `F1`. Check others with `api --type KeyCode`.
- **`Input.MouseDelta.Y` is positive when the mouse moves down**, as in window coordinates. For mouse look, subtract it from the pitch.
- Input actions: an `InputActionMap` (an asset made from the Input Actions create menu, or built in code), `Input.RegisterActionMap(map)`, `Input.FindAction(name)`, then `ReadValue<T>()`, `IsPressed()`, `WasPressedThisFrame()`. **Actions start disabled and read their default until you call `map.Enable()`.**
- `Debug.Log`, `LogWarning`, `LogError`, `LogException`, and `LogOnce(id, message)` for things that repeat every frame. `Debug.DrawLine`, `DrawWireCube`, `DrawWireSphere` draw gizmos. If you also use `System.Diagnostics`, write `Prowl.Runtime.Debug`.

## Async and threads

- `await GameTask.NextFrame()`, `Frames(n)`, `Delay(seconds)` (game time, stops while paused), `DelayRealtime(seconds)`, `WaitUntil(() => cond)`, `WorkerThread()`, `MainThread()`. All but the last two take an optional `CancellationToken`.
- Await inside a component resumes on the main thread.
- When play stops or scripts reload, pending continuations are **dropped** without running their `finally`. Pass `DestroyCancellationToken` or `GameTask.SessionToken` and catch `OperationCanceledException` when you need to clean up.
- Scene objects may only be touched on the main thread. Most writes throw off the main thread, but reads do not, so a wrong read from a worker fails silently.
- Never block with `.Result`, `.Wait()` or `GetAwaiter().GetResult()` in a component (PROWLTH001). It deadlocks.

## Layers, tags and audio defaults

- `LayerMask` defaults to **everything**, not nothing. Build masks from layer names with `LayerMask.NameToLayer`.
- Setting `go.Layer` or `go.Tag` to a name that is not defined silently stores -1 instead of failing. Define tags and layers first (see the eval recipe in the prowl-cli skill).
- `AudioSource` defaults to 3D (`Spatial`) with a `MaxDistance` of 10 and linear falloff, so a sound more than 10 units away is silent. A camera does not come with an `AudioListener`, add one to the player camera.

Find any other built in component with `api --search`.

## Editor only code

- Scripts in any folder named `Editor` compile into the editor only assembly and are never shipped.
- Wrap editor code elsewhere in `#if PROWL_EDITOR`. The editor compiles every script with `PROWL_EDITOR` defined, so a missing guard only shows up in a build.
- Without `[ExecuteAlways]`, gameplay callbacks only run in play mode.

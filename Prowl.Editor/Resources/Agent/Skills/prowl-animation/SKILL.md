---
name: prowl-animation
description: Animating characters, arms and weapons in Prowl. Use when importing models or animation files, choosing Generic or Humanoid rigs, playing clips, building or editing .animgraph state machines and blends, driving Animator parameters from scripts, using root motion with a CharacterController, or attaching objects to bones.
---

# Prowl animation

Prowl animation graphs are not Unity's Animator Controller. Transitions have no conditions of their own, states decide when they may be entered and left. Read the graph section before building one, or the graph never leaves its first state.

## Importing

- A model file imports as a `PrefabAsset` with sub assets: meshes, materials, an Avatar named `<model> Avatar`, and one `AnimationClip` per take, named after the take. `asset info --path Models/Arms.fbx` lists them.
- Unless the rig type is None, the importer adds an `Animator` to the model's root with its Avatar and Clips filled in.
- `importer Models/Arms.fbx --values '{"rigType": 2}'` switches the rig. 0 none, 1 Generic (the default), 2 Humanoid.
- A Generic clip plays on another model only where bone names match exactly. A Humanoid clip plays on any humanoid. Rigs that are only arms usually fail humanoid mapping, so keep first person arms and their gun animations Generic, with the same bone names.
- Per clip settings are keyed by the take name: `importer Models/Arms.fbx --values '{"clips.Fire.loop": false, "clips.Fire.name": "Shoot"}'`. Keys are `loop`, `name`, `trimStart`, `trimEnd`, `rootTravel`, `rootTurn`, `rootHeight`. A take name containing a dot, such as `mixamo.com`, can not be written as a dotted key. Change that clip in the model inspector instead, since passing a whole `clips` object replaces every clip's settings.
- **Root travel and root turn are extracted by default**, so a walk clip plays in place and its movement becomes root motion. Set `rootTravel` to false on clips whose movement should stay in the pose.

## Animator

- It needs an `Avatar`, or nothing animates.
- Each frame it runs after every `Update` and before `LateUpdate`. Set parameters in `Update`. Override bones in `LateUpdate`.
- `SetFloat`, `SetBool`, `SetInt`, `SetTrigger`, `ResetTrigger`, and the matching getters. A parameter must be declared in the graph.
- Without a graph: `Play(clip)` or `Play("Fire")` (by name from `Clips`), `CrossFade(clip, seconds)`. `PlayAutomatically` plays `Clips[0]`. There is no separate legacy Animation component.
- Assign a graph with `set /Arms:Animator Graph Anim/Arms.animgraph`.
- Call `Rebind()` after adding or removing bones under it.

```csharp
using Prowl.Runtime;
using Prowl.Vector;

public sealed class ArmsInput : Component
{
    public Animator Arms;

    public override void Update()
    {
        if (Arms.IsNotValid()) return;
        Arms.SetFloat("Speed", Float2.Length(Input.GetWASD()));
        if (Input.GetMouseButtonDown(0)) Arms.SetTrigger("Fire");
        if (Input.GetKeyDown(KeyCode.R)) Arms.SetTrigger("Reload");
    }
}
```

## Root motion

- `ApplyRootMotion = true` moves the Animator's own Transform directly, through walls.
- With a `CharacterController`, leave it off and feed the delta into `Move` in `LateUpdate`. The delta is in the character's local space.

```csharp
using Prowl.Runtime;

public sealed class RootMotionMover : Component
{
    private Animator _animator;
    private CharacterController _controller;

    public override void OnEnable()
    {
        _animator = GetComponentInChildren<Animator>();
        _controller = GetComponent<CharacterController>();
    }

    public override void LateUpdate()
    {
        var delta = _animator.RootMotionDelta;
        _controller.Move(Transform.Rotation * delta.position);
        Transform.Rotation = Transform.Rotation * delta.rotation;
    }
}
```

## Graphs

Create one with `asset create --path Anim/Arms --type AnimationGraph`, read it with `graph Anim/Arms.animgraph`, and edit it with `graph Anim/Arms.animgraph --action edit --ops '<json>'`. `graph --action types` lists every node with its pins and settings. Ops run in order as one undo step, and `"as"` names a result so later ops can use `$name`. A state named with `"as": "fire"` also gives `$fire.output`.

How a graph works:

- The first pose node added to the graph itself becomes the root. Usually that is a `motion.stateMachine`.
- A state is its own small graph. Nodes added with `"owner": "$state"` belong to it, and its output node has three pins: `Pose`, `Enter` and `Exit`.
- **A transition from A to B fires only when A's Exit and B's Enter are both true.** A new state starts with Enter closed and Exit open. So with nothing wired to Enter, nothing ever transitions into that state.
- Open a gate permanently with `{"op": "gate", "state": "$idle", "enter": true}`, or wire a flag into the pin to make it conditional.
- Any State transitions ignore the source's Exit.
- `motion.stateFinished` is true once the state has played through (`Threshold` 0.99). Wire it into Exit for one shot states like fire and reload, and set their clip node's `Looping` to `Once`.
- A trigger is a flag parameter with `"trigger": true`. It turns itself off when a transition fires on it.
- **An unwired required number pin reads 0**, not the value shown in the editor. Feed constants with `motion.constFloat` and its `Value` setting.
- `motion.floatCompare` has pins `A` and `B` and an `Op` setting that defaults to `Greater`.
- `motion.blend1d` has a `Parameter` pin, then one pose pin per input. Each pose input's threshold is its pin value, set with the `input` op. They default to 1, so set every one.
- Layers: `motion.layerBlend` with groups of Layer, Weight and Mask, and `motion.boneMask` whose `Bones` setting reads `"Spine:1,LeftArm:0.5"`.

This builds idle, fire and reload for first person arms:

```json
[
  {"op": "param", "name": "Speed", "kind": "Number", "value": 0},
  {"op": "param", "name": "Fire", "kind": "Flag", "trigger": true},
  {"op": "param", "name": "Reload", "kind": "Flag", "trigger": true},
  {"op": "add", "type": "motion.stateMachine", "as": "sm"},
  {"op": "state", "machine": "$sm", "name": "Idle", "as": "idle"},
  {"op": "state", "machine": "$sm", "name": "Fire", "as": "fire"},
  {"op": "state", "machine": "$sm", "name": "Reload", "as": "reload"},

  {"op": "add", "type": "motion.clip", "owner": "$idle", "as": "idleClip"},
  {"op": "connect", "from": "$idleClip", "to": "$idle.output", "pin": "Pose"},
  {"op": "gate", "state": "$idle", "enter": true},

  {"op": "add", "type": "motion.clip", "owner": "$fire", "as": "fireClip"},
  {"op": "set", "node": "$fireClip", "key": "Looping", "value": "Once"},
  {"op": "connect", "from": "$fireClip", "to": "$fire.output", "pin": "Pose"},
  {"op": "add", "type": "motion.parameter", "owner": "$fire", "as": "fireParam"},
  {"op": "set", "node": "$fireParam", "key": "Name", "value": "Fire"},
  {"op": "connect", "from": "$fireParam", "to": "$fire.output", "pin": "Enter"},
  {"op": "add", "type": "motion.stateFinished", "owner": "$fire", "as": "fireDone"},
  {"op": "connect", "from": "$fireDone", "to": "$fire.output", "pin": "Exit"},

  {"op": "add", "type": "motion.clip", "owner": "$reload", "as": "reloadClip"},
  {"op": "set", "node": "$reloadClip", "key": "Looping", "value": "Once"},
  {"op": "connect", "from": "$reloadClip", "to": "$reload.output", "pin": "Pose"},
  {"op": "add", "type": "motion.parameter", "owner": "$reload", "as": "reloadParam"},
  {"op": "set", "node": "$reloadParam", "key": "Name", "value": "Reload"},
  {"op": "connect", "from": "$reloadParam", "to": "$reload.output", "pin": "Enter"},
  {"op": "add", "type": "motion.stateFinished", "owner": "$reload", "as": "reloadDone"},
  {"op": "connect", "from": "$reloadDone", "to": "$reload.output", "pin": "Exit"},

  {"op": "transition", "machine": "$sm", "from": "Idle", "to": "Fire", "duration": 0.05},
  {"op": "transition", "machine": "$sm", "from": "Idle", "to": "Reload", "duration": 0.15},
  {"op": "transition", "machine": "$sm", "from": "Fire", "to": "Idle", "duration": 0.1},
  {"op": "transition", "machine": "$sm", "from": "Reload", "to": "Idle", "duration": 0.2}
]
```

Then set each clip, using the node ids the edit returned: `{"op": "set", "node": "<id>", "key": "Clip", "value": "Models/Arms.fbx#Fire"}`.

For a speed driven state, wire `motion.parameter` (Name `Speed`) into pin `A` of a `motion.floatCompare`, a `motion.constFloat` (Value 0.1) into pin `B`, and the compare into the state's `Enter`.

## Bones and attachments

- Bones are ordinary GameObjects under the Animator, bound by name. Attach a gun by parenting it under the hand bone: `go parent --target /Gun --parent /Arms/Armature/Hand_R` (find the real path with `tree --root /Arms --depth 10 --filter Hand`), then zero its local position with `go transform`.
- **Never rename bones.** The Animator and the `SkinnedMeshRenderer` find them by name and path.
- First person setup: the arms model under the camera, the gun under the hand bone, and one graph like the one above.
- `motion.ragdoll` and `motion.footGrounding` exist for physical characters, see `graph --action types --filter ragdoll`.

Look up `Animator`, `AnimationGraph`, `AnimationClip`, `Avatar` and `AvatarMask` with `api --type`.

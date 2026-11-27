# Prowl.Motion

A skeletal animation library for the Prowl Game Engine. It samples and blends clips, runs animation graphs with state machines and layers, drives IK, and retargets between humanoid rigs through a muscle space.

Pure C#, no engine dependency and no reflection. Math via Prowl.Vector.

## Features

- **Clips**
  - Uniformly sampled clips, and compressed clips (constant channels stored once, quantized rotation, position and scale)
  - Additive clips, root motion tracks, sync tracks, events, secondary clips for attached skeletons
  - Scalar channels alongside the bones (blend shape weights and the like), which blend with the pose,
    can be read as graph values, written from graph values, or driven by a joint angle
  - Play a clip on another rig that names its bones the same way, through a skeleton mapping

- **Graphs** (definition shared, instance per character)
  - Clip, Blend1D, Blend2D, velocity blend, weighted blend, selectors, random selector, sequence, speed scale
  - State machines with timed, synchronized or frozen transitions and easing
  - Inertialized transitions, pose snapshot, make additive, pose smoothing
  - Override, additive and masked layers, bone masks, muscle space layers
  - Value nodes (float, bool, int, vector, id, target), noise, timers, springs, curves, event conditions, state queries
  - Referenced sub graphs, external graph slots, and a pose the host writes each frame
  - Write your own nodes: the node bases, bind context and playback types are all public

- **IK, constraints and warping**
  - Two bone IK, chain IK, multi effector IK rig, look at, foot grounding, foot lock
  - Aim and copy constraints, twist distribution along a limb, spring bones for hair and cloth
  - Orientation and target warping of root motion, target matching, stride warping, root motion filtering

- **Humanoid**
  - Automatic humanoid bone mapping from bone names and topology
  - 95 muscle human pose, retargeting between rigs of any proportions and bind pose
  - Mirroring, muscle space blending and masking
  - Clips baked into muscle space, which bind to any humanoid avatar and play like an ordinary clip

- **Runtime**
  - No allocations after warm up
  - Skeletons and clips are immutable and shared, instances update independently on any thread
  - A compact binary form for skeletons, clips and humanoid mappings, so a host can store them

## Usage

### Building a skeleton and a clip

```csharp
using Prowl.Motion;

var skeleton = new Skeleton(boneIds, parentIndices, referencePose);

// One pose per frame, sampled at a uniform rate.
var clip = new CompressedAnimationClip(skeleton, frames, durationSeconds);
```

### Playing clips

Subclass an animator and write the pose back to your engine.

```csharp
sealed class CharacterAnimator : SimpleAnimator
{
    public CharacterAnimator(Skeleton skeleton) : base(skeleton) { }

    protected override void ApplyBoneTransform(int boneIndex, in Transform3D local)
    {
        // write local position, rotation and scale to the engine bone
    }
}

var animator = new CharacterAnimator(skeleton);
animator.Play(idle);
animator.CrossFade(run, 0.25f);
animator.Update(deltaTime);
```

### Graphs

```csharp
var graph = new AnimationGraph();
int speed = graph.AddFloatParameter("Speed");
int walk = graph.AddClip(walkClip);
int run = graph.AddClip(runClip);
graph.SetRoot(graph.AddBlend1D(speed, new[] { (walk, 1.5f), (run, 5f) }));

var instance = graph.CreateInstance(skeleton);
instance.SetFloat("Speed", 3f);
instance.Update(deltaTime, worldTransform);
// instance.Pose, instance.RootMotionDelta, instance.Events
```

### Baking a clip for any humanoid

```csharp
HumanoidClip baked = HumanoidClip.Bake(sourceAvatar, sourceClip);
animator.Play(baked.Bind(targetAvatar));
```

### Saving and loading

```csharp
using (var writer = new BinaryWriter(stream))
{
    MotionBinary.Write(writer, skeleton);
    MotionBinary.Write(writer, clip);
}

using var reader = new BinaryReader(stream);
Skeleton loadedSkeleton = MotionBinary.ReadSkeleton(reader);
AnimationClipBase loadedClip = MotionBinary.ReadClip(reader, loadedSkeleton);
```

Events are not written: they usually point at host code, so the host stores them its own way and
passes them back when reading a clip.

### Humanoid retargeting

```csharp
Avatar source = AvatarBuilder.BuildAutomatic(sourceSkeleton);
Avatar target = AvatarBuilder.BuildAutomatic(targetSkeleton);

var human = new HumanPose();
Retargeter.RetargetFrom(source, sourcePose, human);
Retargeter.RetargetTo(target, human, targetPose);
```

## Conventions

- **Coordinate system**: left handed, Y up, characters facing +Z.
- **Poses**: local (parent space) transforms are the source of truth. Model space transforms are computed on demand.
- **Bones**: identified by a hashed name (`StringID`) and addressed by index at runtime.

## Layout

```
Motion/              main library
Tests/               xUnit test suite
```

## License

This component is part of the Prowl Game Engine and is licensed under the MIT License. See the LICENSE file in the project root for details.

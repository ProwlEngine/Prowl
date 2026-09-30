# Particles (Rendering Side)

Source: [`Components/ParticleSystem/ParticleSystemComponent.cs`](../../Prowl.Runtime/Components/ParticleSystem/ParticleSystemComponent.cs)
and [`Modules/`](../../Prowl.Runtime/Components/ParticleSystem/Modules/) (simulation modules: Initial, Emission,
VelocityOverLifetime, ColorOverLifetime, SizeOverLifetime, RotationOverLifetime, UV, Wind, Collision, Light),
[`Particle.shader`](../../Prowl.Runtime/Assets/Defaults/Particle.shader), `Particle.mat`,
[`InstancedMeshRenderable.cs`](../../Prowl.Runtime/InstancedMeshRenderable.cs).

Simulation is gameplay/core; this page covers what the renderer consumes.

## Collect

`OnRenderCollect`: skip if no particles, no material, or no quad mesh.
1. `UpdateInstanceData`: per particle, world position (local simulation space is transformed by the system's
   `LocalToWorld`), matrix `T(pos) * Rz(rotation) * S(size)`, color, custom data
   `(normalizedLifetime, uvOffset.x, uvOffset.y, uvScale)` from the UV (flipbook) module; world AABB grown by
   `size / 2` per particle.
2. `_ObjectID` into shared props.
3. `InstancedMeshRenderable.CreateBatched(...)` with the system position as the sort position (stable depth sort, no
   per-particle sorting) and batches of 1023.
4. If `Light` module enabled: add one pooled `ParticleLightProxy` per alive particle to the lights list (see
   [light components](../lighting/light-components.md#particle-lights)).

## Shader (`Default/Particle`)

`RenderOrder=Transparent`, `Blend Alpha`, `Cull Off`, `ZWrite Off`. Instanced vertex path: particle position from
matrix column 3, scale from column lengths, Z rotation recovered with `atan2` from column 0, then a camera-facing
billboard built from the view matrix's right/up rows. UV = `uv * scale + offset` for flipbooks, color = vertex *
instance color. Fragment: texture * color * `_MainColor`, discard alpha < 0.01, `gammaToLinearSpace`, no lighting, no
fog. `_SoftParticlesFactor` is declared but soft particles are not implemented.

## Rebuild notes

- Particles are unlit, unfogged, unsorted within a system, and have no motion vectors.
- The billboard is rebuilt in the vertex shader from the instance matrix; a rebuild could upload position/size/rotation
  directly.

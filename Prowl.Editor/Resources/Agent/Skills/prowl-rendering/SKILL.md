---
name: prowl-rendering
description: Materials, shaders, cameras, lights and effects in Prowl. Use when creating or editing materials, picking shaders, setting up cameras (field of view, clip planes, a weapon viewmodel camera), adding post processing, lights, fog, skybox, lightmap bakes, or particle effects such as muzzle flashes.
---

# Prowl rendering

## Materials

The built in `Standard` shader uses these property names. `material <ref>` lists the real ones for any shader.

| Property | Meaning |
| --- | --- |
| `_MainTex`, `_MainColor` | albedo texture and tint |
| `_NormalTex`, `_NormalScale` | normal map |
| `_SurfaceTex` | G is roughness, B is metallic |
| `_Roughness`, `_Metallic` | multipliers on the surface texture |
| `_OcclusionTex` | ambient occlusion in R |
| `_EmissionTex`, `_EmissiveColor`, `_EmissionIntensity` | emission |
| `_Tiling`, `_Offset` | UV tiling and offset |

**Metallic and emission do nothing on their own.** The shader multiplies them by a texture: metallic is `_SurfaceTex.b * _Metallic`, and the default surface texture has a blue of 0. Emission is `_EmissionTex * _EmissiveColor * _EmissionIntensity`, and the default emission texture is black. Assign a surface or emission texture (a white texture works for emission) before those values show. `_Roughness` works without a texture and defaults to 1, fully rough.

- From the CLI: `material Materials/Metal.mat --values '{"_MainColor": [0.8, 0.8, 0.8, 1], "_EmissionTex": "Textures/White.png", "_EmissiveColor": [1, 0.5, 0, 1], "_EmissionIntensity": 4}'`.
- In code: `material.SetColor`, `SetFloat`, `SetTexture`, `SetVector`.
- Shader variants are separate shaders: `Shader.LoadDefault(DefaultShader.StandardTransparent)`, also `StandardCutout`, `StandardDoubleSided` and others. There is no `Shader.Find`.
- **There is no MaterialPropertyBlock.** For a per object colour, `Clone()` the material and assign the copy. Dispose clones you no longer need. Changing a shared material asset changes every object using it.
- `.shader` files have their own format with GLSL bodies. Start from an existing shader or the editor's create menu, not from Unity ShaderLab.

## Cameras

- A newly added Camera component has `HDR` off, `FarClipPlane` 100 and `FieldOfView` 60. The default scene's Main Camera has HDR on.
- `Camera.Main` is the enabled camera with the highest `Depth` that draws to the screen.
- Post processing is a list of `ImageEffect` objects on the camera, not components. They live in `Prowl.Runtime.Rendering`: `TonemapperEffect`, `BloomEffect`, `FXAAEffect`, `TAAEffect`, `SMAAEffect`, `GTAOEffect`, `ScreenSpaceReflectionEffect`, `MotionBlurEffect`, `BokehDepthOfFieldEffect`, `AutoExposureEffect`, `VolumetricFogEffect`. Turn on `HDR` when using bloom or tonemapping.

```csharp
// prowl eval: HDR, bloom and tonemapping on the main camera
using Prowl.Runtime.Rendering;
var camera = Scene.Current.FindObjectsOfType<Camera>()[0]!;
camera.HDR = true;
camera.Effects = [new BloomEffect(), new TonemapperEffect(), new FXAAEffect()];
return camera.Effects.Count;
```

Eval changes do not mark the scene dirty, so `scene --action save` afterwards.

A first person viewmodel camera keeps the gun from clipping into walls:

```csharp
using Prowl.Runtime;

public sealed class ViewmodelSetup : MonoBehaviour
{
    public Camera World;
    public Camera Viewmodel;

    public override void Start()
    {
        int weapon = LayerMask.NameToLayer("Weapon");
        World.CullingMask.RemoveLayer(weapon);

        Viewmodel.Depth = World.Depth + 1;
        Viewmodel.ClearFlags = CameraClearFlags.Depth;
        Viewmodel.CullingMask = LayerMask.FromMask(1u << weapon);
        Viewmodel.NearClipPlane = 0.01f;
    }
}
```

The viewmodel camera is a child of the world camera, and the arms and gun objects are on the `Weapon` layer (define it first, see the prowl-cli eval recipes).

## Environment

`Scene.Current.Fog`, `Ambient` and `Skybox` are struct fields. Set them directly, for example `Scene.Current.Fog.Mode = Scene.FogParams.FogMode.Off`. Copying one into a local and changing the local does nothing. Fog is on by default (exponential squared, density 0.01).

## Lights and lightmaps

- `DirectionalLight`, `PointLight`, `SpotLight`. The brightest directional light is the sun.
- Only objects with `IsStatic` set get lightmaps. The bake runs from the Environment panel and only progresses while that panel is open.

## Particles

- `ParticleSystemComponent` (namespace `Prowl.Runtime.ParticleSystem`) modules are fields: `Initial`, `Emission`, `Shape`, `Renderer` start enabled, the rest (`ColorOverLifetime`, `SizeOverLifetime`, `Trails` and so on) need `Enabled = true`.
- Curves are `MinMaxCurve`. From the CLI: `set /Muzzle:ParticleSystemComponent Emission.RateOverTime.ConstantValue 0`.

```csharp
using Prowl.Runtime;
using Prowl.Runtime.ParticleSystem;

public sealed class MuzzleFlash : MonoBehaviour
{
    public ParticleSystemComponent Flash;

    public override void Start()
    {
        Flash.Looping = false;
        Flash.PlayOnEnable = false;
        Flash.Emission.RateOverTime.ConstantValue = 0f;
        Flash.Initial.StartLifetime.ConstantValue = 0.05f;
        Flash.Initial.StartSpeed.ConstantValue = 0f;
    }

    public void Fire() => Flash.Emit(6);   // emits right away, playing or not
}
```

Look up `Material`, `Shader`, `Camera`, `ImageEffect` and `ParticleSystemComponent` with `api --type`.

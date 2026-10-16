---
name: prowl-rendering
description: Materials, shaders, cameras, lights and effects in Prowl. Use when creating or editing materials, picking shaders, setting up cameras (field of view, clip planes, a weapon viewmodel camera), adding post processing, lights, fog, skybox, lightmap bakes, or particle effects such as muzzle flashes.
---

# Prowl rendering

## Materials

**Colour values are linear**, in materials and lights alike. Textures are decoded from sRGB, but a colour you type is used as is, so a colour picked in an image editor comes out too bright and washed out. Convert it first, roughly `value ^ 2.2`: sRGB 0.5 grey is linear 0.21.

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

- A newly added Camera component has `FarClipPlane` 100, `FieldOfView` 60, no `Profile` and `AntiAliasing` `None`. The default scene's Main Camera is the same.
- `Camera.Main` is the enabled camera with the highest `Depth` that draws to the screen.
- Anti aliasing is a camera field, not an effect: `camera.AntiAliasing = AntiAliasingMode.FXAA` (also `SMAA`, `TAA`), tuned through `camera.Fxaa`, `camera.Smaa` and `camera.Taa`.
- Post processing comes from volume profiles. A `VolumeProfile` holds volume components from `Prowl.Runtime.Rendering`: `Tonemapping`, `Bloom`, `AutoExposure`, `AmbientOcclusion`, `ScreenSpaceReflections`, `MotionBlur`, `DepthOfField`, `GodRays`, `Vignette`, `ChromaticAberration`, `FilmGrain`, `ColorGrading`, `ColorLookup`, `Sharpen`, `EdgeDetection`, `Pixelation`, `VolumetricFog`. The camera's own `Profile` applies under every `Volume` component in the scene. A `Volume` is global by default, or set `IsGlobal = false` and a `Size` to apply it only inside a box.
- Each setting is a `VolumeParameter` and only counts once overridden. Use `.Set(value)` to set and override it in one go, or `Add<T>(overrideAll: true)`.
- `Bloom` is off until `Intensity` is above 0. `Threshold` defaults to 1.2, so mostly emissive and very bright surfaces bloom. Lower it to make lit surfaces glow too. `Tonemapping` is off while `Mode` is `TonemappingMode.None`. `AgX` is a soft filmic curve that desaturates bright colours, `ACES` keeps colours punchier.

```csharp
// prowl eval: bloom, tonemapping and FXAA on the main camera
using Prowl.Runtime.Rendering;
var camera = Scene.Current.FindObjectsOfType<Camera>()[0]!;
camera.Profile = new VolumeProfile { Name = "Main Camera Profile" };
camera.Profile.Add<Bloom>().Intensity.Set(1f);
camera.Profile.Add<Tonemapping>().Mode.Set(TonemappingMode.ACES);
camera.AntiAliasing = AntiAliasingMode.FXAA;
return camera.Profile.Components.Count;
```

Eval changes do not mark the scene dirty, so `scene --action save` afterwards.

A first person viewmodel camera keeps the gun from clipping into walls:

```csharp
using Prowl.Runtime;

public sealed class ViewmodelSetup : Component
{
    public Camera World;
    public Camera Viewmodel;

    public override void Start()
    {
        int weapon = LayerMask.NameToLayer("Weapon");
        World.CullingMask.RemoveLayer(weapon);

        Viewmodel.Depth = World.Depth + 1;
        Viewmodel.Background = CameraBackground.None;
        Viewmodel.CullingMask = LayerMask.FromMask(1u << weapon);
        Viewmodel.NearClipPlane = 0.01f;
    }
}
```

The viewmodel camera is a child of the world camera, and the arms and gun objects are on the `Weapon` layer (define it first, see the prowl-cli eval recipes).

## Environment

`Scene.Current.Environment` holds the sky, ambient light and fog. `SkyMaterial` is a Material whose shader has a Sky pass, usually from `SceneEnvironment.CreateSkyMaterial(BuiltInSky.Procedural)` (also Gradient, Cubemap, Panorama, SixSided), null shows `SkyColor`. `Ambient` (Mode Uniform, Hemisphere or Sky, Intensity, colours) and `Fog` (Mode Off, Linear, Exponential, ExponentialSquared, Color, Density, Start, End, UseSky) are struct fields: set them directly, for example `Scene.Current.Environment.Fog.Mode = FogMode.Off` (FogMode and AmbientMode are in Prowl.Graphite.Rendering). Copying one into a local and changing the local does nothing. Fog is on by default (exponential squared, density 0.01).

## Lights and lightmaps

- One `Light` component, its `Type` one of `LightType.Directional`, `Point`, `Spot`, `Rect`, `Disk`, `Tube`. It shines along Forward. Spot angles are half angles (`InnerAngle`, `OuterAngle`), rects use `Width` and `Height`, disks `Width` as the diameter, tubes `Length` and `SourceRadius`. `Cookie` (2D for a spot, cubemap for a point) and `IesProfile` work on points and spots. Directional, point and spot lights cast shadows (`CastShadows`, `ShadowResolution`, `ShadowFilter`, directional `Cascades` and `ShadowDistance`). The brightest directional light is the sun.
- Renderers take `ShadowCasting` (`Off`, `On`, `ShadowsOnly`) and `ReceiveShadows`.
- Only objects with `IsStatic` set get lightmaps. The bake runs from the Environment panel and only progresses while that panel is open.

## Particles

- `ParticleSystemComponent` (namespace `Prowl.Runtime.ParticleSystem`, module types and their enums such as `ParticleBlendMode` in `Prowl.Runtime.ParticleSystem.Modules`) modules are fields: `Initial`, `Emission`, `Shape`, `Renderer` start enabled, the rest (`ColorOverLifetime`, `SizeOverLifetime`, `Trails` and so on) need `Enabled = true`.
- Curves are `MinMaxCurve`. From the CLI: `set /Muzzle:ParticleSystemComponent Emission.RateOverTime.ConstantValue 0`.

```csharp
using Prowl.Runtime;
using Prowl.Runtime.ParticleSystem;

public sealed class MuzzleFlash : Component
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

Look up `Material`, `Shader`, `Camera`, `VolumeProfile` and `ParticleSystemComponent` with `api --type`.

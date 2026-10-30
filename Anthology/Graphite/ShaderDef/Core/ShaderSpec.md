# ShaderDef Shader Markup Specification

Graphite uses a ShaderLab-inspired declarative render state markup language known as ShaderDef
If that was a mouthful, it is a text description of how your CPU should set up your GPU to draw a shader.
It uses GLSL shaders in conjunction with a custom ShaderLab-adjacent syntax to describe GPU render state, shader variants, and the programs to use.

---

## Table of Contents

- [File Structure](#file-structure)
- [Properties Block](#properties-block)
- [Surface Block](#surface-block)
- [Pass Block](#pass-block)
  - [Name](#name)
  - [Tags](#tags)
  - [Render State Commands](#render-state-commands)
  - [Stencil Block](#stencil-block)
  - [GLSLPROGRAM Block](#glslprogram-block)
- [Fallback](#fallback)
- [Diagnostics](#diagnostics)
- [Full Example](#full-example)

---

## File Structure

```
Shader "ShaderName"
{
    Properties { ... }      // optional

    Surface "Lit" { ... }   // optional, the standard passes

    Pass [index]            // one or more without a Surface block
    {
        ...
    }

    Fallback "FallbackShaderName"
}
```

| Element          | Required | Notes                                              |
|------------------|----------|----------------------------------------------------|
| `Shader "Name"`  | Yes      | Quoted display name for the shader                 |
| `Properties`     | No       | Metadata for engine-side tools; Graphite ignores it |
| `Surface`        | No       | Graphite's standard passes running a surface function |
| `Pass`           | Yes (≥1) | One block per rendering pass, none needed after a `Surface` block |
| `Fallback`       | No       | Metadata for engine-side tools; Graphite ignores it |

---

## Properties Block

The `Properties` block declares named default parameters that can be set on a material at runtime.

Graphite parses and stores these on `ShaderDefinition.Properties` but does not use them itself. They
exist for engine-side consumers such as a material inspector.

```
Properties
{
    PropertyName("Display Name", Type) = DefaultValue
    ...
}
```

The default value is optional. If omitted, the property is zero-initialized.

### Property Types

| Type                 | Default Value Syntax              | Description                          |
|----------------------|-----------------------------------|--------------------------------------|
| `Integer`            | `= 1`                             | 32-bit integer (stored as float)     |
| `Float`              | `= 0.5`                           | 32-bit floating-point scalar         |
| `Vector`             | `= (x, y, z, w)`                  | Four-component floating-point vector |
| `Color`              | `= (r, g, b, a)`                  | Four-component color (same as Vector)|
| `Matrix`             | `= ((row0)(row1)(row2)(row3))`    | 4×4 floating-point matrix            |
| `Texture2D`          | `= "name" {}`                     | 2D texture; `name` is a built-in default (e.g. `"red"`, `""`) |
| `Texture2DArray`     | `= "name" {}`                     | Array of 2D textures                 |
| `Texture3D`          | `= "name" {}`                     | 3D volume texture                    |
| `TextureCubemap`     | `= "name" {}`                     | Cubemap texture                      |
| `TextureCubemapArray`| `= "name" {}`                     | Array of cubemap textures            |

### Example

```
Properties
{
    _Color("Tint Color", Color) = (1, 1, 1, 1)
    _Roughness("Roughness", Float) = 0.5
    _Count("Sample Count", Integer) = 4
    _Offset("UV Offset", Vector) = (0, 0, 0, 0)
    _AlbedoMap("Albedo", Texture2D) = "" {}
    _Cubemap("Environment", TextureCubemap) = "" {}
}
```

### Attributes

Attributes follow the default value, each in brackets: `[Name]` or `[Name(arg, arg)]`. Arguments are numbers,
identifiers or quoted strings. ShaderDef keeps each as text; the meanings below are what Graphite's renderer and the
inspectors built on it read.

| Attribute              | On                 | Meaning                                                                |
|------------------------|--------------------|------------------------------------------------------------------------|
| `Gamma`                | `Color`            | The value is entered in gamma space and stored linear                  |
| `Range(min, max)`      | `Float`, `Integer` | Shown as a slider between the bounds                                   |
| `Keyword(AXIS)`        | textures           | The material toggle `AXIS` is on while a texture is set                |
| `Toggle(AXIS)`         | `Float`, `Integer` | The material toggle `AXIS` is on while the value is not zero, or any of them when several drive it |
| `TwoSided`             | `Float`, `Integer` | With `Toggle(AXIS)`: while that toggle is on the material draws both sides whatever its culling, back faces with the normal flipped |
| `Transmissive`         | `Float`, `Integer` | With `Toggle(AXIS)`: while that toggle is on the material draws as transparent whatever its surface, through its `Transmission` pass in place of `Forward`, and unless it also draws two sided, through its `BackDepth` pass before the transparent surfaces |
| `KeywordEnum(AXIS)`    | `Integer`          | The material axis `AXIS` takes the value at the integer's index        |
| `Enum(A, B, C)`        | `Integer`          | Shown as a dropdown of the names, stored as their index                |
| `Header("Text")`       | any                | A heading drawn above the property                                     |
| `Space`                | any                | A gap drawn above the property                                         |
| `ShowIf(AXIS)`         | any                | Shown only while the toggle `AXIS` is on; `ShowIf(AXIS, VALUE)` for an enum axis |
| `HideInInspector`      | any                | Not shown                                                              |
| `NoScaleOffset`        | textures           | No per texture scale and offset shown                                  |
| `Normal`               | textures           | Expects a tangent space normal map                                     |
| `HDR`                  | `Color`            | The colour may exceed 1                                                |
| `Map`                  | `Texture2D`        | A packed map: 16 bytes holding the texture, sampler, channel, UV set and a half scale offset, read as `X`, `X_ST`, `X_Channel`, `X_UV` and `X_Present` |
| `MapMask`              | `Integer`          | Filled with a bit per `Map` texture, in declaration order, set while it holds a texture |

A material that sets an axis explicitly overrides what its properties drive.

---

## Surface Block

The quickest way to a custom lit or unlit shader. `Surface "Lit"` or `Surface "Unlit"` gives the shader Graphite's
standard Forward, Prepass, ShadowCaster and Debug passes with every feature of Graphite/Lit or Graphite/Unlit: shadows,
lightmaps, probes, decals, fog, surface types and their properties. The shader's own `Properties` are added to the
standard ones, replacing any of the same name.

Graphite/Lit is OpenPBR Surface v1.1.1. Its properties are the spec's parameters under their spec names, such as
`base_color`, `specular_roughness` and `coat_weight`, and engine extensions under plain names: `ambient_occlusion`,
`normal_scale`, `coat_normal_scale`, `tangent_rotation` and `alpha_cutoff`. Every texturable input `X` has a map `X_map`,
and `geometry_normal_map`, `geometry_coat_normal_map` and `geometry_tangent_map` feed the frame. The input is `X` times
its map wherever the map is set. A map's channel, UV set, scale and offset are part of its texture value, set with
`Material.SetTextureChannel`, `SetTextureUVSet` and `SetTextureScaleOffset`. Transparent PBR surfaces write
premultiplied colour with `geometry_opacity` as coverage. Its Forward, Transmission and Debug passes are tagged
`"Output" = "Premultiplied"`, so `SurfaceSettings` blends them with premultiplied factors whichever `SurfaceBlend` is set:
Alpha and Premultiplied as One, InverseSourceAlpha, Additive as One, One.

The coat, fuzz, thin film and anisotropy are material toggles of the Forward and Debug passes, `PBR_COAT`, `PBR_FUZZ`,
`PBR_THIN_FILM` and `PBR_ANISOTROPY`, each on while `coat_weight`, `fuzz_weight`, `thin_film_weight` or either roughness
anisotropy is not zero, so surfaces without them pay nothing. A Surface function that raises one of those from zero
needs its material to enable the toggle with `Material.EnableKeyword`. The global `PBR_TIER` LITE drops the thin film
and anisotropy and lights the coat and fuzz from probes and ambient light only.

`PBR_TRANSMISSION` follows `transmission_weight` (`[Transmissive]`). It is declared by the Debug pass, and the
Transmission pass defines it: that pass refracts the view's opaque colour behind the surface through the volume whose
far side the BackDepth pass draws, and is tagged `"ReadsWhen" = "PBR_TIER FULL"`, so LITE views copy nothing and
refract the probes instead. See `Graphite/PBR/Transmission.glsl`.

The optional GLSLPROGRAM block holds fragment code defining the model's Surface function, which may change the inputs
before the surface is lit. Its second argument is read only: `UV`, `UV1`, `VertexColor`, `RelativePos`, `ViewDir` and
`GeometryNormal`, `Tangent` and `Bitangent`.

- PBR: `void Surface(inout PBR_Params p, Graphite_SurfaceData i)`. `PBR_Params` holds every spec parameter in camel
  case, `baseColor` for `base_color`, with `geometryNormal` in world space and `occlusion` for `ambient_occlusion`.
- Unlit: `void Surface(inout Unlit_Params p, Graphite_SurfaceData i)` over `BaseColor` and `Alpha`.

The depth passes run it only for cutout surfaces, with the UVs, vertex colour and relative position filled in, so a
cutout driven by any of them cuts shadows and depth the same. Their `ViewDir` points toward the depth pass's own camera,
a light's for shadows, and their normals keep defaults.

Positions in Graphite shaders are relative: world space moved so the camera sits at the origin, which keeps the
precision of objects far from it. World means true world space. `Graphite_RelativeToWorld(i.RelativePos)` gives the
world position, for effects fixed to the world such as a dissolve. Directions and normals are world space, as moving the
origin changes none of them.

A tangent space normal map, such as a detail map, goes through `Graphite_SurfaceNormalMap(i, texel, scale)`, which
places it on the mesh's frame and returns a world normal. `Graphite_StandardNormalMap(texel, normal, tangent, bitangent,
scale)` takes any frame, for blending onto `p.geometryNormal` or for triplanar mapping.

```
Shader "MyGame/Rim"
{
    Properties
    {
        _RimColor("Rim Color", Color) = (0.3, 0.6, 1.0, 1.0)
        _RimPower("Rim Power", Float) = 3.0
    }

    Surface "PBR"
    {
        GLSLPROGRAM
        uniform vec4 _RimColor;
        uniform float _RimPower;

        void Surface(inout PBR_Params p, Graphite_SurfaceData i)
        {
            float rim = 1.0 - max(dot(p.geometryNormal, i.ViewDir), 0.0);
            p.emissionColor = _RimColor.rgb;
            p.emissionLuminance = 500.0 * pow(rim, _RimPower);
        }
        ENDGLSL
    }
}
```

An optional `Vertex` block moves vertices for wind, waves and wobble. It defines
`void SurfaceVertex(inout Graphite_SurfaceVertex v)` over the object space `Position`, `Normal`, `Tangent` (w the
handedness), `Color`, `UV` and `UV1`, run before the vertex is transformed in every pass, so shadows and depth match.
Animate with `v.Time` rather than the view's time: the forward pass runs the function again at last frame's time to find
where the vertex was, which keeps motion vectors, TAA and motion blur right.

```
Surface "PBR"
{
    Vertex
    {
        GLSLPROGRAM
        uniform float _WaveHeight;

        void SurfaceVertex(inout Graphite_SurfaceVertex v)
        {
            v.Position += v.Normal * sin(v.Position.x * 2.0 + v.Time) * _WaveHeight;
        }
        ENDGLSL
    }
}
```

A surface model marks where this goes with a `#pragma surface_vertex` line in each pass's Vertex section, before
Graphite/Standard/StandardVertex.glsl is included.

An optional `Lighting` block replaces how the surface is lit, for toon ramps and other custom shading. It defines
either function below or both, over the closure Graphite/PBR/Closure.glsl prepares once per pixel:

- `vec3 Graphite_BRDF(PBR_Closure c, Graphite_LightContribution l)`, which every light goes through. It may call
  `PBR_EvaluateLight(c, l)`, or `PBR_EvaluateDiffuse(c, L)` and `PBR_EvaluateSpecular(c, L)` for each lobe.
- `vec3 Graphite_AmbientBRDF(PBR_Closure c, PBR_Indirect i)`, which ambient light goes through: `i.irradiance` is the
  diffuse light over pi from the lightmap, probes or sky, `i.specular` the base lobe's probe radiance, both with
  occlusion applied. The default, `PBR_EvaluateAmbient(c, i)`, returns
  `c.diffuseAlbedo * i.irradiance + c.specularAlbedo * i.specular`, and a toon surface can band or tint its shadow
  side here.

Only the lit passes take it, Forward and Debug for `Surface "PBR"`, so decals and every surface type keep working.
`Surface "Unlit"` has no lights and refuses one. Its uniforms may repeat the surface code's, as every material uniform
is a property.

```
Surface "PBR"
{
    Lighting
    {
        GLSLPROGRAM
        vec3 Graphite_BRDF(PBR_Closure c, Graphite_LightContribution l)
        {
            float ramp = step(0.5, dot(c.normal, l.diffuseL) * 0.5 + 0.5);
            return c.diffuseAlbedo * ramp * l.diffuse * l.radiance;
        }
        ENDGLSL
    }
}
```

A surface model marks where this goes with a `#pragma surface_lighting` line in a pass's Shared section, before
Graphite/Standard/Standard.glsl is included.

Further `Pass` blocks may follow, such as a pass of a type a render feature draws.

---

## Pass Block

```
Pass [index]
{
    Name "PassName"         // optional
    Tags { ... }            // optional

    // Render state commands (zero or more, any order)
    ZTest LessEqual
    ZWrite On
    Cull Back
    ...

    Stencil { ... }         // optional

    GLSLPROGRAM
    ...                     // embedded GLSL sections
    ENDGLSL
}
```

The optional integer `index` after `Pass` identifies the pass for multi-pass selection.

### Name

```
Name "ShadowCaster"
```

Assigns a human-readable name to the pass. Used for pass lookup and debugging.

### Tags

```
Tags { "LightMode" = "ForwardBase"  "Queue" = "Transparent" }
```

Arbitrary key-value string pairs associated with the pass. Tags are consumed by the library or
render pipeline; the shader system stores them but does not interpret them. Graphite's renderer reads these:

| Tag            | Values                                                        | Meaning                                                                 |
|----------------|---------------------------------------------------------------|-------------------------------------------------------------------------|
| `LightMode`    | `Prepass`, `Forward`, `ShadowCaster`, `ObjectId`, `Sky`, `Transmission`, `BackDepth`, any other name a feature draws | The pass type the renderer draws the pass as. `Transmission` replaces `Forward` for transmissive materials, `BackDepth` draws their back faces' distance along the view ray, culling front faces, unblended. `ShadowCaster` draws every material casting shadows, transparent ones included, unblended with depth written; one that lets light through discards fragments to cast a partial shadow |
| `RenderQueue`  | an integer, 2500 and up transparent                           | The default render queue of the shader's materials                      |
| `SceneColor`   | `Read`                                                        | The pass samples the view's opaque colour through `Graphite/SceneColor.glsl`. A view whose transparent list holds such a pass copies its colour after the opaque surfaces and the sky, with mips, before drawing its transparent surfaces |
| `SceneDepth`   | `Read`                                                        | The pass fetches the view's opaque depth through `Graphite/SceneDepth.glsl`. A view whose transparent list holds such a pass copies its depth after the opaque surfaces before drawing its transparent surfaces |
| `BackDepth`    | `Read`                                                        | The pass fetches the view's back depth, which `TransmissionDepthFeature` draws from the `BackDepth` passes of the view's transmissive volumes after the opaque surfaces |
| `ReadsWhen`    | `AXIS VALUE`, a global axis and one of its values             | The pass's `SceneColor`, `SceneDepth` and `BackDepth` reads count only in views at that value, a view without the axis set taking its first |

---

## Render State Commands

All render state commands are optional. Unspecified fields fall back to the library defaults listed
below.

### Culling

```
Cull Back | Front | Off
```

Controls which polygon faces are discarded before rasterization.

| Value   | Description                          | Default |
|---------|--------------------------------------|---------|
| `Back`  | Cull back-facing polygons            | ✓       |
| `Front` | Cull front-facing polygons           |         |
| `Off`   | Disable face culling (double-sided)  |         |

---

### Depth Test — `ZTest`

```
ZTest Disabled | Never | Less | Equal | LessEqual | Greater | NotEqual | GreaterEqual | Always
```

Sets the depth comparison function. `Disabled` turns depth testing off entirely; any other value enables depth testing and sets the comparison function.

| Value          | Description                                          | Default |
|----------------|------------------------------------------------------|---------|
| `Disabled`     | Depth testing is turned off entirely                 |         |
| `Never`        | Never passes                                         |         |
| `Less`         | Passes if fragment depth < stored depth              |         |
| `Equal`        | Passes if fragment depth = stored depth              |         |
| `LessEqual`    | Passes if fragment depth <= stored depth             | ✓       |
| `Greater`      | Passes if fragment depth > stored depth              |         |
| `NotEqual`     | Passes if fragment depth != stored depth             |         |
| `GreaterEqual` | Passes if fragment depth >= stored depth             |         |
| `Always`       | Always passes (depth test is effectively skipped)    |         |

---

### Depth Write — `ZWrite`

```
ZWrite On | Off
```

Controls whether passing fragments write their depth value to the depth buffer.

| Value | Description                   | Default |
|-------|-------------------------------|---------|
| `On`  | Write depth to depth buffer   | ✓       |
| `Off` | Do not write depth            |         |

---

### Depth Clamp — `ZClip`

```
ZClip On | Off
```

Controls depth clamping. When `Off`, fragments outside the near/far clip planes are clamped to
the depth range rather than clipped. Equivalent to OpenGL's `GL_DEPTH_CLAMP`.

| Value | Description                                      | Default |
|-------|--------------------------------------------------|---------|
| `On`  | Standard depth clipping (no clamping)            | ✓       |
| `Off` | Enable depth clamping — clip planes not enforced |         |

---

### Blending — `Blend`, `BlendRGB`, `BlendAlpha`

```
Blend    <SrcFactor> <DstFactor>
BlendRGB <SrcFactor> <DstFactor>
BlendAlpha <SrcFactor> <DstFactor>
```

- `Blend` — sets the same source and destination blend factors for both RGB and alpha channels.
- `BlendRGB` — sets factors for the RGB channels only.
- `BlendAlpha` — sets factors for the alpha channel only.

Specifying any `Blend` command implicitly enables blending for the pass.

#### Blend Factors

| Value                   | Description                                              |
|-------------------------|----------------------------------------------------------|
| `Zero`                  | Factor is `(0, 0, 0, 0)`                                 |
| `One`                   | Factor is `(1, 1, 1, 1)`                                 |
| `SourceColor`           | Source color `(Rs, Gs, Bs, As)`                          |
| `InverseSourceColor`    | `1 - SourceColor`                                        |
| `SourceAlpha`           | Source alpha `(As, As, As, As)`                          |
| `InverseSourceAlpha`    | `1 - SourceAlpha`                                        |
| `DestinationAlpha`      | Destination alpha                                        |
| `InverseDestinationAlpha` | `1 - DestinationAlpha`                                 |
| `DestinationColor`      | Destination color                                        |
| `InverseDestinationColor` | `1 - DestinationColor`                                 |
| `BlendFactor`           | Constant blend color set on the pipeline                 |
| `InverseBlendFactor`    | `1 - BlendFactor`                                        |

**Common presets:**

| Intent                  | Command                                       |
|-------------------------|-----------------------------------------------|
| Alpha blending          | `Blend SourceAlpha InverseSourceAlpha`        |
| Additive blending       | `Blend One One`                               |
| Premultiplied alpha     | `Blend One InverseSourceAlpha`                |
| Multiply                | `Blend DestinationColor Zero`                 |

---

### Blend Equation — `BlendOp`

```
BlendOp Add | Subtract | ReverseSubtract | Minimum | Maximum
```

Sets the equation used to combine source and destination color values after applying blend factors.
Applies to both RGB and alpha channels.

| Value             | Formula                     | Description                        |
|-------------------|-----------------------------|------------------------------------|
| `Add`             | `Src + Dst`                 | Default additive blend             |
| `Subtract`        | `Src - Dst`                 | Subtracts destination from source  |
| `ReverseSubtract` | `Dst - Src`                 | Subtracts source from destination  |
| `Minimum`         | `min(Src, Dst)`             | Per-channel minimum                |
| `Maximum`         | `max(Src, Dst)`             | Per-channel maximum                |

---

### Color Write Mask — `ColorMask`

```
ColorMask RGBA | RGB | R | G | B | A | RG | ...
```

Specifies which color channels are written to the render target. Provide any combination of the
characters `R`, `G`, `B`, and `A`.

| Example Value | Channels Written          |
|---------------|---------------------------|
| `RGBA`        | All channels (default)    |
| `RGB`         | Red, Green, Blue only     |
| `A`           | Alpha only                |
| `RBA`         | Red, Blue, Alpha          |

---

### Polygon Offset — `Offset`

```
Offset <factor> <units>
```

Applies a depth bias to rendered polygons to avoid z-fighting (e.g. decals rendered on top of
another surface).

| Parameter | Type  | Description                                                                  |
|-----------|-------|------------------------------------------------------------------------------|
| `factor`  | float | Scales the maximum depth slope of the polygon                                |
| `units`   | float | Adds a constant depth offset in units of the smallest depth-buffer increment |

Example: `Offset -1 -1` pulls a surface slightly towards the camera.

---

### Alpha to Coverage — `AlphaToMask`

```
AlphaToMask On | Off
```

Enables alpha-to-coverage multisampling. The fragment alpha value is converted to a sample coverage
mask, producing anti-aliased transparency edges without sorting. Requires MSAA to be active.

| Value | Default |
|-------|---------|
| `Off` | ✓       |
| `On`  |         |

---

## Stencil Block

The `Stencil` block is a sub-block of a `Pass` that configures the stencil test. All commands
inside are optional; front- and back-face stencil operations can be configured independently.

The presence of a `Stencil` block enables the stencil test for the pass. Omit the block to leave
stencil testing disabled.

```
Stencil
{
    Ref       <int>
    ReadMask  <int>
    WriteMask <int>

    Comp      <StencilFunc>
    CompFront <StencilFunc>
    CompBack  <StencilFunc>

    Pass      <StencilOp>
    PassFront <StencilOp>
    PassBack  <StencilOp>

    Fail      <StencilOp>
    FailFront <StencilOp>
    FailBack  <StencilOp>

    ZFail      <StencilOp>
    ZFailFront <StencilOp>
    ZFailBack  <StencilOp>
}
```

Commands without a `Front`/`Back` suffix apply to both faces simultaneously.

### Reference & Mask Values

| Command     | Type | Description                                                             |
|-------------|------|-------------------------------------------------------------------------|
| `Ref`       | int  | Reference value compared against the stencil buffer                    |
| `ReadMask`  | int  | Bitmask ANDed with both the reference and stored value before comparing |
| `WriteMask` | int  | Bitmask controlling which stencil bits are written                      |

### Stencil Comparison Functions

Used by `Comp`, `CompFront`, `CompBack`.

| Value          | Passes when…                            |
|----------------|-----------------------------------------|
| `Never`        | Never                                   |
| `Less`         | `Ref & ReadMask < StencilValue & ReadMask` |
| `Equal`        | `Ref & ReadMask = StencilValue & ReadMask` |
| `LessEqual`    | `Ref & ReadMask ≤ StencilValue & ReadMask` |
| `Greater`      | `Ref & ReadMask > StencilValue & ReadMask` |
| `NotEqual`     | `Ref & ReadMask ≠ StencilValue & ReadMask` |
| `GreaterEqual` | `Ref & ReadMask ≥ StencilValue & ReadMask` |
| `Always`       | Always                                  |

### Stencil Operations

Used by `Pass`/`PassFront`/`PassBack`, `Fail`/`FailFront`/`FailBack`, and
`ZFail`/`ZFailFront`/`ZFailBack`.

| Operation      | Trigger                                              |
|----------------|------------------------------------------------------|
| `Pass`         | Stencil test passed and depth test passed            |
| `Fail`         | Stencil test failed                                  |
| `ZFail`        | Stencil test passed but depth test failed            |

| Value                | Description                                                 |
|----------------------|-------------------------------------------------------------|
| `Keep`               | Keep the current stencil value                              |
| `Zero`               | Set stencil value to 0                                      |
| `Replace`            | Replace with the `Ref` value                                |
| `IncrementAndClamp`  | Increment, clamping at the maximum value                    |
| `DecrementAndClamp`  | Decrement, clamping at 0                                    |
| `Invert`             | Bitwise invert                                              |
| `IncrementAndWrap`   | Increment, wrapping to 0 when past the maximum              |
| `DecrementAndWrap`   | Decrement, wrapping to the maximum value when below 0       |

---

## GLSLPROGRAM Block

Each pass must contain exactly one `GLSLPROGRAM ... ENDGLSL` block. Everything between the markers is
captured verbatim, the ShaderDef parser does not interpret it. The compiler reads it as a list of
sections:

```glsl
GLSLPROGRAM
Shared
{
    // prepended to every stage
}
Vertex
{
    in vec3 vertexPosition;
    out vec2 uv;
    void main() { ... }
}
Fragment
{
    in vec2 uv;
    out vec4 color;
    void main() { ... }
}
ENDGLSL
```

| Section    | Notes                                                                 |
|------------|-----------------------------------------------------------------------|
| `Shared`   | Optional. Prepended to every stage of the pass.                      |
| `Vertex`   | Required for a graphics pass.                                         |
| `Fragment` | Optional, a pass without one writes depth only.                       |
| `Compute`  | A compute pass, the only section besides `Shared`.                    |

| Element       | Required | Notes                                                    |
|---------------|----------|----------------------------------------------------------|
| `GLSLPROGRAM` | Yes      | Opening marker. Case-sensitive, must be uppercase.       |
| `ENDGLSL`     | Yes      | Closing marker. Case-sensitive, must be uppercase.       |

The code is plain GLSL in the OpenGL style. The compiler adds `#version 460` (a `#version` in the
source is an error) and turns it into Vulkan GLSL:

* Loose uniforms of every stage (`uniform vec4 _Color;`) are gathered into one std140 uniform block,
  reflected as `$Global`, with the same layout in every stage.
* Textures, samplers, images, uniform blocks and buffer blocks get descriptor bindings by name, the same
  in every stage. Explicit `layout(set = S, binding = B)` qualifiers are kept.
* Varyings get locations by name, matched between the vertex and fragment stage. Vertex inputs and
  fragment outputs get locations in declaration order. Explicit locations are kept.
* `gl_VertexID` and `gl_InstanceID` are mapped to their Vulkan forms.
* `GRAPHITE_VERTEX`, `GRAPHITE_FRAGMENT` or `GRAPHITE_COMPUTE` is defined for the stage being compiled.

`#include "Name"` resolves `Graphite/...` from the compiler's built in library and anything else through
the host. `#pragma once` is supported.

Variant axes are declared with pragmas anywhere in the program or its includes:

```glsl
#pragma variant _NORMALMAP                 // toggle, defines _NORMALMAP when on
#pragma variant _SURFACE OPAQUE CUTOUT     // enum, defines _SURFACE_OPAQUE or _SURFACE_CUTOUT
#pragma variant global FOG OFF LINEAR EXP  // set by the renderer or a view, never by a material
```

Compile errors report `file:line`, where the file is the shader name (and pass name) and the line is the
line of the `.shader` document, or the include's own name and line.

---

## Fallback

```
Fallback "FallbackShaderName"
```

Optional. Names an alternative shader to use when this shader cannot run on the current hardware or
render pipeline.

Graphite parses and stores the name on `ShaderDefinition.Fallback` but never resolves it. Resolving
the name to another shader is up to the engine.

---

## Diagnostics

Parsing reports errors with the source line and column. The following conditions are rejected
rather than silently accepted:

### Structure

| Condition                                  | Message                                       |
|--------------------------------------------|-----------------------------------------------|
| A `Pass` has no `GLSLPROGRAM` block        | `Each Pass must contain a GLSLPROGRAM block` |
| A `GLSLPROGRAM` block has no `ENDGLSL`      | `Unterminated GLSLPROGRAM block: missing closing 'ENDGLSL'` |
| A `Shader` declares no passes              | `Shader must contain at least one Pass`       |
| Content follows the shader's closing brace | `Unexpected content '...' after shader`       |

### Duplicates

Duplicate identifiers are errors; the diagnostic points at the second occurrence.

| Scope                              | Message                      |
|------------------------------------|------------------------------|
| Two tags share a key in one `Pass` | `Duplicate tag key '...'`    |
| Two properties share a name        | `Duplicate property '...'`   |
| Two passes share a `Name`          | `Duplicate pass name '...'`  |

Passes without a `Name` are never considered duplicates of one another.

### Commands

A render-state or `Stencil` command that is not recognized (for example a typo like `Culll`)
reports `Unknown command '...'` rather than failing later with a confusing message.

### Values

| Condition                                          | Message                                       |
|----------------------------------------------------|-----------------------------------------------|
| A number cannot be parsed (e.g. overflow, hex)     | `'...' is not a valid integer` / `number`     |
| A property default does not match its declared type| `<Type> property expects <shape>, but found '...'` |

> Because a `Vector` and a `Matrix` default both begin with `(`, supplying one where the other is
> expected is reported by the underlying value parser rather than as a property-shape mismatch.

---

## Full Example

```glsl
Shader "Example/PBR"
{
    Properties
    {
        _Albedo("Albedo", Color) = (1, 1, 1, 1)
        _Roughness("Roughness", Float) = 0.5
        _Metallic("Metallic", Float) = 0.0
        _AlbedoMap("Albedo Map", Texture2D) = "" {}
        _NormalMap("Normal Map", Texture2D) = "" {}
    }

    Pass 0
    {
        Name "ForwardLit"
        Tags { "LightMode" = "ForwardBase" }

        Cull Back
        ZTest LessEqual
        ZWrite On
        Blend SourceAlpha InverseSourceAlpha

        GLSLPROGRAM
        Vertex
        {
            void main() { ... }
        }
        Fragment
        {
            out vec4 color;
            void main() { ... }
        }
        ENDGLSL
    }

    Pass 1
    {
        Name "ShadowCaster"
        Tags { "LightMode" = "ShadowCaster" }

        Cull Front
        ZTest Less
        ZWrite On
        ColorMask R

        GLSLPROGRAM
        Vertex
        {
            void main() { ... }
        }
        ENDGLSL
    }

    Fallback "Hidden/InternalError"
}
```

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
| `Toggle(AXIS)`         | `Float`, `Integer` | The material toggle `AXIS` is on while the value is not zero           |
| `KeywordEnum(AXIS)`    | `Integer`          | The material axis `AXIS` takes the value at the integer's index        |
| `Enum(A, B, C)`        | `Integer`          | Shown as a dropdown of the names, stored as their index                |
| `Header("Text")`       | any                | A heading drawn above the property                                     |
| `Space`                | any                | A gap drawn above the property                                         |
| `ShowIf(AXIS)`         | any                | Shown only while the toggle `AXIS` is on; `ShowIf(AXIS, VALUE)` for an enum axis |
| `HideInInspector`      | any                | Not shown                                                              |
| `NoScaleOffset`        | textures           | No per texture scale and offset shown                                  |
| `Normal`               | textures           | Expects a tangent space normal map                                     |
| `HDR`                  | `Color`            | The colour may exceed 1                                                |

A material that sets an axis explicitly overrides what its properties drive.

---

## Surface Block

The quickest way to a custom lit or unlit shader. `Surface "Lit"` or `Surface "Unlit"` gives the shader Graphite's
standard Forward, Prepass and ShadowCaster passes with every feature of Graphite/Lit or Graphite/Unlit: shadows,
lightmaps, probes, decals, fog, surface types and their properties. The shader's own `Properties` are added to the
standard ones, replacing any of the same name.

The optional GLSLPROGRAM block holds fragment code defining `void Surface(inout Graphite_SurfaceData s)`, which may
change the surface before it is lit: `BaseColor`, `Alpha`, `Normal` (world space), `Metallic`, `Roughness`,
`Occlusion` and `Emission`, reading `UV`, `UV1`, `VertexColor`, `WorldPos` (camera relative), `ViewDir` and
`GeometryNormal`. The depth passes run it only for cutout surfaces, with the UVs, base colour and alpha filled in.

```
Shader "MyGame/Rim"
{
    Properties
    {
        _RimColor("Rim Color", Color) = (0.3, 0.6, 1.0, 1.0)
        _RimPower("Rim Power", Float) = 3.0
    }

    Surface "Lit"
    {
        GLSLPROGRAM
        uniform vec4 _RimColor;
        uniform float _RimPower;

        void Surface(inout Graphite_SurfaceData s)
        {
            float rim = 1.0 - max(dot(s.Normal, s.ViewDir), 0.0);
            s.Emission += _RimColor.rgb * pow(rim, _RimPower);
        }
        ENDGLSL
    }
}
```

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
| `LightMode`    | `Prepass`, `Forward`, `ShadowCaster`, `ObjectId`, `Sky`, any other name a feature draws | The pass type the renderer draws the pass as                            |
| `RenderQueue`  | an integer, 2500 and up transparent                           | The default render queue of the shader's materials                      |
| `SceneColor`   | `Read`                                                        | The pass samples the view's opaque colour through `Graphite/SceneColor.glsl`. A view whose transparent list holds such a pass copies its colour after the opaque surfaces and the sky, with mips, before drawing its transparent surfaces |

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

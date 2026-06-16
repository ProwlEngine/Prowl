# ShaderDef Shader Markup Specification

Graphite uses a ShaderLab-inspired declarative render state markup language known as ShaderDef
If that was a mouthful, it is a text description of how your CPU should set up your GPU to draw a shader.
It uses Slang shaders in conjunction with a custom ShaderLab-adjacent syntax to describe GPU render state, shader variants, and the programs to use.

---

## Table of Contents

- [File Structure](#file-structure)
- [Properties Block](#properties-block)
- [Pass Block](#pass-block)
  - [Name](#name)
  - [Tags](#tags)
  - [Render State Commands](#render-state-commands)
  - [Stencil Block](#stencil-block)
  - [ShaderSource Block](#shadersource-block)
    - [Entrypoints](#entrypoints)
- [Fallback](#fallback)
- [Full Example](#full-example)

---

## File Structure

```
Shader "ShaderName"
{
    Properties { ... }      // optional

    Pass [index]            // one or more
    {
        ...
    }

    Fallback "FallbackShaderName"
}
```

| Element          | Required | Notes                                              |
|------------------|----------|----------------------------------------------------|
| `Shader "Name"`  | Yes      | Quoted display name for the shader                 |
| `Properties`     | No       | Exposes default material parameters to the library |
| `Pass`           | Yes (≥1) | One block per rendering pass                       |
| `Fallback`       | Yes      | Name of a fallback shader if this one fails        |

---

## Properties Block

The `Properties` block declares named default parameters that can be set on a material at runtime.

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

    ShaderSource "SourceFile"
    {
        ...
    }
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
render pipeline; the shader system stores them but does not interpret them.

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

Sets the depth comparison function used to determine whether a fragment passes the depth test.

| Value          | Description                                          | Default |
|----------------|------------------------------------------------------|---------|
| `Disabled`     | Depth testing is turned off entirely                 |         |
| `Never`        | Never passes                                         |         |
| `Less`         | Passes if fragment depth < stored depth              |         |
| `Equal`        | Passes if fragment depth = stored depth              |         |
| `LessEqual`    | Passes if fragment depth ≤ stored depth              | ✓       |
| `Greater`      | Passes if fragment depth > stored depth              |         |
| `NotEqual`     | Passes if fragment depth ≠ stored depth              |         |
| `GreaterEqual` | Passes if fragment depth ≥ stored depth              |         |
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

| Value                  | Description                                              |
|------------------------|----------------------------------------------------------|
| `Zero`                 | Factor is `(0, 0, 0, 0)`                                 |
| `One`                  | Factor is `(1, 1, 1, 1)`                                 |
| `SrcColor`             | Source color `(Rs, Gs, Bs, As)`                          |
| `OneMinusSrcColor`     | `1 - SrcColor`                                           |
| `SrcAlpha`             | Source alpha `(As, As, As, As)`                          |
| `OneMinusSrcAlpha`     | `1 - SrcAlpha`                                           |
| `DstAlpha`             | Destination alpha                                        |
| `OneMinusDstAlpha`     | `1 - DstAlpha`                                           |
| `DstColor`             | Destination color                                        |
| `OneMinusDstColor`     | `1 - DstColor`                                           |
| `SrcAlphaSaturate`     | `min(As, 1 - Ad)` — saturated source alpha               |
| `ConstantColor`        | Constant blend color                                     |
| `OneMinusConstantColor`| `1 - ConstantColor`                                      |
| `ConstantAlpha`        | Constant blend alpha                                     |
| `OneMinusConstantAlpha`| `1 - ConstantAlpha`                                      |
| `Src1Color`            | Second source color (dual-source blending)               |
| `OneMinusSrc1Color`    | `1 - Src1Color`                                          |
| `Src1Alpha`            | Second source alpha (dual-source blending)               |
| `OneMinusSrc1Alpha`    | `1 - Src1Alpha`                                          |

**Common presets:**

| Intent                  | Command                               |
|-------------------------|---------------------------------------|
| Alpha blending          | `Blend SrcAlpha OneMinusSrcAlpha`     |
| Additive blending       | `Blend One One`                       |
| Premultiplied alpha     | `Blend One OneMinusSrcAlpha`          |
| Multiply                | `Blend DstColor Zero`                 |

---

### Blend Equation — `BlendOp`

```
BlendOp Add | Subtract | ReverseSubtract | Min | Max
```

Sets the equation used to combine source and destination color values after applying blend factors.
Applies to both RGB and alpha channels.

| Value             | Formula                     | Description                        |
|-------------------|-----------------------------|------------------------------------|
| `Add`             | `Src + Dst`                 | Default additive blend             |
| `Subtract`        | `Src - Dst`                 | Subtracts destination from source  |
| `ReverseSubtract` | `Dst - Src`                 | Subtracts source from destination  |
| `Min`             | `min(Src, Dst)`             | Per-channel minimum                |
| `Max`             | `max(Src, Dst)`             | Per-channel maximum                |

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
Offset <factor>, <units>
```

Applies a depth bias to rendered polygons to avoid z-fighting (e.g. decals rendered on top of
another surface).

| Parameter | Type  | Description                                                                  |
|-----------|-------|------------------------------------------------------------------------------|
| `factor`  | float | Scales the maximum depth slope of the polygon                                |
| `units`   | float | Adds a constant depth offset in units of the smallest depth-buffer increment |

Example: `Offset -1, -1` pulls a surface slightly towards the camera.

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

| Value           | Description                                                 |
|-----------------|-------------------------------------------------------------|
| `Keep`          | Keep the current stencil value                              |
| `Zero`          | Set stencil value to 0                                      |
| `Replace`       | Replace with the `Ref` value                                |
| `Increment`     | Increment, clamping at the maximum value                    |
| `Decrement`     | Decrement, clamping at 0                                    |
| `Invert`        | Bitwise invert                                              |
| `IncrementWrap` | Increment, wrapping to 0 when past the maximum              |
| `DecrementWrap` | Decrement, wrapping to the maximum value when below 0       |

---

## ShaderSource Block

Each pass must contain exactly one `ShaderSource` block, which identifies the source `.slang` file to use
as the shader for the pass. This is to allow re-use of logic between multiple `.slang` files, with different
entrypoints contained in the same module being able to be used across different `Pass` blocks
and `.shaderdef` definitions.

```
ShaderSource "VertexShader.slang"
{
    Vertex "vertexEntrypoint"
    Fragment "fragmentEntrypoint"
}
```

| Element                   | Required | Notes                                                |
|---------------------------|----------|------------------------------------------------------|
| `ShaderSource "Filename"` | Yes      | Quoted source file to load                           |
| `Vertex "Entrypoint"`     | Yes      | The module entrypoint to use for the Vertex stage    |
| `Fragment "EntryPoint"`   | Yes      | The module entrypoint to use for the Fragment stage  |

### Entrypoints

Entrypoint commands describe what entry point to use for a shader stage from within the `ShaderSource` block.

| Command                        | Description                                                       |
|--------------------------------|-------------------------------------------------------------------|
| `Vertex "Name"`                | The Slang module function to use for the vertex shader stage      |
| `Fragment "Name"`              | The Slang module function to use for the fragment shader stage    |
| `Mesh "Name"`                  | The Slang module function to use for the mesh shader stage        |
| `Task "Name"`                  | The Slang module function to use for the task shader stage        |
| `Geometry "Name"`              | The Slang module function to use for the geometry shader stage    |
| `Tesselation "Name"`           | The Slang module function to use for the tesselation shader stage |

---

## Fallback

```
Fallback "FallbackShaderName"
```

Specifies the name of an alternative shader to use when this shader cannot run on the current
hardware or render pipeline. The library resolves the fallback by name.

---

## Full Example

```hlsl
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
        Blend SrcAlpha OneMinusSrcAlpha

        ShaderSource "PBR.slang"
        {
            Vertex "PBRVertex"
            Fragment "PBRFragment"
        }
    }

    Pass 1
    {
        Name "ShadowCaster"
        Tags { "LightMode" = "ShadowCaster" }

        Cull Front
        ZTest Less
        ZWrite On
        ColorMask R

        ShaderSource "PBR.slang"
        {
            Vertex "PBRShadowVertex"
            Fragment "PBRShadowFragment"
        }
    }

    Fallback "Hidden/InternalError"
}
```

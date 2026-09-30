# Shader File Format

Source: [`AssetImporting/ShaderParser.cs`](../../Prowl.Runtime/AssetImporting/ShaderParser.cs),
[`Rendering/Shaders/ShaderPass.cs`](../../Prowl.Runtime/Rendering/Shaders/ShaderPass.cs),
[`Rendering/Shaders/ShaderProperty.cs`](../../Prowl.Runtime/Rendering/Shaders/ShaderProperty.cs),
[`Rendering/Shaders/KeywordState.cs`](../../Prowl.Runtime/Rendering/Shaders/KeywordState.cs),
[`Rendering/Shaders/BlendStateDescription.cs`](../../Prowl.Runtime/Rendering/Shaders/BlendStateDescription.cs).
The `Shader`/`Material` resource classes are core and not in the snapshot.

The shader asset format is core infrastructure that stays after the pipeline is removed; it is documented here because
every pipeline feature is expressed through it (tags, passes, keywords).

## Grammar

```
Shader "Category/Name"

Properties
{
    _Name ("Display Name", Type) = default
    ...
}

Pass "PassName"
{
    Tags { "Key" = "Value", "Key2" = "Value+100" }
    Blend Alpha | Additive | Override | Off | { Src SrcAlpha Dst OneMinusSrcAlpha Mode Add }
    Cull Back | Front | Off | None
    ZTest Off | LEqual | Always | ...
    ZWrite On | Off
    Winding ...
    Stencil { Ref 1 Comp Equal Pass Replace Fail Keep ZFail Keep ReadMask 255 WriteMask 255 }
    GrabTexture "_GrabTexture"
    GrabDepth "_GrabDepth"

    GLSLPROGRAM
        Shared   { ...code prepended to both stages... }
        Vertex   { ... }
        Fragment { ... }
    ENDGLSL
}

Fallback "Other/Shader"
```

- Comments and BOMs are stripped before tokenizing; **all non-ASCII characters in the source are replaced by spaces**
  (GLSL compilers reject them).
- Property types: `Float`, `Int`, `Vector2/3/4`, `Color`, `Matrix`, `Texture2D`, `Texture3D`, and `Range(min, max)`
  (a Float with an inspector slider hint). Texture defaults are names of default textures (`"white"`, `"normal"`,
  `"surface"`, `"emission"`, `"black"`, ...).
- `Tags` values may carry a signed integer sort offset (`"Transparent+1000"`). The parser splits it into the tag value
  and a per-tag sort offset used by batch sorting.
- `Shared`/`Vertex`/`Fragment` are found with a regex plus balanced-brace matching (string-literal aware). Vertex and
  fragment sources are `shared + stage`.
- `Fallback` is stored per pass but not used at runtime by the pipeline (compile failures fall back to `Hidden/Invalid`).

## Includes

`#include "Name"` resolves to `Name.glsl` next to the including shader (embedded resources for defaults, filesystem or a
custom resolver otherwise), recursively, with includes pasted inline at import time. Include guards (`#ifndef`) are the
shader author's job. `ShaderPass.VertexSource` holds the fully preprocessed text.

## Tags the pipeline understands

| Tag | Values | Consumer |
|---|---|---|
| `RenderOrder` | `Opaque`, `Transparent`, `UI` | opaque pass, transparent pass, UI passes |
| `LightMode` | `Prepass`, `ShadowCaster` | unified prepass, shadow atlas |
| `RenderType` | `Opaque` | declared by `Hidden/Invalid`, not read |

A pass with no matching tag is never drawn by the scene passes; image-effect passes are drawn explicitly by index via
`Blit`, so their tags do not matter.

## Variants

`ShaderPass.TryGetVariantProgram(Dictionary<string,bool> keywords, out program)`:

1. Build the key: every enabled keyword name followed by `;`, in dictionary order, on the stack; look up with a span
   (no allocation on the hot path).
2. Miss: prepend `#define KEYWORD` for each enabled keyword, `#define FRAGMENT_VERSION 1`, and `#version 410`, then
   `Graphics.CompileProgram(frag, vert, null)`.
3. Compile failure: log and use pass 0 of `Hidden/Invalid` (magenta) instead; cache that under the key.

There is no keyword declaration list: any keyword can be toggled at runtime and compiles a new variant lazily
(a first-use hitch). Keywords come from two places:
- material keywords (`Material.SetKeyword`, e.g. `TONEMAP_AGX`, `AUTOFOCUS`, `TERRAIN_8_LAYERS`);
- mesh-derived keywords set by the pipeline per batch (`HAS_*`, `SKINNED`, `BLENDSHAPES`, `GPU_INSTANCING`).

`KeywordState` is a serializable orderless-hashed key/value keyword set (older API, still used by materials).

## Render state

`RasterizerState` per pass: depth test/write/func, blend enable/src/dst/mode (+ presets `Alpha`, `Additive`,
`Override`), cull face, winding, stencil ref/func/ops/masks. `BlendStateDescription.cs` holds an older
`BlendDescription` struct with presets (`OverrideBlend`, `AlphaBlend`, `AdditiveBlend`, `Disabled`).

## Rebuild notes

- GLSL 410 is the only target. `layout(binding=)` is conditional on 420+.
- Variant explosion is unbounded because keywords are free-form. A rebuild should declare variant axes.
- Stage separation relies on `#ifdef PROWL_VERTEX_STAGE / PROWL_FRAGMENT_STAGE` inside shared includes
  (see [Standard shader family](standard-shader-family.md)).

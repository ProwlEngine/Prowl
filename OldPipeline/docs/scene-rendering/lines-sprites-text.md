# Lines, Sprites, Text Meshes

## LineRenderer

Source: [`Components/LineRenderer.cs`](../../Prowl.Runtime/Components/LineRenderer.cs),
[`Line.shader`](../../Prowl.Runtime/Assets/Defaults/Line.shader).

Fields: `Material`, `StartWidth` / `EndWidth` 0.1, `Points`, `Loop`, `StartColor` / `EndColor`, `TextureMode`
(`Stretch` / `Tile`), `TextureTiling`, `RecalculateNormals`.

Implements `IRenderable` itself and adds itself in `OnRenderCollect`. `GetRenderingData(viewer, ...)` rebuilds a
camera-facing ribbon into a cached mesh **for each call** (each viewer, each pass): points to world space, optional loop
point, per-segment perpendicular = `cross(direction, toCamera)` (falls back to camera up when the segment points at the
camera), width interpolated along the line, U by accumulated length (tile) or normalized (stretch), vertex colors lerped
start to end, two triangles per segment. Properties: `_ObjectID`, `_StartColor`, `_EndColor`.

`Line.shader` is a leftover from a deferred pipeline: it writes four MRT outputs (albedo, motion with jitter removed,
normal, surface) and calls `ApplyFog(float, vec3)` without including `Lighting`, which does not exist in the current
includes. See [known issues](../reference/known-issues.md).

## SpriteRenderer

Source: [`Components/SpriteRenderer.cs`](../../Prowl.Runtime/Components/SpriteRenderer.cs),
[`Sprite.shader`](../../Prowl.Runtime/Assets/Defaults/Sprite.shader).

Fields: `Sprite`, `Color`, `FlipX`, `FlipY`, `Material` (default `Default/Sprite` material), `SortingOrder`.
Builds a quad mesh for the sprite rect (rebuilt when the sprite changes), binds `_MainTex` and every
`Sprite.SecondaryTextures` entry by name (for custom sprite materials, e.g. `_NormalMap`). `SortingOrder` is emulated by
translating the world matrix along local Z by `SortingOrder * SortBias` (it biases the distance sort, there is no
sorting layer system). Emits a `MeshRenderable`.

Shader: `RenderOrder=Transparent`, `Blend Alpha`, `Cull Off`, `ZWrite Off`; `texture * vColor * _MainColor`, sRGB
decode, `ApplyFog`. Unlit.

## TextMeshComponent

Source: [`Components/TextMeshComponent.cs`](../../Prowl.Runtime/Components/TextMeshComponent.cs),
[`DefaultTextMesh.shader`](../../Prowl.Runtime/Assets/Defaults/DefaultTextMesh.shader).

World-space text. Properties: `Font` (FontAsset, falls back to a default), `Text`, `TextColor`, `Quality`, `Size`,
`PixelsPerUnit`, `MaxWidth` (wrapping), `Anchor`, `Material` (shared `UI/Text Mesh` material by default). Rebuilds its
glyph mesh when dirty (setters mark dirty), binds the font atlas as `_MainTex`, `_MainColor` white, `_ObjectID`, and
emits a `MeshRenderable` with `LocalToWorld`. Shader is transparent, alpha blended, `Cull Off`, `ZWrite Off`.

## Rebuild notes

- LineRenderer regenerating geometry inside `GetRenderingData` means shadow passes and every camera rebuild it; a
  rebuild should expand lines in the vertex shader.
- Line.shader needs a rewrite before use.

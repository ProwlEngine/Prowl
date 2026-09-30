# Property Sets API

Reference for `PropertySet` and `PropertyID`: the named bag of uniforms, textures, samplers and buffers you hand to a draw.

- [Overview](#overview)
- [Quick example](#quick-example)
- [PropertyID](#propertyid)
- [PropertySet](#propertyset)
- [Binding semantics](#binding-semantics)
- [Common patterns](#common-patterns)
- [Pitfalls](#pitfalls)
- [See also](#see-also)

## Overview

Shaders in Graphite are fed by name. You create a `PropertySet`, write values under the same names the shader declares, and call `CommandBuffer.SetProperties(set)` before drawing. There are no slot numbers, descriptor sets or register indices in your code. The backend matches names to the shader's reflected layout at draw time.

## Quick example

From [TexturedQuad](../../Samples/TexturedQuad/Program.cs#L26) and [PBRRenderer](../../Samples/PBRRenderer/Program.cs#L314):

```csharp
PropertySet sceneProperties = new();
sceneProperties.SetTexture("AlbedoTexture", albedo, device.LinearSampler);
sceneProperties.SetFloat4("BaseColor", new Float4(1, 1, 1, 1));

CommandBuffer cmd = context.GetCommandBuffer("Scene");
cmd.SetShader(shader);
cmd.SetVertexSource(mesh);
cmd.SetProperties(sceneProperties);
cmd.DrawIndexed();
context.SubmitCommandBuffer(cmd);
```

Per-frame values are written into the same set before recording:

```csharp
sceneProperties.SetMatrix("MatrixMVP", projection * view);
```

## PropertyID

An interned string as a cheap integer. Source: [PropertyID.cs](../../Graphite/Core/Identifiers/PropertyID.cs#L10).

| Member | Signature | Description |
|--------|-----------|-------------|
| `Intern` | `static PropertyID Intern(string name)` | Returns the existing ID for the name or mints a new one. Thread-safe. |
| `ToString` | `static string? ToString(PropertyID id)` | Reverse lookup; null if the ID was never interned |
| implicit conversion | `static implicit operator PropertyID(string name)` | Calls `Intern`, so string literals work everywhere an ID is expected |
| `IsValid` | `bool IsValid { get; }` | False for `default` |
| equality | `==`, `!=`, `Equals` | Compares the underlying int |

Instance `ToString()` prints `ResourceID(n)`, not the name, and is safe on hot paths. The static `ToString(id)` returns the name.

Passing a string literal interns on every call. A cached ID avoids the lookup:

```csharp
private static readonly PropertyID MatrixMvp = PropertyID.Intern("MatrixMVP");

_properties.SetMatrix(MatrixMvp, projection * view);
```

## PropertySet

`sealed partial class PropertySet`. Not thread-safe. Last write to a name wins. Source: [PropertySet.cs](../../Graphite/Core/Binding/PropertySet.cs#L13).

### Construction and state

| Member | Signature | Description |
|--------|-----------|-------------|
| constructor | `PropertySet()` / `PropertySet(int initialEntryCapacity)` | Empty set, optionally presized |
| `EntryCount` | `int EntryCount { get; }` | Number of named entries |
| `ResourceVersion` | `uint ResourceVersion { get; }` | Increments on resource setters (buffer, texture, sampler), `Clear`, and merges that bring in resources. Uniform writes do not change it. |
| `Clear` | `void Clear()` | Removes every entry |
| `ApplyOther` | `void ApplyOther(PropertySet other)` | Copies all of `other`'s entries into this set, overwriting matching names |

### Uniform setters

All take `(PropertyID name, value)` and return `void`. They write into the named entry; the entry's kind becomes uniform.

| Member | Value type |
|--------|------------|
| `SetFloat` | `float` |
| `SetFloat2` / `SetFloat3` / `SetFloat4` | `Float2` / `Float3` / `Float4` |
| `SetInt` | `int` |
| `SetInt2` / `SetInt3` / `SetInt4` | `Int2` / `Int3` / `Int4` |
| `SetDouble` | `double` |
| `SetDouble2` / `SetDouble3` / `SetDouble4` | `Double2` / `Double3` / `Double4` |
| `SetMatrix` | `Float4x4` |
| `SetDoubleMatrix` | `Double4x4` |

### Resource setters

| Member | Signature | Description |
|--------|-----------|-------------|
| `SetTexture` | `void SetTexture(PropertyID name, Texture texture, Sampler? sampler = null)` | Binds a whole texture |
| `SetTexture` | `void SetTexture(PropertyID name, TextureView view, Sampler? sampler = null)` | Binds a specific view (mip range, layer range) |
| `SetTexture` | `void SetTexture(PropertyID name, RenderTexture renderTexture, Sampler? sampler = null)` | Binds the render texture's first color attachment |
| `SetSampler` | `void SetSampler(PropertyID name, Sampler sampler)` | Binds a sampler under its own name, independent of any texture |
| `SetBuffer` | `void SetBuffer(PropertyID name, DeviceBuffer buffer, bool readOnly = true)` | Binds a whole buffer |
| `SetBuffer` | `void SetBuffer(PropertyID name, DeviceBufferRange range, bool readOnly = true)` | Binds a sub-range of a buffer |

Null arguments throw. The `sampler` parameter of `SetTexture` is optional; with `null` the shader gets the device's default linear sampler.

### Applying to a command buffer

```csharp
cmd.SetProperties(set);
cmd.ClearProperties();
```

See [command-buffers.md](command-buffers.md) for the full state API.

## Binding semantics

### Names and kinds

An element in the shader binds to the entry with the same name and the right kind:

| Shader element | Entry you wrote |
|----------------|-----------------|
| Texture (read-only or read-write / storage image) | `SetTexture` |
| Sampler | `SetSampler`, or the sampler argument of a same-named `SetTexture` |
| Structured buffer | `SetBuffer` |
| Uniform buffer | `SetBuffer`, and/or loose `SetFloat`/`SetMatrix`/... values |
| Loose uniform fields (plain globals in the shader) | Uniform setters by field name |

A name written with the wrong kind is treated as missing.

### Sampler resolution

For a sampler the backend checks, in order: an entry written with `SetSampler` under the sampler's name; the sampler attached to a same-named texture entry; the device default linear sampler. Combined image sampler elements use the same order.

### Loose uniforms versus explicit uniform buffers

If the shader declares loose uniform fields, their values are packed by the backend from your uniform entries, so most code never creates a uniform buffer. The packed block is uploaded to transient per-execution memory. If you also bind a buffer under the block's name with `readOnly: false`, the values are written into your buffer instead and your range is bound. With `readOnly: true` the buffer is bound as is and scalar writes are ignored.

### Layering

`SetProperties` merges. Apply a shared set first, then a per-object set; the second overrides names the first defined and the rest stay bound:

```csharp
cmd.SetProperties(frameProperties);
cmd.SetProperties(materialProperties);
```

### Change detection

The command buffer skips re-applying the same set object if nothing in it changed since the last `SetProperties` of that set. Each uniform entry carries its own version so the backend repacks a uniform block only when a value changed. This makes it cheap to keep one long-lived set per material and rewrite uniforms every frame.

## Common patterns

### One set per material, one per frame

```csharp
frame.SetMatrix("ViewProjection", viewProjection);
frame.SetFloat3("CameraPosition", cameraPosition);

foreach (Item item in items)
{
    cmd.SetProperties(frame);
    cmd.SetProperties(item.Material);
    cmd.SetVertexSource(item.Mesh);
    cmd.DrawIndexed();
}
```

### Switching textures between draws

Three quads with different textures share one shader and only swap sets ([TexturedQuad](../../Samples/TexturedQuad/Program.cs#L51)):

```csharp
cmd.SetShader(_shader);

cmd.SetProperties(_leftProperties);
cmd.SetVertexSource(_leftQuad);
cmd.DrawIndexed();

cmd.SetProperties(_rightProperties);
cmd.SetVertexSource(_rightQuad);
cmd.DrawIndexed();
```

### Per-object uniforms in one set

[CubeGrid](../../Samples/CubeGrid/CubeGrid.cs#L89) keeps a `PropertySet` per cube with the texture set once, then rewrites the matrix each frame:

```csharp
cube.Properties.SetMatrix("MatrixMVP", mvp);
cmd.SetProperties(cube.Properties);
cmd.DrawIndexed();
```

Every distinct uniform value in a draw gets its own packed range, but the texture descriptor set is reused since its bound resources did not change.

### Sampling a graph texture

```csharp
RenderTexture scene = context.GetRenderTexture(_sceneHandle);
_properties.SetTexture("sceneTexture", scene.ColorTextures[0], _sampler);
```

Or pass the `RenderTexture` itself to get its first color attachment.

### Finding misspelled names

`GraphicsDevice.OnMissingProperty` reports misspelled names. It fires when a shader element has no matching entry and a default (null texture, empty buffer, zeroed block) was substituted. It is null, and therefore silent, by default.

## Pitfalls

- `PropertySet` is not thread-safe.
- Names are case-sensitive strings; a typo binds a default silently unless `OnMissingProperty` is set.
- Setting a texture under a name that held a uniform converts the entry. The old value is gone.
- The merge shares entry objects between your set and the command buffer's table. Rewriting a uniform after `SetProperties` and before the next draw in the same command buffer affects that next draw.
- Uniform payload per entry is 128 bytes, the size of a `Double4x4`. Large arrays belong in buffers.
- `SetTexture` with a `RenderTexture` binds only color attachment 0. Other attachments of an MRT target bind from `ColorTextures[i]`.
- `ClearProperties` only clears the command buffer's merged table, not your `PropertySet`. Beginning a command buffer also clears it, so rented buffers always start empty.
- Interning a string literal on every call costs a dictionary lookup.

## See also

- [internals/04-resource-binding.md](../internals/04-resource-binding.md) - how sets become descriptor sets
- [command-buffers.md](command-buffers.md) - `SetProperties`, `ClearProperties`, draws
- [buffers-and-textures.md](buffers-and-textures.md) - `Texture`, `Sampler`, `DeviceBuffer`
- [shader-programs.md](shader-programs.md) - where the names come from
- [render-graph.md](render-graph.md)

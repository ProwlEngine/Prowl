# Prowl.Veldrid

A cut up version of ciberman's NeoVeldrid repository -

The previous Pipeline API has been gutted in favor of a monolithic `ShaderProgram` object, which encapsulates pipeline data slightly differently.

Conceptually, `ShaderProgram` and `Pipeline` are very similar in
behavior, however there are a few key differences:

1. `ShaderProgram` compiles per-platform shaders itself. This API tradeoff was selected due to how this library is intended to be used in Prowl.
This means `Shader` objects can not be compiled seperately to `Pipeline` objects, or reused. This principle was decided on because Prowl's shader markdown syntax directly couples pipeline state with shader state. Prowl only has an extra axis of differing compiled Variants, which need to be compiled regardless, therefore having decoupled shaders and pipeline states *did not benefit Prowl in any way* and so were removed!

2. `PrimitiveTopology` and `OutputDescription` have been divorced from pipelines/shader programs in favor of simplicity. In most renderers, pipelines are already being cached and indexed based on their output description. The Vulkan backend is the only one which benefits from having the output description bundled with the pipeline. In this case, the OutputDescription is saved on the Command Buffer and used to index an internal Vk
pipeline cache which handles indexing cached pipelines from a combination of `ShaderProgram`, `PrimitiveTopology`, and `OutputDescription`.
When a `ShaderProgram` is disposed, its relevant internally cached pipelines are disposed alongside it.

`CommandList` has been renamed to `CommandBuffer` since we're shamelessly attempting to copy Unity's API and want to reduce resistance in transferring over.

`CommandBuffer.SetPipeline` has been replaced with `CommandBuffer.SetShader`. Conceptually the same API.

`CommandBuffer.SetVertexBuffer/IndexBuffer` has been replaced with `CommandBuffer.SetVertexSource`. A new `IVertexSource` interface has been added, which provides an interface for a resolver-architecture where a bound shader program can request for buffers at a given location.

The API looks like so, and is intended to provide a balance between Unity's mesh-style binding system and a flexible binding API for lower-level users:

```cs
public interface IVertexSource
{
    // Provides the draw topology this source wants. Reasoning: coupled with vertex data as  topology directly influences index counts.
    PrimitiveTopology Topology { get; }

    // Resolves a device buffer slot. Layout slot is the index in the created shader's vertex inputs
    // layout is the source layout description used by the shader, if you potentially want to bind your vertex data by name.
    // VertexBinding is a union of the resolved DeviceBuffer and the offset in the buffer to use.
    void ResolveSlot(uint layoutSlot, in VertexLayoutDescription layout, out VertexBinding binding);

    // Resolves an index buffer slot. Can return false if no index buffer is available.
    // Provides format and offset data.
    bool TryGetIndexBuffer(out DeviceBuffer buffer, out IndexFormat format, out uint offset);
}
```

This component is part of the Prowl Game Engine and is licensed under the MIT License. See the LICENSE file in the project root for details.

Thank you to the creators of [Veldrid](https://github.com/veldrid/veldrid) and [NeoVeldrid](https://github.com/jhm-ciberman/neo-veldrid) for being unaware of what I'm doing to your libraries.

Prowl.Veldrid has had radical filesystem and API changes to upstream Veldrid/NeoVeldrid. As such, changes and fixes from NeoVeldrid cannot be easily merged and will land in the commit history with the prefix `(NeoVeldrid)` with the same commit name, but altered file paths, locations, and logic. If any of the original contributors would like more/different credit for any of their work, or would like me to stop sourcing from their commits, please contact me!

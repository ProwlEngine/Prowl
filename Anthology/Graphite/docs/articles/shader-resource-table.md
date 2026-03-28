---
uid: shader-resource-table
---

# Shader Resource Mapping Table

When authoring shader code, you need to keep in mind the types of resources you define. When building a [ResourceLayoutDescription](xref:Veldrid.ResourceLayoutDescription), the type of each element needs to match what you have defined in your shader code. The table below maps each supported ResourceKind in Veldrid to the corresponding type of resource in each shading language.

| [ResourceKind](xref:Veldrid.ResourceKind) | GLSL Type | HLSL Type | MSL Type |
|------ |------|------|---------|
| UniformBuffer | uniform block | cbuffer | constant T& variable |
| StructuredBufferReadOnly | readonly buffer block | StructuredBuffer<T>/ByteAddressBuffer* | device T* variable |
| StructuredBufferReadWrite | buffer block | RWStructuredBuffer<T>/RWByteAddressBuffer* | device T* variable |
| TextureReadOnly | sampler(2D,3D) (OpenGL), texture(2D,3D) (Vulkan) | Texture(2D,3D) | texture(2d,3d) with with access::sample or access::read |
| TextureReadWrite | image(2D,3D) | RWTexture(2D,3D) | texture(2d,3d) with access::read_write |
| Sampler | sampler (Vulkan), Not supported in OpenGL, see [Shaders](xref:shaders-and-resources#sampler) | SamplerState | sampler |

* (RW)ByteAddressBuffer can only be used with a DeviceBuffer that was created with [BufferDescription.RawBuffer](xref:Veldrid.BufferDescription#Veldrid_BufferDescription_RawBuffer) set to true.
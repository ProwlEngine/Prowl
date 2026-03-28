---
uid: shaders-and-resources
---

# Shaders and Resources

[Shaders](xref:Veldrid.Shader) are a fundamental device resource in Veldrid. Each Shader object represents a single shader module created from specialized shader code. Shader objects are combined into shader sets which are one input into a [Pipeline](xref:Veldrid.Pipeline). Shaders can read from and write to several other kinds of device resources while executing.

## Creating a Shader

A [ShaderDescription](xref:Veldrid.ShaderDescription) takes two pieces of information. The first piece is the [stage](xref:Veldrid.ShaderStages) the shader is applicable to. The second piece of information is an API-specific byte array containing the shader code itself. The contents of this byte array depend on the specific graphics API being used (see [GraphicsDevice.BackendType](xref:Veldrid.GraphicsDevice#Veldrid_GraphicsDevice_BackendType)).

* Direct3D11: [ShaderBytes](xref:Veldrid.ShaderDescription#Veldrid_ShaderDescription_ShaderBytes) must contain HLSL bytecode or ASCII-encoded HLSL text (Shader Model 5).
* Vulkan: [ShaderBytes](xref:Veldrid.ShaderDescription#Veldrid_ShaderDescription_ShaderBytes) must contain SPIR-V bytecode.
* OpenGL: [ShaderBytes](xref:Veldrid.ShaderDescription#Veldrid_ShaderDescription_ShaderBytes) must contain ASCII-encoded GLSL text.
* OpenGL ES: [ShaderBytes](xref:Veldrid.ShaderDescription#Veldrid_ShaderDescription_ShaderBytes) must contain ASCII-encoded GLSL ES text.
* Metal: [ShaderBytes](xref:Veldrid.ShaderDescription#Veldrid_ShaderDescription_ShaderBytes) must contain UTF8-encoded Metal text or Metal bytecode (a "metallib" file created using the `metallib` tool). NOTE: Metal bytecode is platform-specific -- it is compiled differently for macOS and iOS, and the two versions cannot be used interchangeably. You must provide the correct version for the platform you are targetting, or provide UTF8 Metal code to be compiled on-device at runtime.

## Writing Portable Shaders

In most cases, you will want to write your shader code only once, in a single language, and to use some form of cross-compilation or translation to automatically generate the other shader languages. The [Veldrid.SPIRV](https://github.com/mellinoe/veldrid-spirv) library provides support for using SPIR-V bytecode in all Veldrid backends using [SPIRV-Cross](https://github.com/KhronosGroup/SPIRV-Cross), and is the recommended portable shader solution. See the [Portable Shaders](xref:portable-shaders) article for more options and information.

## Specialization Constants

Veldrid 4.4.0 introduces the concept of "Specialization Constants", which enable you to create Shaders with parameterized behavior that can be "specialized" when a [Pipeline](xref:Veldrid.Pipeline) is created, with no runtime overhead. See the [Specialization Constants](xref:specialization-constants) article for more information.

## Shader Resources

Shader objects have a unique relationship with [ResourceLayouts](xref:Veldrid.ResourceLayout) and [ResourceSets](xref:Veldrid.ResourceSet). When creating a Pipeline, the provided [ResourceLayouts](xref:Veldrid.GraphicsPipelineDescription#Veldrid_GraphicsPipelineDescription_ResourceLayouts) must match the actual resource types that are specified in the shader code.

ResourceLayouts merely define the layout of resources expected by a set of shaders. When draw commands are executed, the actual shader resources (DeviceBuffers, TextureViews, and Samplers) are determined based on the [CommandList's](xref:command-lists) currently-bound [ResourceSets](xref:Veldrid.ResourceSet).

Each ResourceLayout contains a set of one-or-more resource elements. Additionally, multiple ResourceLayout objects can be used to define the inputs of a shader set. When this is done, each ResourceLayout must be matched by a corresponding ResourceSet containing the appropriate resource types. ResourceSet objects are bound to a particular slot on a [CommandList](xref:command-lists). The slot corresponds to the index of the matching ResourceLayout given in the [ResourceLayouts](xref:Veldrid.GraphicsPipelineDescription#Veldrid_GraphicsPipelineDescription_ResourceLayouts) array of the [GraphicsPipelineDescription](xref:Veldrid.GraphicsPipelineDescription) or [ComputePipelineDescription](xref:Veldrid.ComputePipelineDescription).

Generally, it is advisable to group resources into sets and layouts which are common or shared. For example, it is a good idea to group the camera's view and projection matrix, and other scene-level information, into a single ResourceLayout and shared ResourceSet. This allows many objects to be rendered using the same bound ResourceSet. Specific object Pipelines can utilize extra ResourceLayouts and ResourceSets to accomodate their specific rendering requirements while still utilizing shared resources when it makes sense. Changing ResourceSets can be a costly operation, so re-using them as much as possible can help avoid unnecessary work for the GPU and result in improved performance.

## Types of Resources

There are many types of shader resources available in Veldrid. There is some overlap between different types of resources, and many techniques can be accomplished using several different combinations of resources. There are a variety of tradeoffs that make some resource types better for certain applications than others. For example, some types of resources have smaller storage limits but are faster to access. Some resources have unlimited storage limits, but can be slower to access. Some resources can be both read to and written from, but are generally slower to access and can only be used in certain Shader stages. Understanding the characteristics of each kind of resource is important to achieving optimal performance using Veldrid.

### Uniform Buffer

A uniform [DeviceBuffer](xref:Veldrid.DeviceBuffer) is a resource used to store a small-to-medium amount of data for a shader to access. Uniform buffers are commonly used to store per-object transformations, camera transformations and properties, and other miscellaneous information. Uniform buffers are very fast to access.

A DeviceBuffer must be created with [BufferUsage.UniformBuffer](xref:Veldrid.BufferUsage) to be used as a uniform buffer.

Uniform buffers correspond to the following:
* HLSL: `cbuffer` blocks.
* GLSL: uniform blocks.
  * NOTE: "simple" GLSL uniform variables, e.g. `uniform mat4 ProjectionMatrix;` are not supported in Veldrid. They must be wrapped in a uniform block.
* Metal: `constant T& value` variables.

### Structured Buffer

A structured buffer is another kind of DeviceBuffer resource available to shaders. Like uniform buffers, they can be used to store arbitrary data, but are generally much larger. Structured buffers are used to store a large number of a single kind of value (a "structure"). The size of the structure that is stored must be designated upon DeviceBuffer creation (see [BufferDescription.StructureByteStride](xref:Veldrid.BufferDescription#Veldrid_BufferDescription_StructureByteStride)).

Structured buffers may be read-only or read-write. Read-write buffers can be written to in the fragment and compute stages, allowing arbitrary data to be output by shaders. Read-only structured buffers must be created with the [BufferUsage.StructuredBufferReadOnly](xref:Veldrid.BufferUsage) flag, and read-write structured buffers must be created with the [BufferUsage.StructuredBufferReadWrite](xref:Veldrid.BufferUsage) flag.

Structured buffers have a much larger size limit than uniform buffers (generally, the size is unlimited), but are slightly slower to access.

Structured buffers correspond to the following:
* HLSL: `StructuredBuffer<T>` or `RWStructuredBuffer<T>` objects.
* GLSL: `readonly` or normal "buffer blocks".
* Metal: `device T* value` variables.

### DeviceBufferRange

A [DeviceBufferRange](xref:Veldrid.DeviceBufferRange) is a simple wrapper struct that describes a range of a [DeviceBuffer](xref:Veldrid.DeviceBuffer). When included in a ResourceSet, a DeviceBufferRange makes only a subset of the DeviceBuffer available to be read from or written to by the shader. This is useful when you want to store multiple distinct blocks in a DeviceBuffer and use each in different draw calls, or if you want a compute shader invocation to only fill in a small portion of a buffer.

### TextureView

A [TextureView](xref:Veldrid.TextureView) is a resource which gives a shader read-only or read-write access to a [Texture](xref:Veldrid.Texture). A TextureView allows a subset of the Texture object's dimensions to be accessible, enabling a single slice of an array texture to be read from or written to, for example.

It is also possible to bind a [Texture](xref:Veldrid.Texture) directly into a slot expecting a TextureView. Doing this is functionally equivalent to binding a TextureView that covers the Texture's full range of mip levels and array layers, with the same PixelFormat.

Read-only TextureView objects must have a [Target](xref:Veldrid.TextureViewDescription#Veldrid_TextureViewDescription_Target) that was created with the [TextureUsage.Sampled](xref:Veldrid.TextureUsage) flag. Read-write TextureViews must target a Texture created with the [TextureUsage.Storage](xref:Veldrid.TextureUsage) flag.

TextureViews can have a different [PixelFormat](xref:Veldrid.PixelFormat) from the Texture they target, with some restrictions. This allows you to reinterpret Texture data between different storage types (UNorm, SNorm, UInt, SInt, Float). For views over uncompressed Textures, the overall size and number of components in the view's format must be equal to the underlying Texture's format. For views over compressed Textures, it is only possible to use the underlying Texture's exact PixelFormat or its sRGB/non-sRGB counterpart.

Read-only TextureViews correspond to the following types in various shader languages:
* HLSL: "Texture" objects (`Texture2D`, `Texture2DArray`, `TextureCube`, etc.).
* GLSL (OpenGL): "sampler" objects (`sampler2D`, `sampler2DArray`, `samplerCube`, etc.).
* GLSL (Vulkan): "texture" objects in Vulkan-flavored GLSL (`texture2D`, `texture2DArray`, `textureCube`, etc.).
* Metal: "texture" objects (`texture2d<T>`, `texture2d_ms<T>`, etc.) with `access::sample` or `access::read`.

Read-write TextureViews correspond to the following:
* HLSL: `RWTexture<T>`.
* GLSL: uniform `image` variables.
* Metal: "texture" objects (`texture2d<T>`, `texture2d_ms<T>`, etc.) with `access::read_write`.

### Sampler

A [Sampler](xref:Veldrid.Sampler) is a resource which controls how TextureViews are sampled. See [SamplerDescription](xref:Veldrid.SamplerDescription) for the set of properties governing their behavior.

Several shared Samplers are available as properties on [GraphicsDevice](xref:Veldrid.GraphicsDevice). These can be used when a common type of Sampler is needed and you don't want to manage the lifetime of a shared Sampler yourself.

Anisotropic filtering, while very common, is not supported on all [GraphicsDevice](xref:Veldrid.GraphicsDevice) instances. [GraphicsDeviceFeatures.SamplerAnisotropy](xref:Veldrid.GraphicsDeviceFeatures#Veldrid_GraphicsDeviceFeatures_SamplerAnisotropy) indicates whether anisotropic filtering is supported.

There is an important caveat regarding OpenGL support for Sampler objects and how they can be bound to a Pipeline. Before Vulkan, GLSL did not allow Sampler object state to be separated from Textures. GLSL "sampler" objects encapsulate both, and the GL objects must be bound to a shared set of texture units. TextureViews and Samplers are separated in Veldrid, and these objects must be bound separately to a Pipeline. This means it is not possible to represent Veldrid's abstraction fully in the OpenGL backend. When a Sampler object appears in a ResourceLayout list, it applies to all of the TextureView objects before it (until the previous Sampler in the list). This means that if you need to sample the same TextureView with two different Samplers, then you need to declare two TextureViews with two Samplers in your ResourceLayout.

## Mapping HLSL/GLSL resources to ResourceLayouts

The layout system is convention-based, and relies on shader code being authored in a particular way for resource slots to match.

* Vulkan/SPIR-V: ResourceLayouts match very closely with regular Vulkan and SPIR-V conventions. In a Vulkan shader, a uniform's "set" layout specifies the ResourceLayout index in the overall [ResourceLayouts](xref:Veldrid.GraphicsPipelineDescription#Veldrid_GraphicsPipelineDescription_ResourceLayouts) array. The "binding" layout specifies the specific resource within that ResourceLayout identified by the "set". For example:
```
layout(set = 0, binding = 1) uniform View
```
defines a uniform belonging to binding 1 (of the [ResourceLayoutElementDescription](xref:Veldrid.ResourceLayoutDescription#Veldrid_ResourceLayoutDescription_Elements) array), of set 0 (of the [ResourceLayouts](xref:Veldrid.GraphicsPipelineDescription#Veldrid_GraphicsPipelineDescription_ResourceLayouts) array).

GLSL is used in the example above, but the same principle applies to any SPIR-V source language. When compiling HLSL shaders to SPIR-V for use with Veldrid, the `[[vk::binding(<binding>, <set>)]]` attribute should be used to declare the resource set and binding for each resource. It is important to note that in this attribute, the binding index appears first, and the set index appears second.

* Direct3D 11: Resources are assigned HLSL registers based on their positions in the [ResourceLayouts](xref:Veldrid.GraphicsPipelineDescription#Veldrid_GraphicsPipelineDescription_ResourceLayouts) array first, and then by their position in the [ResourceLayoutElementDescriptions](xref:Veldrid.ResourceLayoutDescription#Veldrid_ResourceLayoutDescription_Elements) array. Each resource type (texture, sampler, uniform) is assigned an increasing integer value for its register number. For example, given these two ResourceLayouts:

    **Layout 0**
    | Element | Type | Name |
    |------ |------|------|
    | 0 | UniformBuffer | UB0 |
    | 1 | TextureView | Tex0 |
    | 2 | Sampler | Sampler0 |
    | 3 | Sampler | Sampler1 |
    | 4 | TextureView | Tex1 |
    | 5 | UniformBuffer | UB3 |

    **Layout 1**
    | Element | Type | Name |
    |------ |------|------|
    | 0 | TextureView | Tex2 |
    | 1 | UniformBuffer | UB1 |
    | 2 | UniformBuffer | UB2 |

    the HLSL resources must be specified as follows:
    ```
    cbuffer UB0 : register(b0) { ... }
    cbuffer UB3 : register(b1) { ... }
    cbuffer UB1 : register(b2) { ... }
    cbuffer UB2 : register(b3) { ... }
    Texture2D Tex0 : register(t0);
    Texture2D Tex1 : register(t1);
    Texture2D Tex2 : register(t2);
    SamplerState Sampler0 : register(s0);
    SamplerState Sampler1 : register(s1);
    ```
    (the declaration order is unimportant -- only the register indices matter).

* Metal: Metal resources are assigned slots in the same way as HLSL resources. There are only three types of slots (buffer, texture, and sampler), and indices are assigned in simple increasing order depending on where they appear in the ResourceLayout creation list. One important thing to note is that Metal treats ALL buffer bindings the same. Unlike other API's, this includes the buffers used for vertex input data. Veldrid assumes that all vertex buffers for a pipeline will be assigned to the lowest-possible slots, and "regular" buffer resources (uniform, structured, etc.) will be assigned slots after those vertex buffers. For example: a Metal Pipeline is created which uses 3 vertex buffers containing vertex data, and 3 uniform buffers. The vertex buffers will use slots 0, 1, and 2 (invisible to the shader author, because they will simply use the `[[ stage_in ]]` syntax). The uniform buffers will use slots 3, 4, and 5. This can be confusing to those familiar with other graphics API's, which separate out vertex buffer bindings entirely.
 * NOTE: This "vertex-buffer-offset" is not applied to fragment stage buffer slots. If there are 5 uniform buffers (0, 1, 2, 3, 4), and buffers 3 and 4 apply to the fragment stage, then they will be placed into fragment slots 3 and 4, regardless of how many vertex buffers are used by the Pipeline.

* OpenGL and OpenGL ES: Resources are matched strictly by-name. Each resource must correspond to a uniform or uniform block in the shader program, and the names must be identical. Numerical indices are ignored when matching resources to GLSL uniforms. Veldrid does not support the "ARB_explicit_uniform_location" extension, primarily because it is not supported by Apple.

  * NOTE: GLSL `sampler2D` variables are matched to a resource with [ResourceKind.TextureReadOnly](xref:Veldrid.ResourceKind). The name of the element with ResourceKind.TextureReadOnly must match the name of the `sampler2D` variable exactly. When specifying an element with ResourceKind.Sampler, the name of the element is irrelevant and unused -- the sampler in that slot will apply to the closest previous element(s) with ResourceKind.TextureReadOnly.

## ResourceBindingModel.Improved

Veldrid 4.2.0 introduces [ResourceBindingModel.Improved](xref:Veldrid.ResourceBindingModel), which is an optional flag specified in [GraphicsDeviceOptions](xref:Veldrid.GraphicsDeviceOptions), or individually in a particular graphics [Pipeline](xref:Veldrid.GraphicsPipelineDescription). The only affect this flag has is to alter the assignment of vertex buffer indices for Metal shaders. Instead of being assigned at the beginning of the buffer list (e.g. slots 0, 1, 2), they are instead assigned to the END of the buffer list. For example, if your Pipeline uses 3 uniform buffers and and two vertex buffers, the indices will be as follows:

**ResourceBindingModel.Default**
    | Element | Type | Name |
    | ------- | ---- | ---- |
    | 0 | VertexBuffer | VB0 |
    | 1 | VertexBuffer | VB1 |
    | 2 | VertexBuffer | VB2 |
    | 3 | UniformBuffer | UB0 |
    | 4 | UniformBuffer | UB1 |
    | 5 | UniformBuffer | UB2 |

**ResourceBindingModel.Improved**
    | Element | Type | Name |
    | ------- | ---- | ---- |
    | 0 | UniformBuffer | UB0 |
    | 1 | UniformBuffer | UB1 |
    | 2 | UniformBuffer | UB2 |
    | 3 | VertexBuffer | VB0 |
    | 4 | VertexBuffer | VB1 |
    | 5 | VertexBuffer | VB2 |

The behavior of ResourceBindingModel.Improved is desirable because it allows you to change the number of vertex buffers without affecting any of the resource indices for any other buffers. This allows you to re-use the same shader code for multiple Pipelines which use a different number of vertex buffers. It gives you the same flexibility that is available to other graphics backends, where vertex buffer binding slots are fully separate from other buffer binding slots.
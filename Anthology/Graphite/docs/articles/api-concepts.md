---
uid: api-concepts
---

# API Concepts

Veldrid is a flexible library exposing a large number of types granting access to various pieces of graphics functionality. The following are the most important concepts in the API which are needed for many common rendering techniques. See the [API Documentation](xref:Veldrid) page for a full listing of available Veldrid types.

## GraphicsDevice

A [GraphicsDevice](xref:Veldrid.GraphicsDevice) is the entry point into Veldrid. It represents a GPU device capable of creating and managing graphics resources, executing commands, and presenting rendered images to the application's main swapchain. GraphicsDevices are created explicity by the application, and a particular backing API (Vulkan, Direct3D, OpenGL) is selected at that time.

## CommandList

A [CommandList](xref:Veldrid.CommandList) is a device resource capable of recording graphics commands, which can then be executed by a GraphicsDevice. All rendering commands, like setting draw state, binding resources, clearing framebuffers, and issuing draw calls, are submitted to a CommandList. Optionally, multiple CommandList objects can be used in parallel to record independent graphics commands on separate threads.

See the [CommandList overview](xref:command-lists) for more information.

## Resources

A variety of device resources can be created by a GraphicsDevice in order to control rendering operations. Veldrid resources are owned by their creator, and they must be disposed explicitly to avoid leaking device memory.

### "Descriptions"

Veldrid exposes a number of Description types, which are plain data structures used by a [ResourceFactory](xref:Veldrid.ResourceFactory) in order to create new graphics resources. These are simple, transient objects which contain a small number of fields describing the properties of a single type of resource. For example, a [BufferDescription](xref:Veldrid.BufferDescription) contains a few pieces of information necessary to create a [DeviceBuffer](xref:Veldrid.DeviceBuffer):

* The total size of the DeviceBuffer in bytes.
* How the DeviceBuffer will be used ([Veldrid.BufferUsage](xref:Veldrid.BufferUsage)).
* If the DeviceBuffer is a structured DeviceBuffer, then the size of each structure element.

All device resources can be uniformly created with an appropriate Description object. Additionally, there are some convenience methods that allow the creation of some resource objects with a small set of common parameters, rather than a full Description object.

### Textures and DeviceBuffers

[Textures](xref:Veldrid.Texture) and [DeviceBuffers](xref:Veldrid.DeviceBuffer) are staple device resources used to store various kinds of information on the GPU. They can act as the source or destination of data for rendering operations.

Textures can be sampled in shader programs using a [TextureView](xref:Veldrid.TextureView). They can also be used as the destination of drawing operations when used to create a [Framebuffer](xref:Veldrid.Framebuffer) object.

See the [Textures overview](xref:textures) for more information about Textures.

DeviceBuffers can be created for a variety of applications. The [BufferUsage](xref:Veldrid.BufferUsage) type enumerates all options.

* Vertex Buffers (BufferUsage.VertexBuffer) contain vertex data which is bound to a CommandList before issuing render commands. Vertex data is pulled from the bound vertex buffers.

* Index Buffers (BufferUsage.IndexBuffer) contain index data which controls how vertices are selected during indexed drawing [CommandList.DrawIndexed](xref:Veldrid.CommandList#Veldrid_CommandList_DrawIndexed_System_UInt32_System_UInt32_System_UInt32_System_Int32_System_UInt32_).

* Uniform Buffers (BufferUsage.UniformBuffer) are DeviceBuffers which can be read from shader programs. These are commonly used to store object transformations, camera transformations, and other arbitrary pieces of data encoding some information about the scene being rendered.

* Structured Buffers (BufferUsage.StructuredBufferReadOnly and BufferUsage.StructuredBufferReadWrite) are DeviceBuffers containing an array of a single data type, whose size is specified upon buffer creation. Shaders can get read-only or read-write access to these resources, depending on the needs of the technique being used.

* Indirect Buffers (BufferUsage.IndirectBuffer) contain "draw" or "dispatch" information. When issuing a draw or dispatch command, instead of directly specifying the parameters (vertex count, instance count, base vertex, etc.), you can point the command at a DeviceBuffer which contains that information. This gives you greater flexibility and allows you, for example, to fill up your Indirect Buffer with a compute shader.

### Shaders

[Shaders](xref:Veldrid.Shader) are a device resource which represent a single shader module, for a single shader stage (vertex, fragment, tesselation, geometry, [compute](xref:compute-shaders)). They are created from graphics-API-specific data chunks. Multiple shader modules are combined into a "shader set", used to construct a Pipeline. See the [Shaders and Resources overview](xref:shaders-and-resources) and the [compute shaders overview](xref:compute-shaders) for more information.

### Pipelines

In Veldrid, there are two types of [Pipelines](xref:Veldrid.Pipeline): graphics and compute.

A graphics Pipeline is a device resource which encapsulates a large amount of information. In other graphics libraries, a Pipeline is split into several unrelated objects or function calls which can be combined in confusing and unpredictable ways. Veldrid pipelines represent the combined set of all of these pieces of information, and are immutable. This avoids a huge amount of complexity inherent in the mutable state machine paradigm of OpenGL and similar APIs. Additionally, the correctness of a Pipeline is validated up-front. If you attempt to create a Pipeline from components that are not compatible, then an exception is produced before the invalid Pipeline is created. Pipeline information encompasses:

* Blend state: how color values are blended into the Framebuffer.
* Depth stencil state: How depth-stencil testing, writing, comparing are performed.
* Rasterizer state: How culling, clipping, scissor tests, etc. are performed.
* Primitive topology: How a series of input vertices are interpreted.
* Shader set: the full set of shader programs in use.
* Resource layouts: The types and layout of all shader resources being used.
* Outputs: The depth and color outputs of the Framebuffer that are written to.

It is not possible to issue a draw command without binding a graphics Pipeline.

A compute Pipeline is another kind of resource which encapsulates the necessary state for a compute shader dispatch. A compute Pipeline does not include any graphics-specific information, like blend state, depth stencil state, etc. It is therefore much simpler, and only contains:

* The compute shader module used
* Resource layouts

Compute Pipelines do not have explicit Framebuffer-based outputs like graphics Pipelines do. Instead, they make use of read-write shader resources to output information.

Graphics and compute pipelines are tracked separately in a CommandList. This means that setting a compute Pipeline, or attaching ResourceSets to a compute Pipeline, do not disturb the previously-set graphics Pipeline state.

### ResourceLayouts and ResourceSets

A [ResourceSet](xref:Veldrid.ResourceSet) is another fundamental device resource which is used, along with a Pipeline, for drawing commands. ResourceSets are the mechanism by which BindableResource objects ([DeviceBuffers](xref:Veldrid.DeviceBuffer), [TextureViews](xref:Veldrid.TextureView), and [Samplers](xref:Veldrid.Sampler)) are bound to a Pipeline and become accessible to shaders for use when rendering. The types and order of resources is described in a [ResourceLayout](xref:Veldrid.ResourceLayout) object, used to create both a ResourceSet and a Pipeline.

### Framebuffers

A [Framebuffer](xref:Veldrid.Framebuffer) controls the set of textures that are drawn into when draw commands are executed. The application's swapchain Framebuffer is also accessible via the [GraphicsDevice.SwapchainFramebuffer](xref:Veldrid.GraphicsDevice#Veldrid_GraphicsDevice_SwapchainFramebuffer) property. The swapchain Framebuffer is used to present an image to the application window or view. See the [Framebuffers overview](xref:framebuffers) for more information.

### Swapchains

A [Swapchain](xref:Veldrid.Swapchain) is a special object which handles the actual presentation of rendered images to a visible application view. Generally, an application like a game only needs one Swapchain, but it is also possible to create multiple Swapchains in a single application. This allows you to render to multiple windows, or multiple panels in a single window (provided the UI framework supports that). See [Swapchains](xref:swapchains) for more information.
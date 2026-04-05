# Vulkan Backend

The Vulkan backend is a multi-platform backend implemented using the Vulkan API. Vulkan is supported on Windows, Linux, and Android. Vulkan's API is very close to NeoVeldrid's and as such this is a fairly simple, straightforward backend.

Vulkan [GraphicsDevices](xref:NeoVeldrid.GraphicsDevice) are created from a [VkSurfaceSource](xref:NeoVeldrid.Vk.VkSurfaceSource), which is a platform-specific object used to create a Vulkan surface (VkSurfaceKHR). The following helper functions are available:

* [CreateWin32](xref:NeoVeldrid.Vk.VkSurfaceSource#NeoVeldrid_Vk_VkSurfaceSource_CreateWin32_System_IntPtr_System_IntPtr_): Creates a VkSurfaceSource for the given Win32 instance and window handle.
* CreateXlib: Creates a VkSurfaceSource for the given Xlib display and window.

## API Concept Map

| NeoVeldrid Concept | Vulkan Concept | Notes |
| --------------- | --------------| ----- |
| [GraphicsDevice](xref:NeoVeldrid.GraphicsDevice) | [VkDevice](https://www.khronos.org/registry/vulkan/specs/1.0/man/html/VkDevice.html), [VkPhysicalDevice](https://www.khronos.org/registry/vulkan/specs/1.0/man/html/VkPhysicalDevice.html), [VkCommandPool](https://www.khronos.org/registry/vulkan/specs/1.0/man/html/VkCommandPool.html), [VkDescriptorPool](https://www.khronos.org/registry/vulkan/specs/1.0/man/html/VkDescriptorPool.html), [VkQueue](https://www.khronos.org/registry/vulkan/specs/1.0/man/html/VkQueue.html) | |
| [CommandList](xref:NeoVeldrid.CommandList) | [VkCommandBuffer](https://www.khronos.org/registry/vulkan/specs/1.0/man/html/VkCommandBuffer.html) | |
| [DeviceBuffer](xref:NeoVeldrid.DeviceBuffer) | [VkBuffer](https://www.khronos.org/registry/vulkan/specs/1.0/man/html/VkBuffer.html) | |
| [BufferUsage](xref:NeoVeldrid.BufferUsage) | [VkBufferUsageFlagBits](https://www.khronos.org/registry/vulkan/specs/1.0/man/html/VkBufferUsageFlagBits.html) | |
| [Texture](xref:NeoVeldrid.Texture) | [VkImage](https://www.khronos.org/registry/vulkan/specs/1.0/man/html/VkImage.html) | |
| [TextureUsage](xref:NeoVeldrid.TextureUsage) | [VkImageUsageFlagBits](https://www.khronos.org/registry/vulkan/specs/1.0/man/html/VkImageUsageFlagBits.html) | |
| [TextureView](xref:NeoVeldrid.TextureView) | [VkImageView](https://www.khronos.org/registry/vulkan/specs/1.0/man/html/VkImageView.html) | |
| [Sampler](xref:NeoVeldrid.Sampler) | [VkSampler](https://www.khronos.org/registry/vulkan/specs/1.0/man/html/VkSampler.html) | |
| [Pipeline](xref:NeoVeldrid.Pipeline) | [VkPipeline](https://www.khronos.org/registry/vulkan/specs/1.0/man/html/VkPipeline.html) | |
| [Blend State](xref:NeoVeldrid.BlendStateDescription) | [VkPipelineColorBlendStateCreateInfo](https://www.khronos.org/registry/vulkan/specs/1.0/man/html/VkPipelineColorBlendStateCreateInfo.html) | |
| [Depth Stencil State](xref:NeoVeldrid.DepthStencilStateDescription) | [VkPipelineDepthStencilStateCreateInfo](https://www.khronos.org/registry/vulkan/specs/1.0/man/html/VkPipelineDepthStencilStateCreateInfo.html) | |
| [Rasterizer State](xref:NeoVeldrid.RasterizerStateDescription) | [VkPipelineRasterizationStateCreateInfo](https://www.khronos.org/registry/vulkan/specs/1.0/man/html/VkPipelineRasterizationStateCreateInfo.html) | |
| [PrimitiveTopology](xref:NeoVeldrid.PrimitiveTopology) | [VkPrimitiveTopology](https://www.khronos.org/registry/vulkan/specs/1.0/man/html/VkPrimitiveTopology.html), [VkPipelineInputAssemblyStateCreateInfo](https://www.khronos.org/registry/vulkan/specs/1.0/man/html/VkPipelineInputAssemblyStateCreateInfo.html) | |
| [Vertex Layouts](xref:NeoVeldrid.VertexLayoutDescription) | [VkPipelineVertexInputStateCreateInfo](https://www.khronos.org/registry/vulkan/specs/1.0/man/html/VkPipelineVertexInputStateCreateInfo.html) | |
| [Shader](xref:NeoVeldrid.Shader) | [VkShaderModule](https://www.khronos.org/registry/vulkan/specs/1.0/man/html/VkShaderModule.html) | |
| [ShaderSetDescription](xref:NeoVeldrid.ShaderSetDescription) | None | |
| [Framebuffer](xref:NeoVeldrid.Framebuffer) | [VkFramebuffer](https://www.khronos.org/registry/vulkan/specs/1.0/man/html/VkFramebuffer.html) | |
| [ResourceLayout](xref:NeoVeldrid.ResourceLayout) | [VkDescriptorSetLayout](https://www.khronos.org/registry/vulkan/specs/1.0/man/html/VkDescriptorSetLayout.html) | |
| [ResourceSet](xref:NeoVeldrid.ResourceSet) | [VkDescriptorSet](https://www.khronos.org/registry/vulkan/specs/1.0/man/html/VkDescriptorSet.html) | |
| [Swapchain](xref:NeoVeldrid.GraphicsDevice#NeoVeldrid_GraphicsDevice_SwapchainFramebuffer) | VkSwapchainKHR | |

## Notes

NeoVeldrid's API is modeled after modern graphics API's like Vulkan. As a result, this backend is simple, straightforward, and has very good performance. Driver support for Vulkan remains somewhat less common than other graphics API's, but new GPUs from all major vendors support Vulkan. In time, Vulkan will likely be supported by all major systems.

### Memory Management

Memory management must be custom-handled in Vulkan. The `VkDeviceMemoryManager` class allocates and reclaims all memory needed in the Vulkan backend. In general, the allocation handling is as follows:

* The manager tracks several "chunk allocators", one for each type of memory (the `memoryTypeBits` and [VkMemoryPropertyFlags](https://www.khronos.org/registry/vulkan/specs/1.0/man/html/VkMemoryPropertyFlags.html) identify a "type" of memory).
* Each chunk allocator tracks a set of contiguous allocations of real physical device memory.
* When requested, an individual chunk allocator identifies a free chunk of memory (or allocates one), and subdivides it into an appropriate block for usage by the caller.
* Chunk allocators also reclaim memory when a resource is disposed.

### Staging Textures

Staging Textures are actually implemented using VkBuffers, rather than VkImages. This has no observable effect on the implementation, and is actually the recommended approach.

Linear VkImages would be another option, but they are extremely limited in most Vulkan implementations, and cannot support all of the possible permutations of a staging Texture (dimensions, mips, array layers, etc.). For example, almost all Vulkan implementations require that Linear images be 2D (not even 1D), have one mip level, have one array layer, and are a small subset of pixel formats. Since staging Textures are conceptually just a contiguous, tightly-packed block of memory, a VkBuffer works perfectly and has none of the above limitations. Simple arithmetic can convert between the buffer' linear address space into a Texture's 1D, 2D, or 3D address space. Vulkan has a variety of commands that perform copies between VkBuffers and VkImages.
# Vulkan Backend

The Vulkan backend is a multi-platform backend implemented using the Vulkan API. Vulkan is supported on Windows, Linux, and Android. Vulkan's API is very close to Veldrid's and as such this is a fairly simple, straightforward backend.

Vulkan [GraphicsDevices](xref:Veldrid.GraphicsDevice) are created from a [VkSurfaceSource](xref:Veldrid.Vk.VkSurfaceSource), which is a platform-specific object used to create a Vulkan surface (VkSurfaceKHR). The following helper functions are available:

* [CreateWin32](xref:Veldrid.Vk.VkSurfaceSource#Veldrid_Vk_VkSurfaceSource_CreateWin32_System_IntPtr_System_IntPtr_): Creates a VkSurfaceSource for the given Win32 instance and window handle.
* [CreateXlib](xref:Veldrid.Vk.VkSurfaceSource#Veldrid_Vk_VkSurfaceSource_CreateXlib_Display__Window_): Creates a VkSurfaceSource for the given Xlib display and window.

## API Concept Map

| Veldrid Concept | Vulkan Concept | Notes |
| --------------- | --------------| ----- |
| [GraphicsDevice](xref:Veldrid.GraphicsDevice) | [VkDevice](https://www.khronos.org/registry/vulkan/specs/1.0/man/html/VkDevice.html), [VkPhysicalDevice](https://www.khronos.org/registry/vulkan/specs/1.0/man/html/VkPhysicalDevice.html), [VkCommandPool](https://www.khronos.org/registry/vulkan/specs/1.0/man/html/VkCommandPool.html), [VkDescriptorPool](https://www.khronos.org/registry/vulkan/specs/1.0/man/html/VkDescriptorPool.html), [VkQueue](https://www.khronos.org/registry/vulkan/specs/1.0/man/html/VkQueue.html) | |
| [CommandList](xref:Veldrid.CommandList) | [VkCommandBuffer](https://www.khronos.org/registry/vulkan/specs/1.0/man/html/VkCommandBuffer.html) | |
| [DeviceBuffer](xref:Veldrid.DeviceBuffer) | [VkBuffer](https://www.khronos.org/registry/vulkan/specs/1.0/man/html/VkBuffer.html) | |
| [BufferUsage](xref:Veldrid.BufferUsage) | [VkBufferUsageFlagBits](https://www.khronos.org/registry/vulkan/specs/1.0/man/html/VkBufferUsageFlagBits.html) | |
| [Texture](xref:Veldrid.Texture) | [VkImage](https://www.khronos.org/registry/vulkan/specs/1.0/man/html/VkImage.html) | |
| [TextureUsage](xref:Veldrid.TextureUsage) | [VkImageUsageFlagBits](https://www.khronos.org/registry/vulkan/specs/1.0/man/html/VkImageUsageFlagBits.html) | |
| [TextureView](xref:Veldrid.TextureView) | [VkImageView](https://www.khronos.org/registry/vulkan/specs/1.0/man/html/VkImageView.html) | |
| [Sampler](xref:Veldrid.Sampler) | [VkSampler](https://www.khronos.org/registry/vulkan/specs/1.0/man/html/VkSampler.html) | |
| [Pipeline](xref:Veldrid.Pipeline) | [VkPipeline](https://www.khronos.org/registry/vulkan/specs/1.0/man/html/VkPipeline.html) | |
| [Blend State](xref:Veldrid.BlendStateDescription) | [VkPipelineColorBlendStateCreateInfo](https://www.khronos.org/registry/vulkan/specs/1.0/man/html/VkPipelineColorBlendStateCreateInfo.html) | |
| [Depth Stencil State](xref:Veldrid.DepthStencilStateDescription) | [VkPipelineDepthStencilStateCreateInfo](https://www.khronos.org/registry/vulkan/specs/1.0/man/html/VkPipelineDepthStencilStateCreateInfo.html) | |
| [Rasterizer State](xref:Veldrid.RasterizerStateDescription) | [VkPipelineRasterizationStateCreateInfo](https://www.khronos.org/registry/vulkan/specs/1.0/man/html/VkPipelineRasterizationStateCreateInfo.html) | |
| [PrimitiveTopology](xref:Veldrid.PrimitiveTopology) | [VkPrimitiveTopology](https://www.khronos.org/registry/vulkan/specs/1.0/man/html/VkPrimitiveTopology.html), [VkPipelineInputAssemblyStateCreateInfo](https://www.khronos.org/registry/vulkan/specs/1.0/man/html/VkPipelineInputAssemblyStateCreateInfo.html) | |
| [Vertex Layouts](xref:Veldrid.VertexLayoutDescription) | [VkPipelineVertexInputStateCreateInfo](https://www.khronos.org/registry/vulkan/specs/1.0/man/html/VkPipelineVertexInputStateCreateInfo.html) | |
| [Shader](xref:Veldrid.Shader) | [VkShaderModule](https://www.khronos.org/registry/vulkan/specs/1.0/man/html/VkShaderModule.html) | |
| [ShaderSetDescription](xref:Veldrid.ShaderSetDescription) | None | |
| [Framebuffer](xref:Veldrid.Framebuffer) | [VkFramebuffer](https://www.khronos.org/registry/vulkan/specs/1.0/man/html/VkFramebuffer.html) | |
| [ResourceLayout](xref:Veldrid.ResourceLayout) | [VkDescriptorSetLayout](https://www.khronos.org/registry/vulkan/specs/1.0/man/html/VkDescriptorSetLayout.html) | |
| [ResourceSet](xref:Veldrid.ResourceSet) | [VkDescriptorSet](https://www.khronos.org/registry/vulkan/specs/1.0/man/html/VkDescriptorSet.html) | |
| [Swapchain](xref:Veldrid.GraphicsDevice#Veldrid_GraphicsDevice_SwapchainFramebuffer) | VkSwapchainKHR | |

## Notes

Veldrid's API is modeled after modern graphics API's like Vulkan. As a result, this backend is simple, straightforward, and has very good performance. Driver support for Vulkan remains somewhat less common than other graphics API's, but new GPUs from all major vendors support Vulkan. In time, Vulkan will likely be supported by all major systems.

### Memory Management

Memory management must be custom-handled in Vulkan. The `VkDeviceMemoryManager` class allocates and reclaims all memory needed in the Vulkan backend. In general, the allocation handling is as follows:

* The manager tracks several "chunk allocators", one for each type of memory (the `memoryTypeBits` and [VkMemoryPropertyFlags](https://www.khronos.org/registry/vulkan/specs/1.0/man/html/VkMemoryPropertyFlags.html) identify a "type" of memory).
* Each chunk allocator tracks a set of contiguous allocations of real physical device memory.
* When requested, an individual chunk allocator identifies a free chunk of memory (or allocates one), and subdivides it into an appropriate block for usage by the caller.
* Chunk allocators also reclaim memory when a resource is disposed.

### Staging Textures

Staging Textures are actually implemented using VkBuffers, rather than VkImages. This has no observable effect on the implementation, and is actually the recommended approach.

Linear VkImages would be another option, but they are extremely limited in most Vulkan implementations, and cannot support all of the possible permutations of a staging Texture (dimensions, mips, array layers, etc.). For example, almost all Vulkan implementations require that Linear images be 2D (not even 1D), have one mip level, have one array layer, and are a small subset of pixel formats. Since staging Textures are conceptually just a contiguous, tightly-packed block of memory, a VkBuffer works perfectly and has none of the above limitations. Simple arithmetic can convert between the buffer' linear address space into a Texture's 1D, 2D, or 3D address space. Vulkan has a variety of commands that perform copies between VkBuffers and VkImages.
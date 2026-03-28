# OpenGL Backend

The OpenGL backend is a multi-platform backend implemented using OpenGL. It is supported on most desktop platforms, except for UWP. OpenGL 3.0 or higher is required for an OpenGL [GraphicsDevice](xref:Veldrid.GraphicsDevice).

The OpenGL ES backend is more-or-less the same as the OpenGL backend. There exist several minor differences related to how extensions are queried, and the exact names of some GL functions are different. Veldrid requires OpenGL ES 3.0 or higher to create an OpenGL ES GraphicsDevice.

To create an OpenGL or OpenGL ES GraphicsDevice, several pieces of pre-configured "context" information are needed (see [OpenGLPlatformInfo](xref:Veldrid.OpenGL.OpenGLPlatformInfo)). This information includes:

* The OpenGL context handle, created outside of Veldrid.
* The "GetProcAddress" delegate, used to retrieve function pointers.
* The "MakeCurrent" delegate, used to make an OpenGL context current on the calling thread.
* The "GetCurrentContext" delegate, used to retrieve the calling thread's current OpenGL context.
* The "ClearCurrentContext" delegate, used to clear the calling thread's OpenGL context.
* The "Delete Context" delegate, used to delete an OpenGL context.
* The "SwapBuffers" delegate, used to swap the applications backbuffer.
* The "SetSyncToVerticalBlank" delegate, used to control Vsync behavior.
* The "SetSwapchainFramebuffer" delegate, used to set the main Swapchain Framebuffer.

These functions are platform-specific, and different operating systems vary in how they implement these functions. As such, it is difficult to write portable logic which satisfies all of these pieces of information. The Veldrid.StartupUtilities library includes functionality to obtain this information portably, using SDL2.

## API Concept Map

| Veldrid Concept | OpenGL Concept | Notes |
| --------------- | --------------| ----- |
| [GraphicsDevice](xref:Veldrid.GraphicsDevice) | GL Context (roughly) | Various other parts of OpenGL support GraphicsDevice functionality. |
| [CommandList](xref:Veldrid.CommandList) | Emulated (see below) |
| [DeviceBuffer](xref:Veldrid.DeviceBuffer) | [Buffer objects](https://www.khronos.org/opengl/wiki/Buffer_Object) | |
| [BufferUsage](xref:Veldrid.BufferUsage) | None | OpenGL buffers can always be used as any type of buffer -- there is no specialization. |
| [Texture](xref:Veldrid.Texture) | [Texture objects](https://www.khronos.org/opengl/wiki/Texture) | |
| [TextureUsage](xref:Veldrid.TextureUsage) | None | OpenGL textures can always be used as any type of texture -- there is no specialization. |
| [TextureView](xref:Veldrid.TextureView) | [`ARB_texture_view`](https://www.khronos.org/opengl/wiki/Texture_Storage#Texture_views) | Not all Veldrid TextureViews require `ARB_texture_view`. |
| [Sampler](xref:Veldrid.Sampler) | [Sampler objects](https://www.khronos.org/opengl/wiki/Sampler_Object) | |
| [Pipeline](xref:Veldrid.Pipeline) | Compound (see below) | |
| [Blend State](xref:Veldrid.BlendStateDescription) | `glBlendColor`, `glBlendFuncSeparatei`, `glBlendEquationSeparatei` | |
| [Depth Stencil State](xref:Veldrid.DepthStencilStateDescription) | `glDepthFunc`, `glDepthMask`, `glStencilFuncSeparate`, `glStencilMask` | |
| [Rasterizer State](xref:Veldrid.RasterizerStateDescription) | `glCullFace`, `GL_CULL_FACE`, `glPolygonMode`, `GL_SCISSOR_TEST`, `glScissorIndexed`, `GL_DEPTH_CLAMP`, `glFrontFace` | |
| [PrimitiveTopology](xref:Veldrid.PrimitiveTopology) | `GL_LINES`, `GL_POINTS`, `GL_TRIANGLES`, etc. | |
| [Vertex Layouts](xref:Veldrid.VertexLayoutDescription) | `glEnableVertexAttribArray`, `glVertexAttrib*Pointer`, `glVertexAttribDivisor` | A single, global VAO is bound during initialization, and vertex attributes are updated into it as needed. |
| [Shader](xref:Veldrid.Shader) | [Shader objects](https://www.khronos.org/opengl/wiki/Shader) | |
| [ShaderSetDescription](xref:Veldrid.ShaderSetDescription) | [Program objects](https://www.khronos.org/opengl/wiki/GLSL_Object#Program_objects). | |
| [Framebuffer](xref:Veldrid.Framebuffer) | [Framebuffer objects](https://www.khronos.org/opengl/wiki/Framebuffer_Object).aspx) | |
| [ResourceLayout](xref:Veldrid.ResourceLayout) | None | The OpenGL backend uses the information from a ResourceLayout to determine which uniform locations a particular resource corresponds to, by name. |
| [ResourceSet](xref:Veldrid.ResourceSet) | None | The OpenGL backend simply uses the individual resources contained in a ResourceSet to bind to a program. The slots are determined using information from the associated ResourceLayout. |
| [Swapchain](xref:Veldrid.GraphicsDevice#Veldrid_GraphicsDevice_SwapchainFramebuffer) | None (implicit) | An OpenGL swapchain is not explicitly created by the user, nor can it be directly accessed or managed. Instead, it is part of the OpenGL context created by the host system.

## Notes

The OpenGL backend is the most complicated backend, and has to work around various limitations in the OpenGL API to provide a full-fidelity implementation of Veldrid's abstraction. Although it is a highly portable backend and can be used on many operating systems, it is not the preferred backend on any system, because of the following:

* OpenGL driver quality is not consistent. Performance and correctness varies wildly across vendors and operating systems. The Veldrid OpenGL backend has a variety of code paths working around the presence and absence of certain extensions, and avoids using some features altogether due to their poor implementations on some systems.
* macOS does not support modern OpenGL, and it is unlikely that it ever will. This means that some functionality, like Compute Shaders and texture views, are completely unsupported on macOS. Various other important extensions, like `ARB_direct_state_access`, are also unimplemented, which results in degraded performance.
* OpenGL is a poorly-designed API, and is simply slower (in general) than other API's.
* OpenGL's lack of support for multiple threads requires more complex and costly handling in Veldrid's OpenGL backend, which imposes some minor runtime overhead. See below for more details.

On Linux, OpenGL may be the only available backend, if Vulkan is not supported. Luckily, Linux OpenGL drivers are generally of decent quality, and support modern functionality (unlike macOS). However, Vulkan is supported by all modern GPU's, and should be preferred when available.

### Threading Concerns

OpenGL has a very unfortunate relationship with threads. Not only can OpenGL functions not be used from multiple threads simultaneously, they must always be called from a SINGLE main thread. This means that it is not possible to simply synchronize access to a GL context between multiple threads. Only one single thread can ever use OpenGL functions. Veldrid allows many functions to be used in a free-threaded manner. All operations on a GraphicsDevice or a ResourceFactory, for example, can be used from any thread at any time. This allows you to create and update resources (DeviceBuffers and Textures) at any time, without worrying about synchronization. Modern graphics API's (even D3D11) allow this without trouble. Unfortunately, OpenGL does not support this natively. To accomodate OpenGL's limitations, the following approach is taken:

* All OpenGL functions are run from a single "OpenGL worker thread". This worker thread is started when the OpenGL GraphicsDevice is created, and loops continuously until the GraphicsDevice is disposed. This thread takes sole ownership of the GL context when it starts, using the provided delegates.
* All OpenGL resources (buffers, textures, framebuffers, shaders, etc.) are lazily-initialized. Because the real OpenGL resources must be created solely from the "OpenGL worker thread", this work is deferred until the resource is first used in an OpenGL function. When the worker thread is processing a command involving a DeviceBuffer, for example, it will ensure the OpenGL buffer object is created (`glCreateBuffers`, `glNamedBufferData`, etc.) before using it. The worker thread is the only thread that ever fully initializes OpenGL resources, but any thread is permitted to create an uninitialized resource.
* Resources that require initialization information store that information inside the lazy object until initialization occurs. For example, an OpenGL Shader stores the raw shader bytes in a shared storage buffer until the OpenGL shader object can actually be created (by the worker thread).
* All commands recorded into an OpenGL CommandList are stored into an emulated, CPU-side list. This is done in a specialized, allocation-free, and highly optimized manner. Commands that include data to be uploaded, like `CommandList.UpdateBuffer`, instead copy the data into a shared storage buffer. When the worker thread finally executes the CommandList, this data is consumed, and the shared storage buffer is recycled.

As you can imagine, the above approach has a small amount of runtime CPU and memory overhead. In general, the OpenGL backend needs to cache a little more information (on the host system) than other backends do. Also, some work that is done up-front in other backends is deferred until a (potentially) unexpected time in the OpenGL backend. For example, when you first bind an OpenGL Pipeline, you may need to pay a lazy initialization cost (compiling the shaders, linking a program, retrieving uniform locations, etc.). On the other hand, creating the uninitialized Pipeline object itself is relatively inexpensive, because none of that work is done up-front.

### Synchronization

Although OpenGL command execution is synchronous, it is all done through a separate worker thread. OpenGL Fence objects are actually signaled when the submission that they are attached to is fully processed by the worker thread. This means that, when an OpenGL CommandList is submitted, it will not be completed until a later time. The [Graphicsdevice.WaitForIdle](xref:Veldrid.GraphicsDevice) method blocks until all previously-submitted commands are processed by the worker thread.

Some GraphicsDevice methods need to truly synchronize with the worker thread. For example, if an OpenGL DeviceBuffer is mapped or unmapped, then `glMapBuffer` or `glUnmapBuffer` must be called on the dedicated worker thread before the call returns. To synchronize the calling thread and the worker thread, a `ManualResetEvent` is used. Once the buffer is mapped or unmapped by the worker thread, the reset event is signaled and the calling thread resumes. Because commands are executed in submission order by the worker thread, this means that a call to `GraphicsDevice.Map` will block until all previously-submitted CommandLists, as well as the actual mapping operation, are fully completed.

### MapMode.Write

One exception to the above example is [MapMode.Write](xref:Veldrid.MapMode). Because this mode does not require information to be read back from OpenGL, no synchronization is required. Instead, a shared storage buffer is provided to the caller immediately, and no communication with OpenGL takes place. When a resource mapped in this way is Unmap'd, the worker thread uploads the data and then recycles the shared buffer. Unmapping a resource like this causes the calling thread to synchronize with the worker thread (because it must communciate with OpenGL).

### State Tracking

Like the Direct3D 11 backend, the OpenGL backend internally tracks certain pieces of OpenGL state, and avoids issuing redundant function calls if they will have no effect. Because all OpenGL function calls are routed through a dedicated worker thread, this information can be easily tracked in a central location.
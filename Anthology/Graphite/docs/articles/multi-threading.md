---
uid: multi-threading
---

# Multi-Threading in NeoVeldrid

Modern graphics APIs encourage the full utilization of CPU resources, and allow rendering commands to be submitted from many different application threads. NeoVeldrid is similarly flexible, and most objects created by NeoVeldrid can be utilized in a multi-threaded application.

# GraphicsDevice
All properties and methods of a [GraphicsDevice](xref:NeoVeldrid.GraphicsDevice) can be safely invoked from any thread at any time.

Note that graphics commands do not complete synchronously with the method [GraphicsDevice.SubmitCommands](xref:NeoVeldrid.GraphicsDevice#NeoVeldrid_GraphicsDevice_SubmitCommands_NeoVeldrid_CommandList_). They are merely submitted to the graphics device and may complete later, but submission order is respected. [GraphicsDevice.WaitForIdle](xref:NeoVeldrid.GraphicsDevice#NeoVeldrid_GraphicsDevice_WaitForIdle) can be called from any thread to block until all submitted graphics commands have completed. Additionally, a [Fence](xref:NeoVeldrid.Fence) can be used in order to receive a finer-grained completion notification for a piece of work.

# ResourceFactory
The [ResourceFactory](xref:NeoVeldrid.ResourceFactory) object is responsible for the creation of all resources owned by the graphics device, and all of its methods can be safely invoked from any thread at any time. Device resources created this way can be used from multiple threads, and the update methods on GraphicsDevice ([UpdateBuffer](xref:NeoVeldrid.GraphicsDevice#NeoVeldrid_GraphicsDevice_UpdateBuffer_NeoVeldrid_DeviceBuffer_System_UInt32_System_IntPtr_System_UInt32_) and [UpdateTexture](xref:NeoVeldrid.GraphicsDevice#NeoVeldrid_GraphicsDevice_UpdateTexture_NeoVeldrid_Texture_System_IntPtr_System_UInt32_System_UInt32_System_UInt32_System_UInt32_System_UInt32_System_UInt32_System_UInt32_System_UInt32_System_UInt32_)), can be safely used in parallel.

# CommandList
The usage of a [CommandList](xref:NeoVeldrid.CommandList) is not thread-safe, but multiple CommandList objects can be created and used in parallel. Applications can leverage this to execute separate, independent render passes in parallel. For example, several cascaded shadow map passes and a 2D UI overlay could be executed in parallel and then utilized in the scene's final output.
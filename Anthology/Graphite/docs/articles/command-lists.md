---
uid: command-lists
---

# Command Lists

Using NeoVeldrid, all graphics commands must be executed using a [CommandList](xref:NeoVeldrid.CommandList). A CommandList is a special device resource (created by a [GraphicsDevice](xref:NeoVeldrid.GraphicsDevice)) that records a variety of commands. These commands can then be submitted for execution on the device using the [GraphicsDevice.SubmitCommands](xref:NeoVeldrid.GraphicsDevice#NeoVeldrid_GraphicsDevice_SubmitCommands_NeoVeldrid_CommandList_) method.

Several types of commands are available, depending on what work is being done.

## Resource Manipulation

[DeviceBuffer](xref:NeoVeldrid.DeviceBuffer) objects can be updated directly in a CommandList, using the UpdateBuffer method. When this method is called, the new data is "queued" into the CommandList, and will only be copied into the DeviceBuffer when execution reaches that point in the recorded CommandList. It is also possible to queue up multiple updates to the same DeviceBuffer in the same CommandList. However, it should be noted that there is storage and processing overhead associated with queueing buffer updates in this way, and it should be used sparingly.

Data can be copied between DeviceBuffers or between Texture objects, using one of the [CopyBuffer](xref:NeoVeldrid.CommandList#NeoVeldrid_CommandList_CopyBuffer_NeoVeldrid_DeviceBuffer_System_UInt32_NeoVeldrid_DeviceBuffer_System_UInt32_System_UInt32_) or [CopyTexture](xref:NeoVeldrid.CommandList#NeoVeldrid_CommandList_CopyTexture_NeoVeldrid_Texture_System_UInt32_System_UInt32_System_UInt32_System_UInt32_System_UInt32_NeoVeldrid_Texture_System_UInt32_System_UInt32_System_UInt32_System_UInt32_System_UInt32_System_UInt32_System_UInt32_System_UInt32_System_UInt32_) overloads. In order to read back data stored on the GPU, one of these methods can be used to transfer the desired information into a "staging" resource, which can be directly mapped and read from the CPU.

The [color](xref:NeoVeldrid.CommandList#NeoVeldrid_CommandList_ClearColorTarget_System_UInt32_NeoVeldrid_RgbaFloat_), [depth, and stencil](xref:NeoVeldrid.CommandList#NeoVeldrid_CommandList_ClearDepthStencil_System_Single_System_Byte_) targets of the active [Framebuffer](xref:NeoVeldrid.Framebuffer) can be cleared.

Multisampled Textures can be resolved down into a regular non-multisampled Texture using the [ResolveTexture](xref:NeoVeldrid.CommandList#NeoVeldrid_CommandList_ResolveTexture_NeoVeldrid_Texture_NeoVeldrid_Texture_) method.

## State Changes

There are several state-change methods available, which control various pieces of state influencing Draw commands. The following state can be changed:
* [Framebuffer](xref:NeoVeldrid.Framebuffer)
* [Viewports](xref:NeoVeldrid.Viewport)
* Scissor rectangles
* [Vertex Buffers](xref:NeoVeldrid.DeviceBuffer)
* [Index Buffer](xref:NeoVeldrid.DeviceBuffer)
* [Pipeline](xref:NeoVeldrid.Pipeline)
* [ResourceSets](xref:NeoVeldrid.ResourceSet)

## Drawing

The [DrawIndexed](xref:NeoVeldrid.CommandList#NeoVeldrid_CommandList_DrawIndexed_System_UInt32_System_UInt32_System_UInt32_System_Int32_System_UInt32_) method can be invoked to record an indexed draw command into the CommandList. The effect of this draw is controlled by the active state of the CommandList -- which vertex buffer, index buffer, Pipeline, ResourceSets, and Framebuffer are bound, and what Viewport and scissor rectangles are set. Regular indexed and instanced draw commands are both supported by this method.

The [Draw](xref:NeoVeldrid.CommandList#NeoVeldrid_CommandList_Draw_System_UInt32_System_UInt32_System_UInt32_System_UInt32_) method can be invoked to record a non-indexed draw command into the CommandList. This method can be used without an index buffer bound, and simply selects sequential vertices from the bound vertex buffer, if it exists.

## Compute Dispatch

The [Dispatch](xref:NeoVeldrid.CommandList#NeoVeldrid_CommandList_Dispatch_System_UInt32_System_UInt32_System_UInt32_) method can be invoked to record a compute dispatch command into the CommandList. The effects of this call depend on the currently-bound compute Pipeline and ResourceSets.

## Indirect

There are also "Indirect" variants of the above draw and compute functions. These "Indirect" variants allow the relevant draw/dispatch parameters to be read from a GPU DeviceBuffer, rather than passed in directly. This "Indirect" buffer must have been created with the [BufferUsage.IndirectBuffer](xref:NeoVeldrid.BufferUsage) flag, and is subject to some other restrictions. The information stored in the buffer should adhere to the format of one of the following structures, depending on the operation being performed:

* [IndirectDrawArguments](xref:NeoVeldrid.IndirectDrawArguments), used by [DrawIndirect](xref:NeoVeldrid.CommandList#NeoVeldrid_CommandList_DrawIndirect_NeoVeldrid_DeviceBuffer_System_UInt32_System_UInt32_System_UInt32_)
* [IndirectDrawIndexedArguments](xref:NeoVeldrid.IndirectDrawIndexedArguments), used by [DrawIndexedIndirect](xref:NeoVeldrid.CommandList#NeoVeldrid_CommandList_DrawIndexedIndirect_NeoVeldrid_DeviceBuffer_System_UInt32_System_UInt32_System_UInt32_)
* [IndirectDispatchArguments](xref:NeoVeldrid.IndirectDispatchArguments), used by [DispatchIndirect](xref:NeoVeldrid.CommandList#NeoVeldrid_CommandList_DispatchIndirect_NeoVeldrid_DeviceBuffer_System_UInt32_)
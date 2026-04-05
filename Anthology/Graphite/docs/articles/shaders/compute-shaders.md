---
uid: compute-shaders
---

# Compute Shaders

NeoVeldrid supports compute shaders, which are a unique type of shader that can perform highly-parallel computations and output arbitrary types of data. Although compute shaders are generally used to produce large amounts of data to be fed into subsequent rendering operations, it is also possible to use compute shaders as a tool for general-purpose parallel computation.

## Dispatch Groups

Compute shaders execute in parallel, 3-dimensional thread groups which are dispatched from a [CommandList](xref:NeoVeldrid.CommandList) (see [CommandList.Dispatch\(uint, uint, uint\)]([CommandList](xref:NeoVeldrid.CommandList#NeoVeldrid_CommandList_Dispatch_System_UInt32_System_UInt32_System_UInt32_)). The number of threads in this work group is determined by the X, Y, and Z counts provided to CommandList.Dispatch, and by the work group size defined in the compute shader code's size attribute and in the [ComputePipelineDescription](xref:NeoVeldrid.ComputePipelineDescription).

## Inputs and Outputs

Unlike shaders in a graphics [Pipeline](xref:NeoVeldrid.Pipeline), a compute shader has no "vertex" inputs, and no [Framebuffer](xref:NeoVeldrid.Framebuffer) outputs. All data in a compute shader must be read from bound [shader resources](xref:shaders-and-resources) such as [DeviceBuffers](xref:NeoVeldrid.DeviceBuffer) and [Textures](xref:textures). To output data, compute shaders make use of read-write buffers and textures.

## CommandList state

The state tracked in a [CommandList](xref:NeoVeldrid.CommandList) is separate for graphics and compute Pipelines. Setting a compute [Pipeline](xref:NeoVeldrid.Pipeline) does not alter the currently-bound graphics Pipeline, and vice-versa. Setting compute [ResourceSets](xref:NeoVeldrid.ResourceSet) does not alter any of the currently-bound graphics ResourceSets, and vice-versa.
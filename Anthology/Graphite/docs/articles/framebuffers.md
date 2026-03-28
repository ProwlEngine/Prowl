---
uid: framebuffers
---

# Framebuffers

[Framebuffers](xref:Veldrid.Framebuffer) are device resources which control the set of textures that render commands draw into. Framebuffers have zero-or-more color attachments, and zero-or-one depth attachment. They are created with a [FramebufferDescription](xref:Veldrid.FramebufferDescription).

## Target Textures

For a [Texture](xref:Veldrid.Texture) to be used as the color target of a Framebuffer, it must have been created with the [TextureUsage.RenderTarget](xref:Veldrid.TextureUsage) flag. Additionally, color targets must use a non-compressed [PixelFormat](xref:Veldrid.PixelFormat).

To be used as the depth target of a Framebuffer, it must have been created with the DepthStencil flag. Additionally, only Textures created using the R16_UNorm, R32_Float, D24_UNorm_S8_UInt, and D32_Float_S8_UInt [PixelFormats](xref:Veldrid.PixelFormat) are able to be used as depth targets. The latter two formats also include 8 bits of stencil buffer space.

All target textures used to create a Framebuffer must have identical dimensions and multisample counts.

## Framebuffer Compatibility

Before any Draw command can be issued, there must be an active Framebuffer set using the [CommandList.SetFramebuffer](xref:Veldrid.CommandList#Veldrid_CommandList_SetFramebuffer_Veldrid_Framebuffer_) method. Additionally, the current Pipeline and Framebuffer must be "compatible". A Pipeline is compatible with a Framebuffer if:

* It has the same number of outputs (color and depth)
* The [format](xref:Veldrid.PixelFormat) of those outputs all match.
* The sample count of all outputs match.

A Framebuffer exposes an [OutputDescription](xref:Veldrid.Framebuffer#Veldrid_Framebuffer_OutputDescription) property, which can be used to create a graphics Pipeline object (see [PipelineDescription.Outputs](xref:Veldrid.GraphicsPipelineDescription#Veldrid_GraphicsPipelineDescription_Outputs)) that is guaranteed to be compatible with it.

## Multisampled Framebuffers

It is possible to create a multisampled Framebuffer by using multisampled target textures. All target textures must have the same multisample count. In order to resolve the multisampled target textures into a single-sampled texture (in order to sample or present the image, for example), the [CommandList.ResolveTexture](xref:Veldrid.CommandList#Veldrid_CommandList_ResolveTexture_Veldrid_Texture_Veldrid_Texture_) method should be used.
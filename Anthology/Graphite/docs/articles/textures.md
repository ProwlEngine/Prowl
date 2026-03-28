---
uid: textures
---

# Textures

[Textures](xref:Veldrid.Texture) are device resources used as the source and destination of texture information. Textures can be created with a variety of options, controlling how individual instances are permitted to be used.

## Format

Texture data is stored in one of a number of [formats](xref:Veldrid.PixelFormat), specified by the [TextureDescription.Format](xref:Veldrid.TextureDescription#Veldrid_TextureDescription_Format) field.

## Type and Dimensions

Textures can be 1D, 2D, or 3D. This is controlled by [TextureDescription.Type](xref:Veldrid.TextureDescription#Veldrid_TextureDescription_Type). The actual dimensions of the Texture are controlled by [TextureDescription.Width](xref:Veldrid.TextureDescription#Veldrid_TextureDescription_Width), [TextureDescription.Height](xref:Veldrid.TextureDescription#Veldrid_TextureDescription_Height), and [TextureDescription.Depth](xref:Veldrid.TextureDescription#Veldrid_TextureDescription_Depth). For any particular type of Texture (1D, 2D, 3D), the dimensions must be valid. For example, a 1D Texture cannot have a Height or Depth greater than 1. A 2D Texture cannot have a depth value greater than 1.

## Array Layers

1D and 2D textures can have multiple array layers. 3D textures ([TextureType.Texture3D](xref:Veldrid.TextureType)) cannot. To control the number of array layers, set [TextureDescription.ArrayLayers](xref:Veldrid.TextureDescription#Veldrid_TextureDescription_ArrayLayers).

## Multisamples

2D Textures can be multisampled Textures. 1D and 3D textures cannot. This is specified by [TextureDescription.SampleCount](xref:Veldrid.TextureDescription#Veldrid_TextureDescription_SampleCount). The maximum sample count is limited by the specific GraphicsDevice being used. Use the [GetSampleCountLimit](xref:Veldrid.GraphicsDevice#Veldrid_GraphicsDevice_GetSampleCountLimit_Veldrid_PixelFormat_System_Boolean_) method to get the maximum sample count for a given [PixelFormat](xref:Veldrid.PixelFormat). Generally, 2 through 8 samples are commonly-supported. Higher values are rarely supported, but often unnecessary.

## Mipmaps

Non-multisampled textures can have multiple mipmap levels. This is specified by [TextureDescription.MipLevels](xref:Veldrid.TextureDescription#Veldrid_TextureDescription_MipLevels).

## As Shader Resources

In order to be read in a shader, a Texture needs to be created with the [TextureUsage.Sampled](xref:Veldrid.TextureUsage) flag. Additionally, the dimension of the Texture object must match what is used in the shader program. For example, an HLSL Texture2D must be matched with a 2D non-multisampled Texture object.

To bind a Texture to a shader program, a [TextureView](xref:Veldrid.TextureView) resource must be created. This TextureView can be used in a [ResourceSet](xref:Veldrid.ResourceSet) to make the Texture available to the shader.

Textures can also be written to in shaders, if they were created using the [TextureUsage.Storage](xref:Veldrid.TextureUsage) flag.

## As Framebuffer Targets

Textures can be used as Framebuffer targets. To use a Texture as a color target, it must have been created with the [TextureUsage.RenderTarget](xref:Veldrid.TextureUsage) flag. To use a Texture as a depth target, it must have been created with the [TextureUsage.DepthStencil](xref:Veldrid.TextureUsage) flag.

## Updating a Texture

Texture data is uploaded from CPU memory using the [GraphicsDevice.UpdateTexture](xref:Veldrid.GraphicsDevice#Veldrid_GraphicsDevice_UpdateTexture_Veldrid_Texture_System_IntPtr_System_UInt32_System_UInt32_System_UInt32_System_UInt32_System_UInt32_System_UInt32_System_UInt32_System_UInt32_System_UInt32_) method. This method can be used to upload the data for a subregion of a single mipmap level and array layer. Multiple calls should be used to fully upload all of the mipmap levels and array layers for a Texture, as appropriate.

## Staging Textures

Although [GraphicsDevice.UpdateTexture](xref:Veldrid.GraphicsDevice#Veldrid_GraphicsDevice_UpdateTexture_Veldrid_Texture_System_IntPtr_System_UInt32_System_UInt32_System_UInt32_System_UInt32_System_UInt32_System_UInt32_System_UInt32_System_UInt32_System_UInt32_) can always be used to update a Texture object, it is more efficient to first upload the data into an intermediate "Staging Texture", and then schedule a copy from that resource into the Texture you wish to render with. A "Staging Texture" is simply a Texture created with the [TextureUsage.Staging](xref:Veldrid.TextureUsage) usage flag.

Staging Textures can be "mapped" into a CPU-visible data range. This allows you to write directly into a region specifically prepared by the GPU driver to transfer texture data. After the data has been mapped and unmapped (see [GraphicsDevice.Map](xref:Veldrid.GraphicsDevice#Veldrid_GraphicsDevice_Map_Veldrid_MappableResource_Veldrid_MapMode_System_UInt32_) and [GraphicsDevice.Unmap](xref:Veldrid.GraphicsDevice#Veldrid_GraphicsDevice_Unmap_Veldrid_MappableResource_System_UInt32_)), you should then schedule a copy using [CommandList.CopyTexture](xref:Veldrid.CommandList#Veldrid_CommandList_CopyTexture_Veldrid_Texture_System_UInt32_System_UInt32_System_UInt32_System_UInt32_System_UInt32_Veldrid_Texture_System_UInt32_System_UInt32_System_UInt32_System_UInt32_System_UInt32_System_UInt32_System_UInt32_System_UInt32_System_UInt32_).

## Reading back Texture data

A Staging Texture is also needed to read back texture data from the GPU. A Texture should be mapped using [MapMode.Read](xref:Veldrid.MapMode) or [MapMode.ReadWrite](xref:Veldrid.MapMode).

## Initial Texture Data

Before a Texture is populated (using UpdateTexture, CopyTexture, or by drawing into it), its contents are undefined. It is not meaningful to copy this data back into a CPU buffer or manipulate it in any way. Some backends may default-initialize a Texture to a certain value in some cases (commonly transparent black, or all zeroes), but this behavior cannot be relied on.

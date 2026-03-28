# API-specific rendering differences

Veldrid does everything it can to eliminate differences between different rendering API's, and to provide a unified interface to the GPU. In some cases, there are fundamental differences between graphics API's that cannot be easily or efficiently reconciled. This document describes these differences and how they can be dealt with in your code.

## Clip Space Coordinates

Clip space coordinates differ by API. These are the coordinates that are output by your vertex shader and are consumed by the rasterizer. Each API's conventions are as follows:

Format: `[MinX,MaxX][MinY,MaxY][MinZ,MaxZ]`

* Direct3D11 and Metal: `[-1,1][-1,1][0,1]`
* Vulkan: `[-1,1][-1,1][0,1]` NOTE: Vulkan's clip space Y axis is inverted compared to other API's.
* OpenGL (ES): `[-1,1][-1,1][-1,1]`

The data types in the `System.Numerics` namespace follow Direct3D conventions. This means that the format of a `System.Numerics.Matrix4x4` is directly usable in the Direct3D11 and Metal backends. Other numerics libraries may match other graphics API's, depending on their intended use cases. Ideally, your application should work optimally with any of the clip space conventions listed above. There are two main approaches to dealing with these differences.

1. Use different projection matrices. Clip space depth is controlled by the format of your projection matrix. It is possible to select, at runtime, a different formula for your projection matrix which will map to the correct depth range of your API.

2. Fix up clip space coordinates in your vertex shader. It is very simple to invert `gl_Position.y` at the end of your Vulkan vertex shader in order to reconcile the difference between D3D11 and Vulkan clip space coordinates:

```
// End of Vulkan vertex shader
gl_Position.y = -gl_Position.y; // Correct for Vulkan clip coordinates
```

 Likewise, this can be used to reconcile the difference between D3D11 and OpenGL clip spaces:

```
// End of OpenGL vertex shader
gl_Position.z = gl_Position.z * 2.0 - gl_Position.w; // Correct for OpenGL clip coordinates
```

The above examples are assuming your application uses Direct3D 11 conventions. If you are starting with OpenGL conventions, for example, then you would need the "opposite" fix-up in the other backends.

## OpenGL framebuffer texture coordinates

OpenGL, unlike other graphics API's, has its texture coordinate origin in the bottom-left corner of a texture. Other API's have this origin in the top left. This results in all texture samples being flipped across the X axis. When sampling regular textures, there is no discernible difference, because texture data is also uploaded starting from that lower-left corner. When sampling a framebuffer attachment, however, the difference is visible, and the sampled data will appear to be flipped across the X axis. In order to reconcile this difference, there are two main options:

1. In your GLSL code, flip the Y value of the relevant texture coordinates. Since shaders are API-specific, this is a logical and straightforward place to put the logic.

2. When constructing the vertex data used in the framebuffer sampling operation, flip the Y values of your texture coordinates. For example, if you are constructing a quad onto which a framebuffer will be sampled, then the top two vertices should have a Y texture coordinate of 1, instead of 0. The bottom two vertices should have a Y texture coordinate of 0, instead of 1. And so on.

## Forcing Clip Space Consistency

When creating your GraphicsDevice, there are two flags that can be enabled to force all backends to use the same clip space range.

* [GraphicsDeviceOptions.PreferDepthRangeZeroToOne](xref:Veldrid.GraphicsDeviceOptions#Veldrid_GraphicsDeviceOptions_PreferDepthRangeZeroToOne): Attempts to force the device to use a [0, 1] clip space Z range.
* [GraphicsDeviceoptions.PreferStandardClipSpaceYDirection](xref:Veldrid.GraphicsDeviceOptions#Veldrid_GraphicsDeviceOptions_PreferStandardClipSpaceYDirection): Attempts to force the depth to use a standard bottom-to-top increasing clip space Y range.

Not all systems support these options. Even if you have requested a certain clip space range, the created GraphicsDevice may not support it. You can use the following properties to query the actual expected clip range of a created GraphicsDevice:

* [GraphicsDevice.IsDepthRangeZeroToOne](xref:Veldrid.GraphicsDevice#Veldrid_GraphicsDevice_IsDepthRangeZeroToOne)
* [GraphicsDevice.IsClipSpaceYInverted](xref:Veldrid.GraphicsDevice#Veldrid_GraphicsDevice_IsClipSpaceYInverted)
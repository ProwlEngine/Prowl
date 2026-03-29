---
uid: swapchains
---

# Swapchains

A [Swapchain](xref:NeoVeldrid.Swapchain) is a special kind of resource that provides the ability to present rendered images to an application view or surface. If you want the display any rendered images in NeoVeldrid, you need a Swapchain.

## Using a Swapchain

There are three main operations on a Swapchain: Rendering to it, presenting it, and resizing it.

Rendering to a Swapchain is accomplished by using its [Framebuffer](xref:NeoVeldrid.Swapchain#NeoVeldrid_Swapchain_Framebuffer) property. Bind this object using a [CommandList](xref:NeoVeldrid.CommandList), and then issue draw commands. A Swapchain's Framebuffer behaves the same as any other Framebuffer in NeoVeldrid.

Presenting a Swapchain is accomplished by calling [GraphicDevice.SwapBuffers(Swapchain)](xref:NeoVeldrid.GraphicsDevice#NeoVeldrid_GraphicsDevice_SwapBuffers_NeoVeldrid_Swapchain_). This should be called after the relevant drawing commands have been submitted.

Resizing a Swapchain is accomplished by calling [Resize(UInt32, UInt32)](xref:NeoVeldrid.Swapchain#NeoVeldrid_Swapchain_Resize_System_UInt32_System_UInt32_). This should generally be done as a response to a "Resized" event issued by whatever windowing or UI framework you are using. If the Swapchain is not resized when the application view changes, then your presented image will appear distorted.

## The Main Swapchain

NeoVeldrid has the notion of a "main Swapchain" (see [GraphicsDevice.MainSwapchain](xref:NeoVeldrid.GraphicsDevice#NeoVeldrid_GraphicsDevice_MainSwapchain)), which is the Swapchain created along with the GraphicsDevice itself. The main Swapchain is only created when certain GraphicsDevice.Create overloads are used:

* Overloads accepting a [SwapchainDescription](xref:NeoVeldrid.SwapchainDescription)
* Overloads accepting platform-specific window information, for example:
  * [GraphicsDevice.CreateD3D11(GraphicsDeviceOptions, IntPtr, UInt32, UInt32)](xref:NeoVeldrid.GraphicsDevice#NeoVeldrid_GraphicsDevice_CreateD3D11_NeoVeldrid_GraphicsDeviceOptions_System_IntPtr_System_UInt32_System_UInt32_)
  * [GraphicsDevice.CreateVulkan(GraphicsDeviceOptions, VkSurfaceSource, UInt32, UInt32)](xref:NeoVeldrid.GraphicsDevice#NeoVeldrid_GraphicsDevice_CreateVulkan_NeoVeldrid_GraphicsDeviceOptions_NeoVeldrid_Vk_VkSurfaceSource_System_UInt32_System_UInt32_)

Overloads that only accept a [GraphicsDeviceOptions](xref:NeoVeldrid.GraphicsDeviceOptions) parameter do NOT create a main Swapchain. When no main Swapchain has been created, it is not valid to use the GraphicsDevice.MainSwapchain property, or to call the methods which implicitly operate on the main Swapchain:

* [GraphicsDevice.SwapBuffers()](xref:NeoVeldrid.GraphicsDevice#NeoVeldrid_GraphicsDevice_SwapBuffers) _Parameter-less overload only_
* [GraphicsDevice.ResizeMainWindow(UInt32, UInt32)](xref:NeoVeldrid.GraphicsDevice#NeoVeldrid_GraphicsDevice_ResizeMainWindow_System_UInt32_System_UInt32_)
* [GraphicsDevice.SwapchainFramebuffer](xref:NeoVeldrid.GraphicsDevice#NeoVeldrid_GraphicsDevice_SwapchainFramebuffer)
* [GraphicsDevice.SyncToVerticalBlank](xref:NeoVeldrid.GraphicsDevice#NeoVeldrid_GraphicsDevice_SyncToVerticalBlank)

NOTE: Calling any of the above members is equivalent to accessing the same member on [GraphicsDevice.MainSwapchain](xref:NeoVeldrid.GraphicsDevice#NeoVeldrid_GraphicsDevice_MainSwapchain). There is no functional difference.

## Creating Swapchains

Like other resources, it is possible to create and destroy extra Swapchains at runtime, using a [ResourceFactory](xref:NeoVeldrid.ResourceFactory) and a [SwapchainDescription](xref:NeoVeldrid.SwapchainDescription). Constructing a SwapchainDescription requires a [SwapchainSource](xref:NeoVeldrid.SwapchainSource), which is a special kind of NeoVeldrid object representing a renderable surface that the application controls. Since this component interacts with the OS and its windowing system, it is platform-specific and created in a special way. There are a variety of static factory methods which allow you to create a SwapchainSource for a particular kind of operating system and UI framework. Due to platform limitations, each kind of SwapchainSource can only be used to create a Swapchain for a limited set of GraphicsDevice types.

| Method | Vulkan | Direct3D 11 | Metal | OpenGL ES |
| ------ | ------ | ----------- | ----- | --------- |
| [CreateWin32(IntPtr hwnd, IntPtr hinstance)](xref:NeoVeldrid.SwapchainSource#NeoVeldrid_SwapchainSource_CreateWin32_System_IntPtr_System_IntPtr_) | ✓ | ✓ | | |
| [CreateUwp(object swapChainPanel, float logicalDpi)](xref:NeoVeldrid.SwapchainSource#NeoVeldrid_SwapchainSource_CreateUwp_System_Object_System_Single_) | | ✓ | | |
| [CreateXlib(IntPtr display, IntPtr window)](xref:NeoVeldrid.SwapchainSource#NeoVeldrid_SwapchainSource_CreateXlib_System_IntPtr_System_IntPtr_) | ✓ | | | |
| [CreateNSWindow(IntPtr nsWindow)](xref:NeoVeldrid.SwapchainSource#NeoVeldrid_SwapchainSource_CreateNSWindow_System_IntPtr_) | ✓* | | ✓ | |
| [CreateNSView(IntPtr nsWindow)](xref:NeoVeldrid.SwapchainSource#NeoVeldrid_SwapchainSource_CreateNSView_System_IntPtr_) | | | ✓ | |
| [CreateUIView(IntPtr uiView)](xref:NeoVeldrid.SwapchainSource#NeoVeldrid_SwapchainSource_CreateUIView_System_IntPtr_) | ✓* | | ✓ | ✓ |
| [CreateAndroidSurface(IntPtr surfaceHandle, IntPtr jniEnv)](xref:NeoVeldrid.SwapchainSource#NeoVeldrid_SwapchainSource_CreateAndroidSurface_System_IntPtr_System_IntPtr_) | ✓ | | | ✓ |

_ * Vulkan support on macOS and iOS requires [MoltenVK](https://github.com/KhronosGroup/MoltenVK). _

A Swapchain can be created with or without a depth target. [SwapchainDescription.DepthFormat](xref:NeoVeldrid.SwapchainDescription#NeoVeldrid_SwapchainDescription_DepthFormat) controls this. If null, no depth target will be created. If non-null, a depth target will be created with that format.

## OpenGL

The OpenGL and OpenGL ES backends do not support multiple Swapchains. This is due to fundamental part of the design of OpenGL, and support cannot be synthesized or emulated efficently. Note that it is also not possible to create an OpenGL or OpenGL ES GraphicsDevice without a main Swapchain.
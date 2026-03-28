# Veldrid Implementation Overview

Veldrid is split roughly into two parts: the portable, public interface which user code references directly, and the internal "backends" which implement that portable interface. This section is provided to more technically describe the separation and interaction between these two parts.

Normal users of Veldrid may not be interested in this information, because it dives into the internal implementation details of the library, which is not necessary for ordinary, correct usage of the library. However, users familiar with a particular graphics API (OpenGL, Direct3D, etc.) may find it helpful to see how concepts from that API map to Veldrid. Users interested in contributing to Veldrid will also find this section useful.

The descriptions in the following sections reflect Veldrid as of version 4.2.0. As they are internal details, they may subtly change over time as optimizations and fixes are integrated into the codebase.

-------------------------------------

**[Public Interface](public-interface.md)**

Backends

* [Direct3D 11](d3d11.md)
* [Vulkan](vulkan.md)
* [Metal](metal.md)
* [OpenGL](opengl.md)
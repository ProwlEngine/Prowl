> [!NOTE]
> Prowl is currently in early development and not yet stable for production use. While the core functionality is in place, expect frequent changes, missing features, and potential bugs. Enthusiasts and contributors are welcome to explore and help shape the engine, but we recommend waiting for a more stable release before using it for serious game projects.

<img src="https://github.com/Kuvrot/Prowl/assets/23508114/5eef8da7-fb84-42f3-9d18-54b4f2d06551" width="100%" alt="Prowl logo image">

![Github top languages](https://img.shields.io/github/languages/top/michaelsakharov/prowl)
[![GitHub version](https://img.shields.io/github/v/release/michaelsakharov/prowl?include_prereleases&style=flat-square)](https://github.com/michaelsakharov/prowl/releases)
[![GitHub license](https://img.shields.io/github/license/michaelsakharov/prowl?style=flat-square)](https://github.com/michaelsakharov/prowl/blob/main/LICENSE.txt)
[![GitHub issues](https://img.shields.io/github/issues/michaelsakharov/prowl?style=flat-square)](https://github.com/michaelsakharov/prowl/issues)
[![GitHub stars](https://img.shields.io/github/stars/michaelsakharov/prowl?style=flat-square)](https://github.com/michaelsakharov/prowl/stargazers)
[![Discord](https://img.shields.io/discord/1151582593519722668?logo=discord
)](https://discord.gg/BqnJ9Rn4sn)

# <p align="center">🎮 An Open Source Unity-like Engine! 🎮</p>

<span id="readme-top"></span>

1. [About The Project](#-about-the-project-)
2. [Features](#-features-)
3. [Getting Started](#-getting-started-)
   * [Prerequisites](#prerequisites)
   * [Installation](#installation)
4. [Contributing](#-contributing-)
5. [Acknowledgments](#-acknowledgments-)
   * [Contributors](#contributors-)
   * [Dependencies](#dependencies-)
6. [License](#-license-)

# <span align="center">📝 About The Project 📝

Prowl is an open-source, **[MIT-licensed](#span-aligncenter-license-span)** game engine developed in **pure C# in latest .NET**.

It aims to provide a seamless transition for developers familiar with _Unity_ by maintaining a similar API while also following KISS and staying as small and customizable as possible. Ideally, _Unity_ projects can port over with as little resistance as possible.

Please keep in mind that Prowl is incredibly new and unstable, and it is not yet Game Ready, however, we are hopeful that Prowl will be stable and ready by the end of this year.

### [<p align="center">Join our Discord server! 🎉</p>](https://discord.gg/BqnJ9Rn4sn)

| ![Screenshot 2024-06-27 172952](https://github.com/michaelsakharov/Prowl/assets/8621606/80df58cc-53ac-4582-b722-1800d6cd4d13) | ![Screenshot 2024-06-27 172106](https://github.com/michaelsakharov/Prowl/assets/8621606/c13e9145-6b35-4ea5-ad66-523a275d0bc9) |
| :-: | :-: |
| ![image](https://github.com/michaelsakharov/Prowl/assets/8621606/91ab57be-b215-40a8-871b-baf1dfc9ea58) | ![image](https://github.com/michaelsakharov/Prowl/assets/8621606/1cc6bb14-7c41-46e9-a581-c79ba51fc45f) |
| ![image](https://github.com/michaelsakharov/Prowl/assets/8621606/b7fb26e0-568f-4bd7-9282-3e2fd12b38a9) | ![image](https://github.com/michaelsakharov/Prowl/assets/8621606/1b376ae7-8f13-41ea-ba1d-a49f777398ac) |
| ![UntitledFLightModel](https://github.com/michaelsakharov/Prowl/assets/8621606/58a3c640-6ace-4f2f-8de6-e3bf5bbf9865) | ![Untitled](https://github.com/michaelsakharov/Prowl/assets/8621606/5165f2c4-681f-4cf7-8579-1152c971d142) |

# <span align="center">✨ Features ✨</span>

-   **General:**
    - Cross-Platform! Windows, Linux & Mac!
    - Unity-like Editor & Scripting API
    - C# Scripting with .NET 9
    - GameObject & MonoBehaviour Component Architecture
    - Custom UI Framework ([Paper](https://github.com/ProwlEngine/Prowl.Paper))
        - Shared between Editor and In-Game UI
        - Immediate mode API with retained state
        - World Space UI via WorldCanvas
    - Vector Graphics & Text Rendering via [Quill](https://github.com/ProwlEngine/Prowl.Quill)
        - Slug (GPU-Accelerated Curve-Based) Text Rendering
        - Bitmap Text Rendering
    - Full-Featured Editor
        - Scene View, Hierarchy, Inspector, Project Browser, Console, Game View
        - Custom Component Editors, Property Editors, and Scene View Editors
        - Transform Gizmos (Move, Rotate, Scale)
        - Undo/Redo System
        - Dockable & Resizable Panels with Layout Persistence
        - Drag & Drop (Assets, GameObjects, Components)
        - Multi-Select & Search/Filtering in Editor Panels
        - Asset Thumbnail Generation & 3D Previews
        - Animation Curve & Gradient Editors
        - Rebindable Shortcut/Hotkey System
        - Editor Theming with Customizable Color Palettes
        - Playtest directly in the Editor
    - Physics using [Jitter Physics 2](https://github.com/notgiven688/jitterphysics2)
        - Colliders: Box, Sphere, Capsule, Cylinder, Cone, Convex Hull, Mesh, Model, Terrain
        - Constraints: Ball Socket, Hinge, Fixed Angle, Cone Limit, Distance Limit, Angular Motor, Linear Motor
        - Character Controller & Wheel Collider
        - Collision Layers & Filtering
        - Raycasting & Shape Casting
    - Audio via MiniAudio
        - Spatial 3D Audio with Attenuation & Doppler
        - Supports WAV, MP3, OGG, FLAC
        - Effect chain (Delay, Distortion, Biquad Filter, Reverb, Phaser) + custom `IAudioEffect`
    - Serialization via [Prowl.Echo](https://github.com/ProwlEngine/Prowl.Echo)
        - Graph → Custom Text Format
        - Graph → Binary
    - Tags & Layers System (32 Customizable Layers)
    - Scene System with Fog & Ambient Lighting
    - Prefabs with Nested Prefab Support
        - Apply, Revert, Break Instance & Override Tracking
    - Projects & Project Settings
    - Script Compilation via dotnet build (Game & Editor Assemblies)
    - Input Action System with Composites & Processors
        - `.inputactions` assets with a dedicated editor
        - Action phases (Disabled / Started / Performed / Cancelled)
        - Composite bindings (WASD → Float2, D-pad, etc.) for keyboard, mouse & gamepad
    - Math via [Prowl.Vector](https://github.com/ProwlEngine/Prowl.Vector)
        - Matrices (`Float4x4`), Quaternions, Transform2D
        - Shapes: AABB, Bounds, Frustum, Cone, Ray, Plane, LineSegment, Rect
        - Random distributions (OnUnitCircle, InUnitSphere, Rotation, etc.)
    - Build System - Build to Standalone Application
        - Packed Asset Files (.prowlpak)
        - Only exports used assets
        - Supports Windows, Mac & Linux

-   **Graphics Rendering:**
    - OpenGL Backend via [Silk.NET](https://github.com/dotnet/Silk.NET)
    - Extensible Render Pipeline (Custom Pipelines Supported)
    - Forward-Lit Pipeline with Depth + Normals Pre-Pass
    - Custom Shader Language with #include Support, Multi-Pass, and Shader Keywords/Variants
    - Node-Based **Shader Graph**
        - 150+ nodes across 14 categories (Math, Vector, Color, UV, Geometry, Scene Data, Lighting, Noise, Post-Effect, Utility, …)
        - Vertex-stage support (Position offset for wind / wobble / displacement)
        - Fragment + Vertex + DepthNormals + Shadow pass emission from one graph
        - Alpha cutout + vertex offset forwarded into shadow & depth passes automatically
        - Lighting modes: Unlit / PBR / Lambert / Blinn-Phong
        - Template seed graphs (Lit Basic / Transparent / Terrain / Grass / Particle / Sky / Post Effect / Custom Lighting)
        - Inline **Custom Code** (raw GLSL) node
        - Control flow (Branch), Local Get/Set variables
        - Noise nodes (FastNoiseLite): OpenSimplex2 / OpenSimplex2S / Perlin / Value / Cellular (Voronoi) / Domain Warp, with FBM / Ridged / PingPong fractal variants
    - HDR & PBR (Physically Based Rendering) - Metallic Workflow
        - Albedo, Normal, Surface (AO / Roughness / Metallic), Emission Maps
    - Mesh Renderer & Skinned Mesh Renderer with Bone Animation
    - Line Renderer
    - Render Textures & Texture3D
    - GPU Instancing & Frustum Culling
    - Point, Spot, and Directional Lights
        - All light types support Shadow Mapping
        - Cascaded Shadow Maps for Directional Lights (up to 4 cascades)
        - Cubemap Shadows for Point Lights
        - Shadow Atlas with Dynamic Packing
    - Post Processing
        - HDR Tonemapping (ACES / Reinhard / Uncharted / Filmic / Melon)
        - Bloom (dual-filter downsample/upsample)
        - FXAA (Fast Approximate Anti-Aliasing)
        - Screen Space Reflections (SSR)
        - Ground-Truth Ambient Occlusion (GTAO)
        - Bokeh Depth of Field
        - Volumetric Fog
        - Cinematic Effects (grain, vignette, chromatic aberration)
    - Forward-Rendered Transparency with Depth-Sorted Blending
    - Grab Pass (depth-aware) for refraction / heat-haze / frosted glass
    - Procedural / Cubemap / Gradient Skybox
    - Terrain System
        - Quadtree LOD
        - Heightmap & Splatmap Painting
        - GPU-Instanced Grass Rendering
        - Tree Rendering with LOD Distance
        - Dedicated Terrain Editor (Height, Paint, Grass, Trees, Settings)
    - Particle System
        - GPU-Instanced Rendering
        - Modules: Emission, Size/Color/Rotation/Velocity Over Lifetime, Collision, UV Animation
        - Local & World Simulation Spaces

-   **Asset Pipeline:**
    - GUID-Based Asset References with Meta Files
    - Import Caching & File Watching for Auto-Reimport
    - Custom Importers via Attributes
    - Sub-Assets with Deterministic GUIDs
    - Forward & Reverse Dependency Tracking
    - Supported Formats:
        - Models: GLTF, GLB, OBJ (Custom Importer, FBX Planned)
        - Textures: PNG, JPG, BMP, TGA, PSD, HDR, DDS, EXR (via Magick.NET)
        - Audio: WAV, MP3, OGG, FLAC

<p align="right">(<a href="#readme-top">back to top</a>)</p>

# <span align="center">🚀 Getting Started 🚀</span>

Getting Prowl up and running is super easy!

## Releases

> **Note**: There are no official releases yet so you need to download this repository to use Prowl!

## Build from source

### Prerequisites

* [.NET 9](https://dotnet.microsoft.com/en-us/download/dotnet/9.0)

### Installation

1. Clone the repo
2. Open `.sln` with your editor ([Visual Studio Version 17.8.0+](https://visualstudio.microsoft.com/vs/preview/), [VSCode](https://code.visualstudio.com/), [Rider]((https://www.jetbrains.com/rider/)), etc.)
3. Run `UpdateSubmodules.bat` (on Windows) or `UpdateSubmodules.sh` (on Linux)
4. That's it! 😄 🎉
<p align="right">(<a href="#readme-top">back to top</a>)</p>

# <span align="center">🤝 Contributing 🤝</span>

Check our [Contributing guide](//CONTRIBUTING.md) to see how to be part of this team.

<p align="right">(<a href="#readme-top">back to top</a>)</p>

# <span align="center">🙏 Acknowledgments 🙏</span>

- Hat tip to the creators of [Raylib](https://github.com/raysan5/raylib), While we are no longer based upon it, it has shaved off hours of development time getting the engine to a usable state.
- Some ideas/code have been taken from the amazing 2D Engine [Duality](https://github.com/AdamsLair/duality).

## Contributors 🌟

- [Michael (Wulferis)](https://twitter.com/Wulferis)
- [Abdiel Lopez (PaperPrototype)](https://github.com/PaperPrototype)
- [Josh Davis](https://github.com/10xJosh)
- [ReCore67](https://github.com/recore67)
- [Isaac Marovitz](https://github.com/IsaacMarovitz)
- [Kuvrot](https://github.com/Kuvrot)
- [JaggerJo](https://github.com/JaggerJo)
- [Jihad Khawaja](https://github.com/jihadkhawaja)
- [Jasper Honkasalo](https://github.com/japsuu)
- [Kai Angulo (k0t)](https://github.com/sinnwrig)
- [Bruno Massa](https://github.com/brmassa)
- [Mark Saba (ZeppelinGames)](https://github.com/ZeppelinGames)
- [EJTP (Unified)](https://github.com/EJTP)
- [Paolo (xZekro51)](https://github.com/xZekro51)
- [Kouame Benoit Junior Augustin (ZedDevStuff)](https://github.com/ZedDevStuff)

## Dependencies 📦

- [Silk.NET](https://github.com/dotnet/Silk.NET) - Windowing, Input, OpenGL & Audio Bindings
- [Jitter Physics 2](https://github.com/notgiven688/jitterphysics2) - Physics Engine
- [Magick.NET](https://github.com/dlemstra/Magick.NET) - Image Processing
- [Prowl.Echo](https://github.com/ProwlEngine/Prowl.Echo) - Serialization
- [Prowl.Paper](https://github.com/ProwlEngine/Prowl.Paper) - UI Framework
- [Prowl.Quill](https://github.com/ProwlEngine/Prowl.Quill) - Vector Graphics & Text Rendering
- [Prowl.Scribe](https://github.com/ProwlEngine/Prowl.Scribe) - TrueType font parsing, glyph rasterization & markdown layout
- [Prowl.Vector](https://github.com/ProwlEngine/Prowl.Vector) - 64-bit Math Library

<p align="right">(<a href="#readme-top">back to top</a>)</p>

# <span align="center">📜 License 📜</span>

Distributed under the MIT License. See [LICENSE](//LICENSE) for more information.

<p align="right">(<a href="#readme-top">back to top</a>)</p>

---

### [Join our Discord server! 🎉](https://discord.gg/BqnJ9Rn4sn)
[![Discord](https://img.shields.io/discord/1151582593519722668?logo=discord
)](https://discord.gg/BqnJ9Rn4sn)


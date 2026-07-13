<img src="https://github.com/Kuvrot/Prowl/assets/23508114/5eef8da7-fb84-42f3-9d18-54b4f2d06551" width="100%" alt="Prowl logo image">

![Github top languages](https://img.shields.io/github/languages/top/ProwlEngine/Prowl)
[![GitHub version](https://img.shields.io/github/v/release/ProwlEngine/Prowl?include_prereleases&style=flat-square)](https://github.com/ProwlEngine/Prowl/releases)
[![GitHub license](https://img.shields.io/github/license/ProwlEngine/Prowl?style=flat-square)](https://github.com/ProwlEngine/Prowl/blob/main/LICENSE.txt)
[![GitHub issues](https://img.shields.io/github/issues/ProwlEngine/Prowl?style=flat-square)](https://github.com/ProwlEngine/Prowl/issues)
[![GitHub stars](https://img.shields.io/github/stars/ProwlEngine/Prowl?style=flat-square)](https://github.com/ProwlEngine/Prowl/stargazers)
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

Prowl is currently in **1.0-preview**, following a complete rewrite of the Editor, renderer, physics, audio, and UI. Projects made with older versions of Prowl are not compatible with 1.0-preview, there is no migration path. Until the final 1.0 release, further breaking changes are still possible.

### [<p align="center">Join our Discord server! 🎉</p>](https://discord.gg/BqnJ9Rn4sn)

| ![Screenshot](https://github.com/user-attachments/assets/f124906e-c403-4618-93e7-461b39ba4deb) | ![Screenshot](https://github.com/user-attachments/assets/956c6f00-2052-464e-b426-0b3cdbbe45de) |
| :-: | :-: |
| ![image](https://github.com/user-attachments/assets/974cc488-379c-4db8-bd39-ff6024e341c6) | ![image](https://github.com/user-attachments/assets/5b00b701-5b61-4fd1-afaa-265ef9d578e7) |
| ![image](https://github.com/user-attachments/assets/e0ec6307-2368-4df5-b7a8-ef7665df2207) | ![image](https://github.com/user-attachments/assets/e59d63d2-d2d9-4ddb-afa7-4a465caa2cc9) |
| ![UntitledFLightModel](https://github.com/user-attachments/assets/71486b58-a81f-440a-ad43-cabdb1e6d6ba) | ![Untitled](https://github.com/user-attachments/assets/4255a0fe-689f-4696-b062-2d62ca35a23c) |

# <span align="center">✨ Features ✨</span>

-   **General:**
    - Cross-Platform! Windows, Linux & Mac, for both the Editor and exported builds
    - Unity-like Editor & Scripting API
    - C# Scripting with .NET 10
    - GameObject & MonoBehaviour Component Architecture
    - **Prowl.Runtime works fully standalone from the Editor** - reference it directly and ship a game with zero Editor dependency
    - Custom Immediate Mode UI ([Paper](https://github.com/ProwlEngine/Prowl.Paper)), Editor built on top of [Origami](https://github.com/ProwlEngine/Prowl.Origami)
    - Vector Graphics & Text Rendering via [Quill](https://github.com/ProwlEngine/Prowl.Quill)
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
        - Editor Theming with Customizable Color Palettes and sizing
        - Playtest directly in the Editor
        - Hot-Reloading Scripts
        - Localization - English, German, Spanish, French, Italian, Japanese, Korean, Polish, Portuguese, Russian, Turkish & Chinese
        - Managed & Native Plugins with Assembly Definitions
    - Physics using [Jitter Physics 2](https://github.com/notgiven688/jitterphysics2)
        - Colliders: Box, Sphere, Capsule, Cylinder, Cone, Convex Hull, Mesh, Model, Terrain
        - Wheel Collider (raycast-based vehicle wheel, with suspension & slip-based grip)
        - Joints & Constraints: Ball Socket, Hinge Joint, Hinge Angle, Fixed Angle, Cone Limit, Distance Limit, Twist Angle, Prismatic, Universal, Point On Line, Point On Plane, Angular Motor, Linear Motor
        - Character Controller
        - Trigger Volumes (Box, Sphere, Capsule)
        - Collision Layers & Filtering (LayerMask)
        - Raycasting & Shape Query API
    - Audio via MiniAudio
        - Spatial 3D Audio with Attenuation & Doppler
        - Supports WAV, MP3, OGG, FLAC
        - Effect chain (Delay, Distortion, Biquad Filter, Reverb, Phaser) + custom `IAudioEffect`
    - Serialization via [Prowl.Echo](https://github.com/ProwlEngine/Prowl.Echo)
    - Tags & Layers System
    - Scene System with Fog & Ambient Lighting
    - Prefabs with Nested Prefab Support
        - Apply, Revert, Break Instance & Override Tracking
    - Projects & Project Settings
    - Script Compilation via dotnet build (Game & Editor Assemblies)
    - Input Action System with Composites & Processors
        - `.inputactions` assets with a dedicated editor
        - Action phases (Disabled / Started / Performed / Cancelled)
        - Composite bindings (WASD → Float2, D-pad, etc.) for keyboard, mouse & gamepad
    - GameObject-Based UI, including World Space UI
        - `RectTransform`-driven layout, Buttons, Sliders, layout groups, drag & drop event handlers
    - Prowl Actions - persistent, inspector-configurable event callbacks
    - Math via [Prowl.Vector](https://github.com/ProwlEngine/Prowl.Vector)
        - Matrices (`Float4x4`), Quaternions, Transform2D
        - Shapes: AABB, Bounds, Frustum, Cone, Ray, Plane, LineSegment, Rect
    - Build System - Build to Standalone Application
        - Packed Asset Files (.prowlpak)
        - Only exports used assets
        - Per-platform build profiles
        - Supports Windows, Mac & Linux
    - Unit Tested - 450+ tests across the Runtime and Editor

-   **Graphics Rendering:**
    - OpenGL Backend via [Silk.NET](https://github.com/dotnet/Silk.NET)
	- Dedicated Render Thread
    - Extensible Render Pipeline (Custom Pipelines Supported)
    - Forward-Lit Pipeline with Thin G-Buffer Pre-Pass (Depth, Normals, Motion, Roughness, Metallic)
	- UV-Unwrapping via [Prowl.Unwrapper](https://github.com/ProwlEngine/Prowl.Unwrapper), Progressive Lightmapper via [Prowl.Photonic](https://github.com/ProwlEngine/Prowl.Photonic)
	- Baked Light Probes
    - Custom Shader Language with #include Support, Multi-Pass, and Shader Keywords/Variants
    - HDR & PBR (Physically Based Rendering) - Metallic Workflow
        - Albedo, Normal, Surface (AO / Roughness / Metallic), Emission Maps
    - Mesh Renderer & Skinned Mesh Renderer with Bone Animation and Blendshapes
    - Line Renderer
    - Sprites, with Sprite Sheet slicing (Grid, Isometric & Automatic alpha-based slicing) and a dedicated Sprite Editor
    - Render Textures & Texture3D
    - GPU Instancing & Frustum Culling
    - Point, Spot, and Directional Lights
        - All light types support Shadow Mapping
        - Cascaded Shadow Maps for Directional Lights (up to 4 cascades)
        - Cubemap Shadows for Point Lights
        - Shadow Atlas with Dynamic Packing
    - Post Processing
        - HDR Tonemapping (ACES / Reinhard / Uncharted / Filmic / Melon / AgX)
        - Bloom (dual-filter downsample/upsample)
        - FXAA (Fast Approximate Anti-Aliasing)
		- TAA (Temporal Anti-Aliasing)
        - Ground-Truth Ambient Occlusion (GTAO)
		- Stochastic Screen Space Reflections (SSR)
        - Bokeh Depth of Field
        - Volumetric Fog
        - Cinematic Effects (grain, vignette, chromatic aberration)
    - Transparency
    - Grab Pass (depth-aware) for refraction / heat-haze / frosted glass
    - Procedural / Cubemap / Gradient Skybox
    - Terrain System
        - Quadtree LOD
        - Heightmap & Splatmap Painting
        - GPU-Instanced Grass Rendering
        - Tree Rendering with LOD Distance
        - Dedicated Terrain Editor (Height, Paint, Grass, Trees, Settings)
		- Holes
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
	- Threaded Asset Loading
    - Supported Formats:
        - Models: GLTF, GLB, OBJ, FBX (via [Prowl.Clay](https://github.com/ProwlEngine/Prowl.Clay))
        - Textures: PNG, JPG, BMP, TGA, PSD, HDR, DDS, EXR (via Magick.NET)
        - Audio: WAV, MP3, OGG, FLAC

<p align="right">(<a href="#readme-top">back to top</a>)</p>

# <span align="center">🚀 Getting Started 🚀</span>

Getting Prowl up and running is super easy!

## Releases

> **Note**: Prowl is now at **1.0-preview**, grab it from the [Releases page](https://github.com/ProwlEngine/Prowl/releases). Projects made with older, pre-1.0-preview versions of Prowl are not compatible and cannot be migrated.

## Build from source

### Prerequisites

* [.NET 10](https://dotnet.microsoft.com/en-us/download/dotnet/10.0)

### Installation

1. Clone the repo
2. Open `.sln` with your editor ([Visual Studio Version 17.8.0+](https://visualstudio.microsoft.com/vs/preview/), [VSCode](https://code.visualstudio.com/), [Rider]((https://www.jetbrains.com/rider/)), etc.)
3. That's it! 😄 🎉
<p align="right">(<a href="#readme-top">back to top</a>)</p>

# <span align="center">🤝 Contributing 🤝</span>

Check our [Contributing guide](//CONTRIBUTING.md) to see how to be part of this team.

<p align="right">(<a href="#readme-top">back to top</a>)</p>

# <span align="center">🙏 Acknowledgments 🙏</span>

- Hat tip to the creators of [Raylib](https://github.com/raysan5/raylib), While we are no longer based upon it, it has shaved off hours of development time getting the engine to a usable state.

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
- [Chandler Cox (Tryibion)](https://github.com/Tryibion)
- [EJTP (Unified)](https://github.com/EJTP)
- [Paolo (xZekro51)](https://github.com/xZekro51)
- [Kouame Benoit Junior Augustin (ZedDevStuff)](https://github.com/ZedDevStuff)

## Dependencies 📦

- [Silk.NET](https://github.com/dotnet/Silk.NET) - Windowing, Input, OpenGL & Audio Bindings
- [Jitter Physics 2](https://github.com/notgiven688/jitterphysics2) - Physics Engine
- [Magick.NET](https://github.com/dlemstra/Magick.NET) - Image Processing
- [Prowl.Echo](https://github.com/ProwlEngine/Prowl.Echo) - Serialization
- [Prowl.Paper](https://github.com/ProwlEngine/Prowl.Paper) - UI Framework
- [Prowl.Origami](https://github.com/ProwlEngine/Prowl.Origami) - Component Library for Paper
- [Prowl.Quill](https://github.com/ProwlEngine/Prowl.Quill) - Vector Graphics & Text Rendering
- [Prowl.Scribe](https://github.com/ProwlEngine/Prowl.Scribe) - TrueType font parsing, glyph rasterization & markdown layout
- [Prowl.Rosetta](https://github.com/ProwlEngine/Prowl.Rosetta) - For Editor Localisation
- [Prowl.Vector](https://github.com/ProwlEngine/Prowl.Vector) - 64-bit Math Library
- [Prowl.Unwrapper](https://github.com/ProwlEngine/Prowl.Unwrapper) - UV Unwrapper
- [Prowl.Photonic](https://github.com/ProwlEngine/Prowl.Photonic) - Progressive Lightmapper
- [Prowl.Clay](https://github.com/ProwlEngine/Prowl.Clay) - Model Importing (GLTF, GLB, OBJ, FBX)

<p align="right">(<a href="#readme-top">back to top</a>)</p>

# <span align="center">📜 License 📜</span>

Distributed under the MIT License. See [LICENSE](//LICENSE) for more information.

<p align="right">(<a href="#readme-top">back to top</a>)</p>

---

### [Join our Discord server! 🎉](https://discord.gg/BqnJ9Rn4sn)
[![Discord](https://img.shields.io/discord/1151582593519722668?logo=discord
)](https://discord.gg/BqnJ9Rn4sn)


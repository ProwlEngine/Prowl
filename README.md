<img src="https://i.imgur.com/qoJ4RTS.png" width="100%" alt="Prowl logo image">

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

Prowl is an open-source, **[MIT-licensed](https://github.com/ProwlEngine/Prowl/blob/main/LICENSE)** game engine developed in **pure C# in latest .NET**.

It aims to provide a seamless transition for developers familiar with _Unity_ by maintaining a similar API while also following KISS and staying as small and customizable as possible. Ideally, _Unity_ projects can port over with as little resistance as possible.

Prowl is currently in **1.0 Preview 5**, which brings a new Vulkan renderer, animation, navigation, VR and a rewritten asset system. Projects from earlier 1.0 previews are backed up and migrated automatically when opened (However this migration isnt gauranteed during Preview Releases always backup!). Until the final 1.0 release, further breaking changes are expected.

### [<p align="center">Join our Discord server! 🎉</p>](https://discord.gg/BqnJ9Rn4sn)

[![ko-fi](https://ko-fi.com/img/githubbutton_sm.svg)](https://ko-fi.com/R7B4280JI3)

<img width="100%" alt="image 5" src="https://i.imgur.com/hDXtuv5.png" />

| ![Screenshot](https://i.imgur.com/khCaEB5.png) | ![Screenshot](https://i.imgur.com/qNeMvC4.jpeg) |
| :-: | :-: |
| ![image](https://i.imgur.com/xooqeDx.png) | ![image](https://i.imgur.com/zbCJOwI.png) |
| ![UntitledFLightModel](https://i.imgur.com/TkJOmw9.png) | ![Untitled](https://i.imgur.com/BXn31d0.png) |
| ![UntitledFLightModel](https://i.imgur.com/rLGLo7h.png) | ![Untitled](https://i.imgur.com/jbKCif2.png) |

# <span align="center">✨ Features ✨</span>

-   **General:**
    - Cross-Platform! Windows, Linux & Mac, for both the Editor and exported builds with Andriod under development!
    - Unity-like Editor & Scripting API
    - C# Scripting with .NET 10, compiled in process with Roslyn
    - GameObject & Component Architecture
    - **Prowl.Runtime works fully standalone from the Editor** - reference it directly and ship a game with zero Editor dependency
    - Custom Immediate Mode UI ([Paper](https://github.com/ProwlEngine/Anthology)), Editor built on top of [Origami](https://github.com/ProwlEngine/Anthology)
    - Vector Graphics & Text Rendering via [Quill](https://github.com/ProwlEngine/Anthology)
    - `GameTask` async API (`MainThread`, `WorkerThread`, `NextFrame`, `Delay`, `WaitUntil`) with per session cancellation
    - Roslyn analyzers that catch common scripting mistakes (component constructors, blocking waits, `?.` on engine objects)
    - Serialization via [Prowl.Echo](https://github.com/ProwlEngine/Anthology)
    - Tags & Layers System
    - Scenes with `Scene.Environment` (sky, ambient, fog), async loading and `DontDestroyOnLoad`
    - Prefabs
        - Apply, Revert, Break Instance & Override Tracking
    - Input Action System with Composites & Processors
        - `.inputactions` assets with a dedicated editor
        - Action phases (Disabled / Started / Performed / Cancelled)
        - Composite bindings (WASD to Float2, D-pad, etc.) for keyboard, mouse, gamepad & XR controllers
    - GameObject-Based UI, including World Space UI
        - `RectTransform`-driven layout, Buttons, Toggles, Sliders, Dropdowns, layout groups, drag & drop event handlers
    - Math via [Prowl.Vector](https://github.com/ProwlEngine/Anthology)
        - SIMD backed vectors, Matrices (`Float4x4`), Quaternions, Transform2D
        - Shapes: AABB, Bounds, Frustum, Cone, Ray, Plane, LineSegment, Rect
        - Mesh editing operators & Quadric mesh simplification
    - Frame Stats profiler with custom scopes

-   **Editor:**
    - Scene View, Hierarchy, Inspector, Project Browser, Console, Game View, Frame Stats
    - Custom Component Editors, Property Editors, and Scene View Editors
    - Transform Gizmos (Move, Rotate, Scale), shift drag to duplicate
    - Undo/Redo System
    - Dockable & Resizable Panels with Layout Persistence and Panel Maximize
    - Drag & Drop (Assets, GameObjects, Components)
    - Multi-Select & Search/Filtering in Editor Panels
    - Asset Thumbnail Generation & 3D Previews
    - Animation Graph, Avatar, Animation Curve & Gradient Editors, plus a Ragdoll Generator
    - Volume Profile, Input Action, Sprite and Terrain Editors
    - Rebindable Shortcut/Hotkey System
    - 12 built in Themes with Customizable Color Palettes, sizing and wallpapers
    - Playtest directly in the Editor
    - Hot-Reloading Scripts
    - Localization - English, German, Spanish, French, Italian, Japanese, Korean, Polish, Portuguese, Russian, Turkish & Chinese
    - Managed & Native Plugins with Assembly Definitions
    - **Prowl CLI** - drive a running editor from scripts or AI agents, projects ship with agent skills

-   **Graphics Rendering:**
    - Vulkan via [Prowl.Graphite](https://github.com/ProwlEngine/Anthology) (MoltenVK on macOS)
    - GPU Driven Renderer: render graph, dedicated render thread, bindless textures, vertex pulling, multi draw indirect
    - Camera relative rendering
    - H-Forward Lighting and cached static shadows
    - Render Features API and pluggable Camera Pipelines for custom passes and designs
    - Prism, a fully managed GLSL to SPIR-V compiler, with async variant compiles, a SPIR-V cache and shaders precompiled into builds
    - Lit & Unlit Shaders, Surface Shaders, Material Variants, Shader Keywords/Variants
    - HDR & PBR (Physically Based Rendering) - Metallic Workflow
        - Albedo, Normal, Surface (AO / Roughness / Metallic), Emission, Parallax, Translucency & Anisotropy
    - Compute Shaders & Compute Buffers
    - Mesh Renderer & Skinned Mesh Renderer with GPU Blendshapes
    - Line Renderer
    - Sprites, with Sprite Sheet slicing (Grid, Isometric & Automatic alpha-based slicing) and a dedicated Sprite Editor
    - Render Textures, Texture3D & Cubemaps
    - Lights: Directional, Point, Spot and Area (Rect, Disk, Tube)
        - Sphere lights, sun disk, Light Cookies & IES Profiles
        - Shadow Atlas with static caching and screen size driven resolution
        - Cascaded Shadow Maps for Directional Lights (up to 4 cascades)
        - Point Light Shadows
        - Hard, Tent & Contact Hardening shadow filters
    - Decals (Albedo, Normal & Mask)
    - Reflection Probes (Baked & Realtime) with box projection and a Sky Probe
    - UV-Unwrapping via [Prowl.Unwrapper](https://github.com/ProwlEngine/Anthology), Progressive Lightmapper via [Prowl.Photonic](https://github.com/ProwlEngine/Anthology)
    - Baked Light Probes
    - Skies: Procedural Atmosphere, Gradient, Cubemap, Panorama & Six Sided
    - Volumetric Clouds & Volumetric Fog
    - Camera Stacking, Camera Cuts & Debug View Modes
    - Tonemapping (ACES, AgX, Reinhard, RomBinDaHouse, Uncharted 2)
    - Anti Aliasing: FXAA, SMAA & TAA
    - Volume based Post Processing with global & local volumes
        - Auto Exposure, Bloom, GTAO, Screen Space Reflections, Motion Blur, Depth of Field, God Rays
        - Vignette, Chromatic Aberration, Film Grain, Color Grading, Color Lookup (LUT), Sharpen, Edge Detection, Pixelation
        - Custom Post Effects
    - Transparency & Refraction
    - Terrain System
        - Quadtree LOD
        - Heightmap & Splatmap Painting
        - Grass, Details & Trees
        - Holes
    - Particle System
        - Shape, Emission, Sub Emitters, Texture Sheet Animation, Trails, Collision, Forces, Over Lifetime and by Speed modules
        - Local & World Simulation Spaces

-   **Animation:**
    - `Animator` with Animation Graphs via [Prowl.Motion](https://github.com/ProwlEngine/Anthology), about 90 nodes
    - State Machines, Blend Spaces, Layers & Masks, Inertial Blending
    - Humanoid Avatars with Retargeting between rigs
    - IK (Two Bone, Look At, Aim), Foot Grounding, Root Motion & Warping
    - Active Ragdolls with muscles & pinning

-   **Physics:**
    - Physics using [Jitter Physics 2](https://github.com/notgiven688/jitterphysics2)
    - Colliders: Box, Sphere, Capsule, Cylinder, Cone, Mesh (Convex or Concave), Terrain
    - Wheel Collider with friction curves, weight transfer, Anti Roll Bars & per surface grip
    - Joints & Constraints: Ball Socket, Hinge Joint, Hinge Angle, Fixed Angle, Cone Limit, Distance Limit, Twist Angle, Prismatic, Universal, Point On Line, Point On Plane, Angular Motor, Linear Motor, Drive
    - Character Controller with moving platforms, custom gravity, steps & slopes
    - Trigger Volumes & Collision Callbacks
    - Collision Layers & Filtering (LayerMask)
    - Raycasting & Shape Query API

-   **Navigation:**
    - NavMesh via [Prowl.Recast](https://github.com/ProwlEngine/Anthology)
    - NavMesh Surfaces, Agents, Obstacles (carving), Links & Modifiers
    - Area costs, Crowd Steering, background baking & partial rebuilds

-   **Audio:**
    - Audio via miniaudio
    - Spatial 3D Audio with Attenuation & Doppler
    - Audio Mixers & Groups
    - Effect chain (Delay, Distortion, Biquad Filter, Reverb, Phaser) + custom `IAudioEffect`

-   **VR:**
    - Desktop VR through OpenXR (SteamVR, Meta and other runtimes)
    - Stereo rendering with multiview
    - Controllers, Haptics, Hand Tracking & Eye Gaze
    - World Space UI Pointers

-   **Asset Pipeline:**
    - GUID-Based Asset References with Meta Files
    - One object per asset, refilled in place on reimport
    - Import Caching & File Watching for Auto-Reimport
    - Custom Importers via Attributes
    - Sub-Assets with Deterministic GUIDs
    - Forward & Reverse Dependency Tracking
    - Async Loading & Residency Management with a memory budget
    - Runtime Asset Mounting (folders, zips, HTTP) and Runtime Importers for mods & user content
    - Supported Formats:
        - Models: GLTF, GLB, OBJ, FBX (via [Prowl.Clay](https://github.com/ProwlEngine/Anthology))
        - Textures: PNG, JPG, BMP, GIF, TGA, TIFF, WEBP, PSD, DDS, EXR, HDR and more (via [Prowl.Aperture](https://github.com/ProwlEngine/Anthology))
        - Audio: WAV, MP3, OGG, FLAC
        - Fonts: TTF, OTF

-   **Build System:**
    - Build to Standalone Application
    - Packed Asset Files (.prowlpak)
    - Only exports used assets
    - Used Shader Variants precompiled into the build
    - Per-platform build profiles
    - Native AOT Player Builds (experimental)
    - Supports Windows, Mac & Linux (Andriod under development)

-   **Samples:**
    - HelloProwl, Rendering, ManyLights, Physics, Controller, Vehicle, Particle, Scripting, VRBasics & VR Showcases under `Samples/Runtime`

<p align="right">(<a href="#readme-top">back to top</a>)</p>

# <span align="center">🚀 Getting Started 🚀</span>

Getting Prowl up and running is super easy!

### Prerequisites

* [.NET 10](https://dotnet.microsoft.com/en-us/download/dotnet/10.0)
* A GPU with Vulkan 1.2 support (macOS runs through MoltenVK)

## Releases

> **Note**: Prowl is now at **1.0 Preview 5**, grab it from the [Releases page](https://github.com/ProwlEngine/Prowl/releases).

## Build from source


### Installation

1. Clone the repo
2. Open `Prowl.slnx` with your editor ([Visual Studio](https://visualstudio.microsoft.com/), [VSCode](https://code.visualstudio.com/), [Rider](https://www.jetbrains.com/rider/), etc.)
3. Build and run `Prowl.Editor`
4. That's it! 😄 🎉
<p align="right">(<a href="#readme-top">back to top</a>)</p>

# <span align="center">🤝 Contributing 🤝</span>

Check our [Contributing guide](https://github.com/ProwlEngine/Prowl/blob/main/CONTRIBUTING.md) to see how to be part of this team.

<p align="right">(<a href="#readme-top">back to top</a>)</p>

# <span align="center">🙏 Acknowledgments 🙏</span>

- Hat tip to the creators of [Raylib](https://github.com/raysan5/raylib), While we are no longer based upon it, it has shaved off hours of development time getting the engine to a usable state.

## Contributors 🌟

<!-- readme: collaborators,contributors -start -->
<table>
	<tbody>
		<tr>
            <td align="center">
                <a href="https://github.com/michaelsakharov">
                    <img src="https://avatars.githubusercontent.com/u/8621606?v=4" width="100;" alt="michaelsakharov"/>
                    <br />
                    <sub><b>Wulferis</b></sub>
                </a>
            </td>
            <td align="center">
                <a href="https://github.com/sinnwrig">
                    <img src="https://avatars.githubusercontent.com/u/116330012?v=4" width="100;" alt="sinnwrig"/>
                    <br />
                    <sub><b>Kai Angulo</b></sub>
                </a>
            </td>
            <td align="center">
                <a href="https://github.com/PaperPrototype">
                    <img src="https://avatars.githubusercontent.com/u/48071553?v=4" width="100;" alt="PaperPrototype"/>
                    <br />
                    <sub><b>Abdiel Lopez</b></sub>
                </a>
            </td>
            <td align="center">
                <a href="https://github.com/xZekro51">
                    <img src="https://avatars.githubusercontent.com/u/28503323?v=4" width="100;" alt="xZekro51"/>
                    <br />
                    <sub><b>Paolo</b></sub>
                </a>
            </td>
            <td align="center">
                <a href="https://github.com/dimmerly">
                    <img src="https://avatars.githubusercontent.com/u/223179705?v=4" width="100;" alt="dimmerly"/>
                    <br />
                    <sub><b>Dimmer</b></sub>
                </a>
            </td>
            <td align="center">
                <a href="https://github.com/Acissathar">
                    <img src="https://avatars.githubusercontent.com/u/10227954?v=4" width="100;" alt="Acissathar"/>
                    <br />
                    <sub><b>Will</b></sub>
                </a>
            </td>
		</tr>
		<tr>
            <td align="center">
                <a href="https://github.com/brmassa">
                    <img src="https://avatars.githubusercontent.com/u/13696218?v=4" width="100;" alt="brmassa"/>
                    <br />
                    <sub><b>Bruno Massa</b></sub>
                </a>
            </td>
            <td align="center">
                <a href="https://github.com/EJTP">
                    <img src="https://avatars.githubusercontent.com/u/87308197?v=4" width="100;" alt="EJTP"/>
                    <br />
                    <sub><b>EJTP</b></sub>
                </a>
            </td>
            <td align="center">
                <a href="https://github.com/Kuvrot">
                    <img src="https://avatars.githubusercontent.com/u/23508114?v=4" width="100;" alt="Kuvrot"/>
                    <br />
                    <sub><b>Jaime Arturo</b></sub>
                </a>
            </td>
            <td align="center">
                <a href="https://github.com/IsaacMarovitz">
                    <img src="https://avatars.githubusercontent.com/u/42140194?v=4" width="100;" alt="IsaacMarovitz"/>
                    <br />
                    <sub><b>Isaac Marovitz</b></sub>
                </a>
            </td>
            <td align="center">
                <a href="https://github.com/10xJosh">
                    <img src="https://avatars.githubusercontent.com/u/55113224?v=4" width="100;" alt="10xJosh"/>
                    <br />
                    <sub><b>Josh Davis</b></sub>
                </a>
            </td>
            <td align="center">
                <a href="https://github.com/ZeppelinGames">
                    <img src="https://avatars.githubusercontent.com/u/31507417?v=4" width="100;" alt="ZeppelinGames"/>
                    <br />
                    <sub><b>Mark Saba</b></sub>
                </a>
            </td>
		</tr>
		<tr>
            <td align="center">
                <a href="https://github.com/ZedDevStuff">
                    <img src="https://avatars.githubusercontent.com/u/98429642?v=4" width="100;" alt="ZedDevStuff"/>
                    <br />
                    <sub><b>Kouame Benoit Junior Augustin</b></sub>
                </a>
            </td>
            <td align="center">
                <a href="https://github.com/keithwill">
                    <img src="https://avatars.githubusercontent.com/u/33398353?v=4" width="100;" alt="keithwill"/>
                    <br />
                    <sub><b>Keith Williams</b></sub>
                </a>
            </td>
            <td align="center">
                <a href="https://github.com/Tryibion">
                    <img src="https://avatars.githubusercontent.com/u/71274967?v=4" width="100;" alt="Tryibion"/>
                    <br />
                    <sub><b>Chandler Cox</b></sub>
                </a>
            </td>
            <td align="center">
                <a href="https://github.com/ArtemkaKun">
                    <img src="https://avatars.githubusercontent.com/u/36485221?v=4" width="100;" alt="ArtemkaKun"/>
                    <br />
                    <sub><b>Artem Yurchenko</b></sub>
                </a>
            </td>
            <td align="center">
                <a href="https://github.com/Porphyri0n">
                    <img src="https://avatars.githubusercontent.com/u/119479540?v=4" width="100;" alt="Porphyri0n"/>
                    <br />
                    <sub><b>Erdoğan Başer</b></sub>
                </a>
            </td>
            <td align="center">
                <a href="https://github.com/jihadkhawaja">
                    <img src="https://avatars.githubusercontent.com/u/47315122?v=4" width="100;" alt="jihadkhawaja"/>
                    <br />
                    <sub><b>Jay Khawaja</b></sub>
                </a>
            </td>
		</tr>
		<tr>
            <td align="center">
                <a href="https://github.com/JaggerJo">
                    <img src="https://avatars.githubusercontent.com/u/13090415?v=4" width="100;" alt="JaggerJo"/>
                    <br />
                    <sub><b>Josua Jäger</b></sub>
                </a>
            </td>
            <td align="center">
                <a href="https://github.com/paulbartrum">
                    <img src="https://avatars.githubusercontent.com/u/1572571?v=4" width="100;" alt="paulbartrum"/>
                    <br />
                    <sub><b>paulbartrum</b></sub>
                </a>
            </td>
            <td align="center">
                <a href="https://github.com/mrunion">
                    <img src="https://avatars.githubusercontent.com/u/5022458?v=4" width="100;" alt="mrunion"/>
                    <br />
                    <sub><b>Matt Runion</b></sub>
                </a>
            </td>
            <td align="center">
                <a href="https://github.com/japsuu">
                    <img src="https://avatars.githubusercontent.com/u/55388432?v=4" width="100;" alt="japsuu"/>
                    <br />
                    <sub><b>Jasper Honkasalo</b></sub>
                </a>
            </td>
            <td align="center">
                <a href="https://github.com/UBTL">
                    <img src="https://avatars.githubusercontent.com/u/105142453?v=4" width="100;" alt="UBTL"/>
                    <br />
                    <sub><b>Uncle Bane</b></sub>
                </a>
            </td>
            <td align="center">
                <a href="https://github.com/draxxris">
                    <img src="https://avatars.githubusercontent.com/u/24984408?v=4" width="100;" alt="draxxris"/>
                    <br />
                    <sub><b>draxxris</b></sub>
                </a>
            </td>
		</tr>
		<tr>
            <td align="center">
                <a href="https://github.com/JothamR">
                    <img src="https://avatars.githubusercontent.com/u/5410231?v=4" width="100;" alt="JothamR"/>
                    <br />
                    <sub><b>Jotham</b></sub>
                </a>
            </td>
            <td align="center">
                <a href="https://github.com/notgiven688">
                    <img src="https://avatars.githubusercontent.com/u/37874600?v=4" width="100;" alt="notgiven688"/>
                    <br />
                    <sub><b>notgiven688</b></sub>
                </a>
            </td>
            <td align="center">
                <a href="https://github.com/recore67">
                    <img src="https://avatars.githubusercontent.com/u/29689492?v=4" width="100;" alt="recore67"/>
                    <br />
                    <sub><b>salman ameeri</b></sub>
                </a>
            </td>
            <td align="center">
                <a href="https://github.com/treviasxk">
                    <img src="https://avatars.githubusercontent.com/u/24716901?v=4" width="100;" alt="treviasxk"/>
                    <br />
                    <sub><b>Trevias Xk</b></sub>
                </a>
            </td>
		</tr>
	<tbody>
</table>
<!-- readme: collaborators,contributors -end -->

## Dependencies 📦

- [Silk.NET](https://github.com/dotnet/Silk.NET) - Windowing, Input & OpenXR Bindings
- [Jitter Physics 2](https://github.com/notgiven688/jitterphysics2) - Physics Engine
- [miniaudio](https://github.com/mackron/miniaudio) - Audio
- [Prowl.Graphite](https://github.com/ProwlEngine/Anthology) - Vulkan Graphics & Renderer
- [Prowl.Motion](https://github.com/ProwlEngine/Anthology) - Animation
- [Prowl.Recast](https://github.com/ProwlEngine/Anthology) - Navigation
- [Prowl.Aperture](https://github.com/ProwlEngine/Anthology) - Image Decoding
- [Prowl.Echo](https://github.com/ProwlEngine/Anthology) - Serialization
- [Prowl.Paper](https://github.com/ProwlEngine/Anthology) - UI Framework
- [Prowl.Origami](https://github.com/ProwlEngine/Anthology) - Component Library for Paper
- [Prowl.Quill](https://github.com/ProwlEngine/Anthology) - Vector Graphics & Text Rendering
- [Prowl.Scribe](https://github.com/ProwlEngine/Anthology) - TrueType font parsing & glyph rasterization
- [Prowl.Scaffold](https://github.com/ProwlEngine/Anthology) - Layout Engine for Paper
- [Prowl.Quire](https://github.com/ProwlEngine/Anthology) - Markdown Parsing
- [Prowl.Ember](https://github.com/ProwlEngine/Anthology) - Script Hot Reload
- [Prowl.Rosetta](https://github.com/ProwlEngine/Anthology) - For Editor Localisation
- [Prowl.Vector](https://github.com/ProwlEngine/Anthology) - 64-bit Math Library
- [Prowl.Unwrapper](https://github.com/ProwlEngine/Anthology) - UV Unwrapper
- [Prowl.Photonic](https://github.com/ProwlEngine/Anthology) - Progressive Lightmapper
- [Prowl.Clay](https://github.com/ProwlEngine/Anthology) - Model Importing (GLTF, GLB, OBJ, FBX)

<p align="right">(<a href="#readme-top">back to top</a>)</p>

# <span align="center">📜 License 📜</span>

Distributed under the MIT License. See [LICENSE](https://github.com/ProwlEngine/Prowl/blob/main/LICENSE) for more information.

<p align="right">(<a href="#readme-top">back to top</a>)</p>

---

### [Join our Discord server! 🎉](https://discord.gg/BqnJ9Rn4sn)
[![Discord](https://img.shields.io/discord/1151582593519722668?logo=discord
)](https://discord.gg/BqnJ9Rn4sn)
[![ko-fi](https://ko-fi.com/img/githubbutton_sm.svg)](https://ko-fi.com/R7B4280JI3)


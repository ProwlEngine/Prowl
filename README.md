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
4. [Roadmap](#-roadmap-)
5. [Contributing](#-contributing-)
6. [Acknowledgments](#-acknowledgments-)
   * [Prerequisites](#contributors-)
   * [Dependencies](#dependencies-)
7. [License](#-license-)

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
    - C# Scripting
    - GameObject & Component structure
    - A Powerful Custom UI Library
        - Same Library for in-game and Editor UI
        - 3D Drawing in UI used for Gizmo's
        - Immediate Mode with retained properties
    - .NET 9
    - Editor with support for Editor Scripts and Custom Editors
    - Physics using [Jitter Physics 2](https://github.com/notgiven688/jitterphysics2)
        - Colliders: Box, Sphere, Capsule, Cylinder, Cone, Convex Mesh
        - Collision Layers
    - Unity-like Coroutines
    - Playtest directly in the Editor
    - ScriptableObjects
    - Projects & Project Settings
    - Powerful Serializer to create In-Memory Graphs
        - Graph → Custom Text Format
        - Graph → Binary
    - Fully 64-bit using Doubles
    - Large World Coordinates Support
        - Camera Relative Rendering
    - Scene System
    - Modular Audio Backend
        - OpenAL
        - Currently only supports .wav files
    - Prefabs
        - Supports Nested Prefabs
    - Build System - Build to Standalone Application
        - Packed Asset files
        - Tiny builds
        - Only exports used assets
        - Supports Windows, Mac & Linux
    - Navmesh and AI Agents (Recast & Detour)
    - Node Graph (Based on Unity's xNode)

-   **Graphics Rendering:**
    - Near Identical API to Unity
    - Modular Graphics Backend
        - OpenGL
        - OpenGL ES
        - Vulkan
        - Metal
        - DirectX 11
    - HDR, PBR (Physically Based Rendering)
        - Albedo Map
        - Normal Map
        - Roughness Map
        - Metallic Map
        - Ambient Occclusion Map
    - Forward Renderer
    - Batching & Frustum Culling
    - Motion Vectors
    - Multiple Shader Passes
    - Point, Spot, and Directional Lights
        - Spot & Directional Light Shadows - Point shadows is not implemented
        - Shadow Atlas
        - Dynamic Shadow Resolutions
    - Post Processing
        - Tonemapping (Melon, Aces, Reinhard, Uncharted, Filmic)
        - Motion Blur
        - Very fast Kawase Bloom
    - Transparency
    - Procedural Super Performant Skybox
    - Dynamic Resolutions Per Camera

-   **Asset Pipeline:**
    - A Powerful Asset Pipeline
    - Meta Files & Reference by GUID
    - Import Caching
    - Support for Custom Importers
    - Supports many major file formats via ImageMagick, Assimp, etc.
    - Sub-Assets, Assets stored inside other assets
    - Dependency Tracking

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

# <span align="center">🗺️ Roadmap 🗺️</span>

### Engine

- 🛠️ Cross Platform
    - ✔️ Windows
    - ✔️ MacOS
    - ✔️ Linux
    - ❌ Android
    - ❌ iOS
    - ❌ Web
- ✔️ UI Engine
- ❌ VR Support
- ✔️ Navmesh and AI Agents
- 🛠️ Networking Solution

### Rendering

- ❌ Realtime GI
- ❌ Lightmaps and Light Probes
- ❌ Cascaded shadow mapping
- ❌ [Particle System](https://github.com/ProwlEngine/Prowl/issues/37)
- ❌ [Terrain Engine](https://github.com/ProwlEngine/Prowl/issues/38)

### Editor

- ❌ Animation Tools
- ❌ Material Node Editor
- ❌ 2D Support

The complete list is in our [board](https://github.com/orgs/ProwlEngine/projects/1). Also, see the [open issues](https://github.com/michaelsakharov/prowl/issues) for a full list of proposed features and known issues.

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

## Dependencies 📦

### Runtime

- [Prowl.DotRecast](https://github.com/ProwlEngine/Prowl.DotRecast)
- [Prowl.Veldrid](https://github.com/ProwlEngine/Prowl.Veldrid)
- [Silk.NET](https://github.com/dotnet/Silk.NET)
- [Jitter Physics 2](https://github.com/notgiven688/jitterphysics2)

### Editor

- [Assimp](https://github.com/assimp/assimp) via [Assimp.NET](https://bitbucket.org/Starnick/assimpnet)
- [Image Sharp](https://github.com/SixLabors/ImageSharp)

<p align="right">(<a href="#readme-top">back to top</a>)</p>

# <span align="center">📜 License 📜</span>

Distributed under the MIT License. See [LICENSE](//LICENSE) for more information.

<p align="right">(<a href="#readme-top">back to top</a>)</p>

---

### [Join our Discord server! 🎉](https://discord.gg/BqnJ9Rn4sn)
[![Discord](https://img.shields.io/discord/1151582593519722668?logo=discord
)](https://discord.gg/BqnJ9Rn4sn)


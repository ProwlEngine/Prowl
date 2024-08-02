<img src="https://github.com/Kuvrot/Prowl/assets/23508114/5eef8da7-fb84-42f3-9d18-54b4f2d06551" width="100%">

![Github top languages](https://img.shields.io/github/languages/top/michaelsakharov/prowl)
[![GitHub version](https://img.shields.io/github/v/release/michaelsakharov/prowl?include_prereleases&style=flat-square)](https://github.com/michaelsakharov/prowl/releases) 
[![GitHub license](https://img.shields.io/github/license/michaelsakharov/prowl?style=flat-square)](https://github.com/michaelsakharov/prowl/blob/main/LICENSE.txt) 
[![GitHub issues](https://img.shields.io/github/issues/michaelsakharov/prowl?style=flat-square)](https://github.com/michaelsakharov/prowl/issues) 
[![GitHub stars](https://img.shields.io/github/stars/michaelsakharov/prowl?style=flat-square)](https://github.com/michaelsakharov/prowl/stargazers) 
[![Discord](https://img.shields.io/discord/1151582593519722668?style=flat-square)](https://discord.gg/BqnJ9Rn4sn)

# <p align="center">🎮 An Open Source Unity-like Engine! 🎮</p>

<a id="readme-top"></a>
  <ol>
    <li> <a href="#about-the-project">About The Project</a> </li>
    <li> <a href="#about-the-project">Features</a> </li>
    <li>
      <a href="#getting-started">Getting Started</a>
      <ul>
        <li><a href="#prerequisites">Prerequisites</a></li>
        <li><a href="#installation">Installation</a></li>
      </ul>
    </li>
    <li><a href="#roadmap">Roadmap</a></li>
    <li><a href="#contributing">Contributing</a></li>
    <li><a href="#acknowledgments">Acknowledgments</a></li>
    <li><a href="#license">License</a></li>
    <li><a href="#dependencies">Dependencies</a></li>
  </ol>

# <p align="center">📝 About The Project 📝</p>
Prowl is an open-source, **MIT-licensed** game engine developed in **pure C# in .NET 8**, (which surprisingly has **no runtime fees** believe it or not! 😮). It aims to provide a seamless transition for developers familiar with Unity by maintaining a similar API while also following KISS and staying as small and customizable as possible. 
The goal is a viable open-source Unity alternative, ideally, Unity projects can port over with as little resistance as possible.


### [<p align="center">Join our Discord server! 🎉</p>](https://discord.gg/BqnJ9Rn4sn) 

![Screenshot 2024-06-27 172952](https://github.com/michaelsakharov/Prowl/assets/8621606/80df58cc-53ac-4582-b722-1800d6cd4d13) | ![Screenshot 2024-06-27 172106](https://github.com/michaelsakharov/Prowl/assets/8621606/c13e9145-6b35-4ea5-ad66-523a275d0bc9)
:-:|:-:
![image](https://github.com/michaelsakharov/Prowl/assets/8621606/91ab57be-b215-40a8-871b-baf1dfc9ea58) | ![image](https://github.com/michaelsakharov/Prowl/assets/8621606/1cc6bb14-7c41-46e9-a581-c79ba51fc45f)
![image](https://github.com/michaelsakharov/Prowl/assets/8621606/b7fb26e0-568f-4bd7-9282-3e2fd12b38a9) | ![image](https://github.com/michaelsakharov/Prowl/assets/8621606/1b376ae7-8f13-41ea-ba1d-a49f777398ac)
![UntitledFLightModel](https://github.com/michaelsakharov/Prowl/assets/8621606/58a3c640-6ace-4f2f-8de6-e3bf5bbf9865) | ![Untitled](https://github.com/michaelsakharov/Prowl/assets/8621606/5165f2c4-681f-4cf7-8579-1152c971d142)

# <p align="center">✨ Features ✨</p>

-   **General:**
    - Cross-Platform! Windows, Linux & Mac!
    - Unity-like Editor & Scripting API
    - C# Scripting
    - Gameobject & Component structure
    - A Powerful Custom UI Library
      - Same Library for Ingame and Editor UI
      - 3D Drawing in UI used for Gizmo's
      - Immediate Mode with retained properties
    - .NET 8
    - Editor with support for Editor Scripts and Custom Editors
    - Physics ([Bepu Physics 2](https://github.com/bepu/bepuphysics2))
      - Colliders: Box, Sphere, Capsule, Cylinder, ~~Mesh Collider~~ - Needs to be re-implemented
      - Triggers
      - Raycasts and Sweeps
      - Non-Kinematic Character Controller (Just a fancy rigidbody)
        - Supports Moving Platforms
      - A ton of physical constraints (All of Bepu's constraints)
    - Unity-like Coroutines
    - Playtest directly in Editor
    - ScriptableObjects
    - Projects & Project Settings
    - Unity-like Serializer to create In-Memory Graphs
       - Graph -> Custom Text Format
       - Graph -> Binary
    - Fully 64-bit using Doubles
    - Large World Coordinates Support
       - Camera Relative Rendering
    - Scene System
    - Modular Audio Backend
       - OpenAL
       - Currently only supports .wav files
    - Prefabs
    - Build System - Build to Standalone Application
       - Packed Asset files
       - Less than 15mb builds - currently working on removing 10mb, Almost done!
       - Only exports used assets
       - Supports Windows, Mac & Linux
    - Navmesh and AI Agents (Recast & Detour)

-   **Graphics Rendering:**
    - Modular Graphics Backend
        - OpenGL
    -  PBR (Physically Based Rendering) using Cook-Torrance BRDF
        - Albedo Map
        - Normal Map
        - Roughness Map
        - Metallic Map
        - Ambient Occclusion Map
        - Emission Map
    - Deferred Renderer
    - Point, Spot, and Directional Lights
    - Shadow Mapping + Contact Hardening (Variable Penumbra)
    - Post Processing
        - HDR with Tonemapping (Melon, Aces, Reinhard, Uncharted, Filmic)
        - Bokeh Depth of Field
        - Screen Space Reflections
        - Kawase Multi-Pass Bloom
        - Temporal Anti-Aliasing
    - Stochastic Transparency
    - Adjustable Render Resolutions per camera
    - Dedicated Shadow Pass for Shaders
    - Procedural Skybox with Skybox-Blended Fog
    - GPU Skinned Mesh Rendering
    - Skeletal Animations

-   **Asset Pipeline:**
    - A Powerful Asset Pipeline with a very similar structure to unity
    - Meta Files & Reference by GUID
    - Import Caching
    - Support for Custom Importers
    - Supports many major file formats via ImageMagick, Assimp, etc
    - Sub-Assets, Assets stored inside other assets
    - Dependency Tracking
### [<p align="right">Join our Discord server! 🎉</p>](https://discord.gg/BqnJ9Rn4sn) 
<p align="right">(<a href="#readme-top">back to top</a>)</p>

# <p align="center">🚀 Getting Started 🚀</p>

Getting Prowl up and running is super easy!

**Note: There are no official releases yet so you need to download this repository to use Prowl!**

### Prerequisites

* [Visual Studio Version 17.8.0+](https://visualstudio.microsoft.com/vs/preview/) - Required to support .NET 8
* [.NET 8](https://dotnet.microsoft.com/en-us/download/dotnet/8.0)

### Installation

1. Clone the repo
2. Open `.sln` file with [Visual Studio Version 17.8.0+](https://visualstudio.microsoft.com/vs/preview/)
3. That's it! 😄 🎉
<p align="right">(<a href="#readme-top">back to top</a>)</p> 

# <p align="center">🗺️ Roadmap 🗺️</p>

### Engine
- 🛠️ Cross Platform
  - ✔️ Windows
  - ✔️ MacOS
  - ✔️ Linux
  - ❌ Andriod
  - ❌ iOS
  - ❌ Web
- ✔️ UI Engine
- ❌ VR Support
- ✔️ Navmesh and AI Agents
- 🛠️ Networking Solution

### Rendering
- ❌ SSAO, Screen-Space Decals, etc.
- ❌ Realtime GI
- ❌ Lightmaps and Light Probes
- ❌ Cascaded shadow mapping
- ❌ Particle System
- ❌ Terrain Engine

### Editor
- 🛠️ Package Manager (Packages partially implemented)
- ❌ Animation Tools
- 🛠️ Visual Scripting
- ❌ Material Node Editor
- ❌ 2D Support


See the [open issues](https://github.com/michaelsakharov/prowl/issues) for a full list of proposed features (and known issues).
<p align="right">(<a href="#readme-top">back to top</a>)</p>

# <p align="center">🤝 Contributing 🤝</p>

🚀 **Welcome to the Prowl community! We're thrilled that you're interested in contributing.**

We're not too proud to admit it – we need your help. 🆘

Developing a game engine is a colossal task, and we can't do it alone. We need passionate developers, designers, testers, and documentation enthusiasts, people like you to help make Prowl the best it can be. 💪

## How You Can Contribute

### Code Contributions 💻

Whether you're a seasoned developer or just getting started, your code contributions are invaluable. We have a list of [open issues](https://github.com/michaelsakharov/prowl/issues) that you can tackle, or feel free to propose your own improvements.

### Bug Reports 🐛

Encountered a bug? We want to know! Submit detailed bug reports on our [issue tracker](https://github.com/michaelsakharov/prowl/issues) to help us squash those pesky bugs.

### Feature Requests 💡

Have a fantastic idea for a new feature? Share it with us! Open a [feature request](https://github.com/michaelsakharov/prowl/issues) and let's discuss how we can make Prowl even better.

<!--Need a Documentation Site, Probably Hugo?-->
<!--
### Documentation 📚

Documentation is crucial, and we could use your help to make ours more comprehensive and user-friendly. Contribute to the [docs](linktodocshere) and help fellow developers get the most out of Prowl.
-->

### Spread the Word 📣

Not a developer? No problem! You can still contribute by spreading the word. Share your experiences with Prowl on social media, blogs, or forums. Let the world know about the exciting things happening here.

## Contributor Recognition 🏆

We're not just asking for contributors; we're asking for partners in this journey. Every small contribution is a step toward realizing Prowl.

All contributors will be acknowledged in our [Acknowledgments](#acknowledgments) section.

**Thank you for considering contributing to Prowl. Together, let's build something amazing!**
### [<p align="right">Join our Discord server! 🎉</p>](https://discord.gg/BqnJ9Rn4sn) 
<p align="right">(<a href="#readme-top">back to top</a>)</p>

# <p align="center">🙏 Acknowledgments 🙏</p>

- Hat tip to the creators of [Raylib](https://github.com/raysan5/raylib), While we are no longer based upon it, it has shaved off hours of development time getting the engine to a usable state.
- Some ideas/code have been taken from the amazing 2D Engine [Duality](https://github.com/AdamsLair/duality).
<p align="right">(<a href="#readme-top">back to top</a>)</p>

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
- [Trevias Xk (treviasxk)](https://github.com/treviasxk)
 
# License 📜

Distributed under the MIT License. See `LICENSE.txt` for more information.
<p align="right">(<a href="#readme-top">back to top</a>)</p>

# Dependencies 📦

### Runtime
- [Silk.NET](https://github.com/dotnet/Silk.NET)
- [Bepu Physics](https://github.com/bepu/bepuphysics2)

### Editor

- [Assimp](https://github.com/assimp/assimp) via [Assimp.NET](https://bitbucket.org/Starnick/assimpnet)
- [ImageMagick](http://www.imagemagick.org/) via [Magick.NET](https://github.com/dlemstra/Magick.NET)
<p align="right">(<a href="#readme-top">back to top</a>)</p>

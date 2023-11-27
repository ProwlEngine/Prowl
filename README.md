# Prowl, An Open Source Unity-like C# Game Engine!

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
    <li><a href="#screenshots">Screenshots</a></li>
  </ol>

# About The Project
Prowl is an open-source, **MIT-licensed** game engine developed in **pure C# in .NET 8**, (which surprisingly has **no runtime fees** believe it or not!). It aims to provide a seamless transition for developers familiar with Unity by maintaining a similar API while also following KISS and staying as small and customizable as possible. This engine is intended to be customized to your needs, everything is written to be a "Minimal" Production-Ready Implementation. Ideally, there will be plenty of Modules/Packages to expand on functionality for those who don't want (or are unable) to expand the base engine.

### [Join our Discord server!](https://discord.gg/BqnJ9Rn4sn)

**Note:** The Engine is very young and far from production-ready, being developed mainly by a single developer in his spare time. **And has only been tested and compiled for Windows!**

**Note:** Currently, the engine is using Raylib as a temporary base to speed up development, with plans to implement a custom-built API Agnostic backend later.

![Sponza screenshot](https://i.imgur.com/RrB7A0a.png)

# Features

-   **General:**
    - Unity-Like Editor & Scripting API
    - C# Scripting
    - Gameobject & Component structure
    - .NET 8
    - Dear ImGUI
    - Editor with support for Editor Scripts and Custom Editors
    - Less than 8k lines of Executable Code for both the Editor and Engine combined!
    - Unity-Like Coroutines
    - JSON Serialization via Newtonsoft.Json
    - Playtest directly in Editor
    - ScriptableObjects

-   **Graphics Rendering:**
    -  PBR (Physically Based Rendering) using Cook-Torrance BRDF
        - Albedo Map
        - Normal Map
        - Roughness Map
        - Metallic Map
        - Ambient Occclusion Map
        - Emission Map
    -  Deferred Renderer
    -  Point, Spot, and Directional Lights
    -  Shadow Mapping + Contact Hardening (Variable Penumbra)
    -  Post Processing
        - HDR with Aces Fitted Tonemapping
        - Bokeh Depth of Field

-   **Asset Pipeline:**
    - A Powerful Asset Pipeline with a very similar structure to unity
    - Meta Files & Reference by GUID
    - Import Caching
    - Support for Custom Importers
    - Supports many major file formats via ImageMagick, Assimp, etc
<p align="right">(<a href="#readme-top">back to top</a>)</p>

# Getting Started

Getting Prowl up and running is super easy!

### Prerequisites

* [Visual Studio Version 17.8.0+](https://visualstudio.microsoft.com/vs/preview/) - Required to support .NET 8
* [.NET 8](https://dotnet.microsoft.com/en-us/download/dotnet/8.0)

### Installation

1. Clone the repo
2. Open `.sln` file with [Visual Studio Version 17.8.0+](https://visualstudio.microsoft.com/vs/preview/)
3. That's it! :D
<p align="right">(<a href="#readme-top">back to top</a>)</p> 

# Roadmap

### Engine
- [ ] API Agnostic Backends to expand to support Vulkan, Metal, WebGPU, and more
- [ ] Cross Platform
  - [x] Windows - Fully functional
  - [ ] Linux - Compiles
  - [ ] MacOS - Compiles
  - [ ] Andriod - Unknown
  - [ ] iOS - Unknown
  - [ ] Web - Unknown
- [ ] VR Support
- [ ] BepuPhysics v2
- [ ] Navmesh and AI Agents
- [ ] Networking Solution
- [ ] 64-bit World Coordinates - [Issue for System.Numerics Double Support](https://github.com/dotnet/runtime/issues/24168)

### Rendering
- [ ] DOF, SSAO, SSR, TAA, Bloom, Screen-Space Decals, etc.
- [ ] Transparency
- [ ] Realtime GI
- [ ] Lightmaps and Light Probes
- [ ] Cascaded shadow mapping
- [ ] Procedural Skybox
   - [ ] Volumetric Clouds
- [ ] Skinned Mesh Rendering & Animations
- [ ] Particle System
- [ ] Terrain Engine

### Editor
- [ ] Package Manager (Packages partially implemented)
- [ ] Animation Tools
- [ ] Live Collaborative Tools?
- [ ] Visual Scripting
- [ ] Material Node Editor
- [ ] Basic 2D Support


See the [open issues](https://github.com/michaelsakharov/prowl/issues) for a full list of proposed features (and known issues).
<p align="right">(<a href="#readme-top">back to top</a>)</p>

# Contributing

🚀 **Welcome to the Prowl community! We're thrilled that you're interested in contributing.**

We're not too proud to admit it – we need your help. 🆘

Developing a game engine is a colossal task, and we can't do it alone. We need passionate developers, designers, testers, and documentation enthusiasts, people like you to help make Prowl the best it can be.

## How You Can Contribute

### Code Contributions

Whether you're a seasoned developer or just getting started, your code contributions are invaluable. We have a list of [open issues](https://github.com/michaelsakharov/prowl/issues) that you can tackle, or feel free to propose your own improvements.

### Bug Reports

Encountered a bug? We want to know! Submit detailed bug reports on our [issue tracker](https://github.com/michaelsakharov/prowl/issues) to help us squash those pesky bugs.

### Feature Requests

Have a fantastic idea for a new feature? Share it with us! Open a [feature request](https://github.com/michaelsakharov/prowl/issues) and let's discuss how we can make Prowl even better.

<!--Need a Documentation Site, Probably Hugo?-->
<!--
### Documentation

Documentation is crucial, and we could use your help to make ours more comprehensive and user-friendly. Contribute to the [docs](linktodocshere) and help fellow developers get the most out of Prowl.
-->

### Spread the Word

Not a developer? No problem! You can still contribute by spreading the word. Share your experiences with Prowl on social media, blogs, or forums. Let the world know about the exciting things happening here.

## Contributor Recognition

We're not just asking for contributors; we're asking for partners in this journey. Every small contribution is a step toward realizing Prowl.

All contributors will be acknowledged in our [Acknowledgments](#acknowledgments) section.

**Thank you for considering contributing to Prowl. Together, let's build something amazing!**
<p align="right">(<a href="#readme-top">back to top</a>)</p>

# Acknowledgments

- Hat tip to the creators of [Raylib](https://github.com/raysan5/raylib), It has shaved off hours of development time getting the engine to a usable state.
- Some ideas/code have been taken from the amazing 2D Engine [Duality](https://github.com/AdamsLair/duality).
- The great C++ [Arc Game Engine](https://github.com/MohitSethi99/ArcGameEngine), for some UI Inspiration
<p align="right">(<a href="#readme-top">back to top</a>)</p>
 
# License

Distributed under the MIT License. See `LICENSE.txt` for more information.
<p align="right">(<a href="#readme-top">back to top</a>)</p>

# Dependencies

### Runtime
- [Raylib](https://github.com/raysan5/raylib) via [Raylib-cs](https://github.com/ChrisDill/Raylib-cs) - Temporary
- [ImageMagick](http://www.imagemagick.org/) via [Magick.NET](https://github.com/dlemstra/Magick.NET)
- [ImGUI](https://github.com/ocornut/imgui) via [ImGUI.NET](https://github.com/ImGuiNET/ImGui.NET)
- [Newtonsoft](https://github.com/JamesNK/Newtonsoft.Json)

### Editor

- [Assimp](https://github.com/assimp/assimp) via [Assimp.NET](https://bitbucket.org/Starnick/assimpnet)
<p align="right">(<a href="#readme-top">back to top</a>)</p>

# Screenshots
![Bokeh Depth of Field](https://i.imgur.com/VYs44qq.png)
![Image of Editor before v1](https://github.com/michaelsakharov/Prowl/assets/8621606/a8830bb7-7980-4101-8076-1a0b75854b87)
![Flight Model](https://github.com/michaelsakharov/Prowl/assets/8621606/7683759c-5e0c-4689-acba-c733b3a64b5c)
![image](https://github.com/michaelsakharov/Prowl/assets/8621606/b95d24d5-f3a8-4652-9489-bfd6660ae497)




<p align="right">(<a href="#readme-top">back to top</a>)</p>

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

![Sponza screenshot](https://i.imgur.com/RrB7A0a.png)

# Features

-   **General:**
    - Unity-like Editor & Scripting API
    - C# Scripting
    - Gameobject & Component structure
    - .NET 8
    - Dear ImGUI Editor, including ImGuizmo, ImPlot, ImNodes
    - Editor with support for Editor Scripts and Custom Editors
    - Physics (BepuPhysics v2)
    - Less than 10k lines of Executable Code for both the Editor and Engine combined!
    - Unity-like Coroutines
    - Playtest directly in Editor
    - ScriptableObjects
    - Projects & Project Settings
    - Unity-like Serializer to create In-Memory Graphs
       - Graph -> Text (System.Text.Json)
       - Graph -> Binary
    - Full 64-bit using Doubles
    - Large World Coordinates Support
       - Camera Relative Rendering
    - Scene System
    - Prefabs
    - Build System - Build to Standalone Application
       - Packed Asset files
    - Node System (A Port of xNode from Unity)

-   **Graphics Rendering:**
    -  PBR (Physically Based Rendering) using Cook-Torrance BRDF
        - Albedo Map
        - Normal Map
        - Roughness Map
        - Metallic Map
        - Ambient Occclusion Map
        - Emission Map
    - Node-Based Customizable Render Pipelines
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
- [ ] Cross Platform
  - [x] Windows - Fully functional
  - [ ] Linux - Compiles & Opens but OpenGL Fails
  - [ ] MacOS - Compiles, M1 fails rest unknown
  - [ ] Andriod - Unknown
  - [ ] iOS - Unknown
  - [ ] Web - Unknown
- [ ] VR Support
  - [ ] Editor VR Support - The Entire editor should be able to function in Desktop VR
- [ ] Navmesh and AI Agents
- [ ] Networking Solution

### Rendering
- [ ] SSAO, Screen-Space Decals, etc.
- [ ] Realtime GI
- [ ] Lightmaps and Light Probes
- [ ] Cascaded shadow mapping
- [ ] Skinned Mesh Rendering & Animations - In Development Now
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

- Hat tip to the creators of [Raylib](https://github.com/raysan5/raylib), While we are no longer based upon it, it has shaved off hours of development time getting the engine to a usable state.
- Some ideas/code have been taken from the amazing 2D Engine [Duality](https://github.com/AdamsLair/duality).
<p align="right">(<a href="#readme-top">back to top</a>)</p>

## Contributors
- [Michael (AKA Wulferis)](https://twitter.com/Wulferis)
- [Josh Davis](https://github.com/10xJosh)
 
# License

Distributed under the MIT License. See `LICENSE.txt` for more information.
<p align="right">(<a href="#readme-top">back to top</a>)</p>

# Dependencies

### Runtime
- [Silk.NET](https://github.com/dotnet/Silk.NET)
- [ImageMagick](http://www.imagemagick.org/) via [Magick.NET](https://github.com/dlemstra/Magick.NET)
- [ImGUI](https://github.com/ocornut/imgui) via [ImGUI.NET](https://github.com/ImGuiNET/ImGui.NET)

### Editor

- [Assimp](https://github.com/assimp/assimp) via [Assimp.NET](https://bitbucket.org/Starnick/assimpnet)
<p align="right">(<a href="#readme-top">back to top</a>)</p>

# Screenshots
![Bokeh Depth of Field](https://i.imgur.com/VYs44qq.png)
![Editor](https://github.com/michaelsakharov/Prowl/assets/8621606/bb3c423b-3cc8-45a1-baa7-c9ad31d945c6)
![image](https://github.com/michaelsakharov/Prowl/assets/8621606/f755ee5d-eba5-4453-9f31-f61768c2554c)
![Flight Model](https://github.com/michaelsakharov/Prowl/assets/8621606/7683759c-5e0c-4689-acba-c733b3a64b5c)
![image](https://github.com/michaelsakharov/Prowl/assets/8621606/43d9f1db-1806-4961-bc40-fd77b196043b)
![Skybox-Aware Fog](https://github.com/michaelsakharov/Prowl/assets/8621606/0cf60c18-b6bc-4190-b458-a92aca29c0d9)
![image](https://github.com/michaelsakharov/Prowl/assets/8621606/6bee582a-903d-4b30-8c0b-50023ccc0c4e)
![image](https://github.com/michaelsakharov/Prowl/assets/8621606/5108bd1f-a822-47c0-855d-c3c4f4fb29b3)






<p align="right">(<a href="#readme-top">back to top</a>)</p>

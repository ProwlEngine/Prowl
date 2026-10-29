<img src="https://i.imgur.com/qoJ4RTS.png" width="100%" alt="Prowl logo image">

<p align="center">
  <a href="https://github.com/ProwlEngine/Prowl/releases"><img alt="Release" src="https://img.shields.io/github/v/release/ProwlEngine/Prowl?include_prereleases&style=flat-square"></a>
  <a href="https://github.com/ProwlEngine/Prowl/blob/main/LICENSE"><img alt="License" src="https://img.shields.io/github/license/ProwlEngine/Prowl?style=flat-square"></a>
  <a href="https://github.com/ProwlEngine/Prowl/issues"><img alt="Issues" src="https://img.shields.io/github/issues/ProwlEngine/Prowl?style=flat-square"></a>
  <a href="https://github.com/ProwlEngine/Prowl/stargazers"><img alt="Stars" src="https://img.shields.io/github/stars/ProwlEngine/Prowl?style=flat-square"></a>
  <a href="https://discord.gg/BqnJ9Rn4sn"><img alt="Discord" src="https://img.shields.io/discord/1151582593519722668?logo=discord&style=flat-square"></a>
</p>

<h1 align="center">A game engine you can read.</h1>

<p align="center">
  Prowl is a free, open-source 3D game engine written entirely in C#.<br>
  Build in a full editor, or straight from code.
</p>

<p align="center">
  <a href="https://github.com/ProwlEngine/Prowl/releases"><b>Download</b></a> ·
  <a href="#-getting-started">Getting started</a> ·
  <a href="#-features">Features</a> ·
  <a href="https://discord.gg/BqnJ9Rn4sn">Discord</a> ·
  <a href="https://ko-fi.com/R7B4280JI3">Support on Ko-fi</a>
</p>

<img width="100%" alt="The Prowl editor" src="https://i.imgur.com/hDXtuv5.png" />

| **MIT** | **C#** | **3** | **0** |
| :-: | :-: | :-: | :-: |
| Free and open source, no royalties | Engine and game in one language | Platforms: Windows, macOS and Linux | Accounts or sign-ups needed |

> [!WARNING]
> Prowl is in **1.0 Preview 5**, which brings a new Vulkan renderer, animation, navigation, VR and a
> rewritten asset system. Breaking changes are expected until the final 1.0 release. Projects from
> earlier previews are backed up and migrated automatically when opened, but migration isn't
> guaranteed between previews, so **always keep your own backup**.

## 🐾 Why Prowl

**C# all the way down.** Most engines put the engine in C++ and your scripts behind a binding layer.
In Prowl, engine and game are the same language.

- **Everything is plain C#.** Renderer, physics, audio and components are all C# you can read and
  change. Built-in components are regular scripts, so yours can do anything they can.
- **Your tools just work.** Projects are standard .NET `.csproj` files. Use Rider, VS Code or Visual
  Studio, with no plugin or extension.
- **Hot-reload, even in play mode.** Save a script and the change lands in the running game. No
  restart, no lost state.
- **Familiar if you know Unity.** GameObjects, components, prefabs and a similar scripting API, so
  Unity projects and habits port over with as little resistance as possible. Prowl still aims to stay
  small, simple and customizable.

| ![Screenshot](https://i.imgur.com/khCaEB5.png) | ![Screenshot](https://i.imgur.com/qNeMvC4.jpeg) |
| :-: | :-: |
| ![Screenshot](https://i.imgur.com/xooqeDx.png) | ![Screenshot](https://i.imgur.com/zbCJOwI.png) |
| ![Screenshot](https://i.imgur.com/TkJOmw9.png) | ![Screenshot](https://i.imgur.com/BXn31d0.png) |
| ![Screenshot](https://i.imgur.com/rLGLo7h.png) | ![Screenshot](https://i.imgur.com/jbKCif2.png) |

## 🧱 Use as much as you want

The editor sits on a runtime, and the runtime is built from standalone libraries. Build on any of the
three.

| Layer | What it is | Start here |
| --- | --- | --- |
| **Prowl.Editor** | Scenes, inspector, play mode and hot-reload, on the same runtime your game ships with. | [Download the editor](https://github.com/ProwlEngine/Prowl/releases) |
| **Prowl.Runtime** | Components, rendering, physics and audio as a plain .NET library. Reference it, write a class, run it. No editor in your project. | [Runtime samples](Samples/Runtime) |
| **[Prowl.Anthology](https://github.com/ProwlEngine/Anthology)** | Standalone, MIT-licensed game libraries for graphics, assets, math and UI. The runtime is built from them, and each one is its own NuGet package. | [Browse Anthology](https://github.com/ProwlEngine/Anthology) |

## 🚀 Getting started

### Requirements

- [.NET 10 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/10.0). Prowl compiles your
  scripts with it, so you need it even if you only use a release build.
- A GPU with Vulkan 1.2 support. macOS runs through MoltenVK.

### Download the editor

Grab the latest build for Windows, macOS or Linux from the
[Releases page](https://github.com/ProwlEngine/Prowl/releases), unzip it and run the editor. New to
Prowl? Watch [Getting Started with Prowl](https://www.youtube.com/watch?v=GY8VC1gQJ7g).

### Build from source

```sh
git clone https://github.com/ProwlEngine/Prowl.git
cd Prowl
dotnet run --project Prowl.Editor
```

Or open `Prowl.slnx` in [Visual Studio](https://visualstudio.microsoft.com/),
[Rider](https://www.jetbrains.com/rider/) or [VS Code](https://code.visualstudio.com/) and run
`Prowl.Editor`. In VS Code, open `Prowl.Editor/Program.cs` and pick **Run → Start Debugging**.

### Or skip the editor

The runtime works fully standalone. Reference `Prowl.Runtime` from your own .NET project and ship a
game with zero editor dependency:

```csharp
using Prowl.Runtime;
using Prowl.Runtime.Resources;
using Prowl.Vector;

new MyGame().Run("Simple Cube", 1280, 720);

public sealed class MyGame : Game
{
    public override void Initialize()
    {
        var scene = new Scene();

        GameObject sun = new("Sun");
        sun.AddComponent<Light>().Type = LightType.Directional;
        sun.Transform.LocalEulerAngles = new Float3(50, 30, 0);
        scene.Add(sun);

        GameObject cam = new("Main Camera") { Tag = "Main Camera" };
        cam.Transform.Position = new Float3(0, 2, -8);
        cam.AddComponent<Camera>();
        scene.Add(cam);

        GameObject cube = new("Cube");
        MeshRenderer mr = cube.AddComponent<MeshRenderer>();
        mr.Mesh = Mesh.CreateCube(Float3.One);
        mr.Material = new Material(Shader.LoadDefault(DefaultShader.Lit));
        scene.Add(cube);

        Scene.Load(scene);
    }
}
```

[`Samples/Runtime`](Samples/Runtime) has complete, runnable examples, starting with
[`HelloProwl`](Samples/Runtime/HelloProwl/Program.cs):

| Sample | Shows |
| --- | --- |
| `HelloProwl` | The smallest complete game: a scene, a custom component and physics cubes |
| `RenderingShowcase` | Materials, decals, transparency, lights, area lights and shadows |
| `ManyLights` | A night plaza lit by thousands of static point lights |
| `ParticleShowcase` | Particle systems, sub-emitters and trails |
| `PhysicsShowcase` | Every collider, stacking, joints, motors, queries and triggers |
| `ControllerShowcase` | The character controller on stairs, slopes, ceilings and tight spaces |
| `VehicleShowcase` | Drivable vehicles on a circuit and a hill climb |
| `ScriptingShowcase` | Lifecycle, hierarchy, `GameTask` async, input actions, time, layers and tags |
| `VRBasics`, `VRShowcase` | A headset, hands and grabbing; then walking, physics and weapons |

## ✨ Features

Prowl ships with the systems a 3D game needs, so you start on your game instead of the plumbing.
Click a category to expand it.

<details>
<summary><b>General</b></summary>

- Cross-platform: Windows, Linux and macOS, for both the editor and exported builds (Android in development)
- Unity-like editor and scripting API
- C# scripting with .NET 10, compiled in process with Roslyn
- GameObject and component architecture
- **Prowl.Runtime works fully standalone** from the editor
- Custom immediate-mode UI ([Paper](https://github.com/ProwlEngine/Anthology)), with the editor built on [Origami](https://github.com/ProwlEngine/Anthology)
- Vector graphics and text rendering via [Quill](https://github.com/ProwlEngine/Anthology)
- `GameTask` async API (`MainThread`, `WorkerThread`, `NextFrame`, `Delay`, `WaitUntil`) with per-session cancellation
- Roslyn analyzers that catch common scripting mistakes (component constructors, blocking waits, `?.` on engine objects)
- Serialization via [Prowl.Echo](https://github.com/ProwlEngine/Anthology)
- Tags and layers
- Scenes with `Scene.Environment` (sky, ambient, fog), async loading and `DontDestroyOnLoad`
- Prefabs with apply, revert, break instance and override tracking
- Input action system with composites and processors
  - `.inputactions` assets with a dedicated editor
  - Action phases (Disabled / Started / Performed / Cancelled)
  - Composite bindings (WASD to Float2, D-pad, etc.) for keyboard, mouse, gamepad and XR controllers
- GameObject-based UI, including world-space UI
  - `RectTransform`-driven layout, buttons, toggles, sliders, dropdowns, layout groups and drag-and-drop event handlers
- Math via [Prowl.Vector](https://github.com/ProwlEngine/Anthology)
  - SIMD-backed vectors, matrices (`Float4x4`), quaternions, Transform2D
  - Shapes: AABB, Bounds, Frustum, Cone, Ray, Plane, LineSegment, Rect
  - Mesh editing operators and quadric mesh simplification
- Frame Stats profiler with custom scopes

</details>

<details>
<summary><b>Editor</b></summary>

- Scene View, Hierarchy, Inspector, Project Browser, Console, Game View and Frame Stats
- Custom component editors, property editors and scene view editors
- Transform gizmos (move, rotate, scale), shift-drag to duplicate
- Undo/redo
- Dockable, resizable panels with layout persistence and panel maximize
- Drag and drop for assets, GameObjects and components
- Multi-select and search/filtering in editor panels
- Asset thumbnails and 3D previews
- Animation graph, avatar, animation curve and gradient editors, plus a ragdoll generator
- Volume profile, input action, sprite and terrain editors
- Rebindable shortcuts
- 12 built-in themes with customizable color palettes, sizing and wallpapers
- Playtest directly in the editor
- Hot-reloading scripts
- Localized into English, German, Spanish, French, Italian, Japanese, Korean, Polish, Portuguese, Russian, Turkish and Chinese
- Managed and native plugins with assembly definitions
- **Prowl CLI**: drive a running editor from scripts or AI agents; projects ship with agent skills

</details>

<details>
<summary><b>Rendering</b></summary>

- Vulkan via [Prowl.Graphite](https://github.com/ProwlEngine/Anthology) (MoltenVK on macOS)
- GPU-driven renderer: render graph, dedicated render thread, bindless textures, vertex pulling, multi-draw indirect
- Camera-relative rendering
- H-Forward lighting and cached static shadows
- Render Features API and pluggable camera pipelines for custom passes
- Prism, a fully managed GLSL to SPIR-V compiler, with async variant compiles, a SPIR-V cache and shaders precompiled into builds
- Lit and unlit shaders, surface shaders, material variants, shader keywords/variants
- HDR and metallic-workflow PBR
  - Albedo, normal, surface (AO / roughness / metallic), emission, parallax, translucency and anisotropy
- Compute shaders and compute buffers
- Mesh renderer and skinned mesh renderer with GPU blendshapes
- Line renderer
- Sprites, with sprite sheet slicing (grid, isometric and automatic alpha-based) and a dedicated sprite editor
- Render textures, Texture3D and cubemaps
- Lights: directional, point, spot and area (rect, disk, tube)
  - Sphere lights, sun disk, light cookies and IES profiles
  - Shadow atlas with static caching and screen-size-driven resolution
  - Cascaded shadow maps for directional lights (up to 4 cascades)
  - Point light shadows
  - Hard, tent and contact-hardening shadow filters
- Decals (albedo, normal and mask)
- Reflection probes (baked and realtime) with box projection and a sky probe
- UV unwrapping via [Prowl.Unwrapper](https://github.com/ProwlEngine/Anthology) and a progressive lightmapper via [Prowl.Photonic](https://github.com/ProwlEngine/Anthology)
- Baked light probes
- Skies: procedural atmosphere, gradient, cubemap, panorama and six-sided
- Volumetric clouds and volumetric fog
- Camera stacking, camera cuts and debug view modes
- Tonemapping: ACES, AgX, Reinhard, RomBinDaHouse, Uncharted 2
- Anti-aliasing: FXAA, SMAA and TAA
- Volume-based post-processing with global and local volumes
  - Auto exposure, bloom, GTAO, screen-space reflections, motion blur, depth of field, god rays
  - Vignette, chromatic aberration, film grain, color grading, LUTs, sharpen, edge detection, pixelation
  - Custom post effects
- Transparency and refraction
- Terrain with quadtree LOD, heightmap and splatmap painting, grass, details, trees and holes
- Particle systems
  - Shape, emission, sub-emitters, texture sheet animation, trails, collision, forces, over-lifetime and by-speed modules
  - Local and world simulation spaces

</details>

<details>
<summary><b>Animation</b></summary>

- `Animator` with animation graphs via [Prowl.Motion](https://github.com/ProwlEngine/Anthology), about 90 nodes
- State machines, blend spaces, layers and masks, inertial blending
- Humanoid avatars with retargeting between rigs
- IK (two-bone, look at, aim), foot grounding, root motion and warping
- Active ragdolls with muscles and pinning

</details>

<details>
<summary><b>Physics</b></summary>

- [Jitter Physics 2](https://github.com/notgiven688/jitterphysics2)
- Colliders: box, sphere, capsule, cylinder, cone, mesh (convex or concave) and terrain
- Wheel collider with friction curves, weight transfer, anti-roll bars and per-surface grip
- Joints and constraints: ball socket, hinge joint, hinge angle, fixed angle, cone limit, distance limit, twist angle, prismatic, universal, point on line, point on plane, angular motor, linear motor and drive
- Character controller with moving platforms, custom gravity, steps and slopes
- Trigger volumes and collision callbacks
- Collision layers and filtering (`LayerMask`)
- Raycasting and shape query API

</details>

<details>
<summary><b>Navigation</b></summary>

- NavMesh via [Prowl.Recast](https://github.com/ProwlEngine/Anthology)
- NavMesh surfaces, agents, obstacles (carving), links and modifiers
- Area costs, crowd steering, background baking and partial rebuilds

</details>

<details>
<summary><b>Audio</b></summary>

- [miniaudio](https://github.com/mackron/miniaudio)
- Spatial 3D audio with attenuation and Doppler
- Audio mixers and groups
- Effect chain (delay, distortion, biquad filter, reverb, phaser) plus custom `IAudioEffect`

</details>

<details>
<summary><b>VR</b></summary>

- Desktop VR through OpenXR (SteamVR, Meta and other runtimes)
- Stereo rendering with multiview
- Controllers, haptics, hand tracking and eye gaze
- World-space UI pointers

</details>

<details>
<summary><b>Asset pipeline</b></summary>

- GUID-based asset references with meta files
- One object per asset, refilled in place on reimport
- Import caching and file watching for auto-reimport
- Custom importers via attributes
- Sub-assets with deterministic GUIDs
- Forward and reverse dependency tracking
- Async loading and residency management with a memory budget
- Runtime asset mounting (folders, zips, HTTP) and runtime importers for mods and user content
- Supported formats:
  - Models: glTF, GLB, OBJ, FBX (via [Prowl.Clay](https://github.com/ProwlEngine/Anthology))
  - Textures: PNG, JPG, BMP, GIF, TGA, TIFF, WEBP, PSD, DDS, EXR, HDR and more (via [Prowl.Aperture](https://github.com/ProwlEngine/Anthology))
  - Audio: WAV, MP3, OGG, FLAC
  - Fonts: TTF, OTF

</details>

<details>
<summary><b>Builds</b></summary>

- Build to a standalone application for Windows, macOS and Linux (Android in development)
- Packed asset files (`.prowlpak`) containing only the assets you use
- Used shader variants precompiled into the build
- Per-platform build profiles
- Native AOT player builds (experimental)

</details>

## 🗂️ Repository layout

| Path | What's there |
| --- | --- |
| `Prowl.Runtime/` | The engine: components, rendering, physics, audio, animation, navigation, XR, assets |
| `Prowl.Editor/` | The editor, built on the runtime |
| `Players/Desktop/` | The desktop player that exported games run in |
| `Prowl.Cli/` | Command-line client for driving a running editor |
| `Prowl.Analyzers/` | Roslyn analyzers that catch common scripting mistakes |
| `Prowl.Runtime.Test/`, `Prowl.Editor.Test/` | Tests |
| `Samples/Runtime/` | Code-only sample games |
| `Libraries/` | Prebuilt native libraries (miniaudio, OpenXR loader) per platform |

Prowl pulls its [Anthology](https://github.com/ProwlEngine/Anthology) libraries from NuGet. To work on
them alongside the engine, clone Anthology next to this repo (`../Anthology`) and turn on the local
switch in any one of these ways:

- pass `-p:ProwlLocalAnthology=true` to `dotnet build`
- create a gitignored `prowl.local.props` that sets `<ProwlLocalAnthology>true</ProwlLocalAnthology>`
- set the `PROWL_LOCAL_ANTHOLOGY=1` environment variable

## 🤝 Contributing

Bug reports, ideas and pull requests are all welcome. Read the
[contributing guide](CONTRIBUTING.md) to get started, and come say hi on
[Discord](https://discord.gg/BqnJ9Rn4sn).

Other ways to help: star the repo so more people find Prowl,
[open an issue](https://github.com/ProwlEngine/Prowl/issues) when something breaks, or
[support development on Ko-fi](https://ko-fi.com/R7B4280JI3).

## 🙏 Acknowledgments

Hat tip to the creators of [Raylib](https://github.com/raysan5/raylib). Prowl is no longer built on
it, but it saved hours of development time getting the engine to a usable state.

### Contributors

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
                <a href="https://github.com/Tryibion">
                    <img src="https://avatars.githubusercontent.com/u/71274967?v=4" width="100;" alt="Tryibion"/>
                    <br />
                    <sub><b>Chandler Cox</b></sub>
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
                <a href="https://github.com/ZeppelinGames">
                    <img src="https://avatars.githubusercontent.com/u/31507417?v=4" width="100;" alt="ZeppelinGames"/>
                    <br />
                    <sub><b>Mark Saba</b></sub>
                </a>
            </td>
		</tr>
		<tr>
            <td align="center">
                <a href="https://github.com/10xJosh">
                    <img src="https://avatars.githubusercontent.com/u/55113224?v=4" width="100;" alt="10xJosh"/>
                    <br />
                    <sub><b>Josh Davis</b></sub>
                </a>
            </td>
            <td align="center">
                <a href="https://github.com/ZedDevStuff">
                    <img src="https://avatars.githubusercontent.com/u/98429642?v=4" width="100;" alt="ZedDevStuff"/>
                    <br />
                    <sub><b>Kouame Benoit Junior Augustin</b></sub>
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
                <a href="https://github.com/keithwill">
                    <img src="https://avatars.githubusercontent.com/u/33398353?v=4" width="100;" alt="keithwill"/>
                    <br />
                    <sub><b>Keith Williams</b></sub>
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

### Dependencies

- [Silk.NET](https://github.com/dotnet/Silk.NET): windowing, input and OpenXR bindings
- [Jitter Physics 2](https://github.com/notgiven688/jitterphysics2): physics
- [miniaudio](https://github.com/mackron/miniaudio): audio

And from [Prowl.Anthology](https://github.com/ProwlEngine/Anthology):

| Library | Used for |
| --- | --- |
| Prowl.Graphite | Vulkan graphics and renderer |
| Prowl.Motion | Animation |
| Prowl.Recast | Navigation |
| Prowl.Clay | Model importing (glTF, GLB, OBJ, FBX) |
| Prowl.Aperture | Image decoding |
| Prowl.Echo | Serialization |
| Prowl.Paper | UI framework |
| Prowl.Origami | Component library for Paper |
| Prowl.Quill | Vector graphics and text rendering |
| Prowl.Scribe | TrueType parsing and glyph rasterization |
| Prowl.Scaffold | Layout engine for Paper |
| Prowl.Quire | Markdown parsing |
| Prowl.Ember | Script hot-reload |
| Prowl.Rosetta | Editor localization |
| Prowl.Vector | Math |
| Prowl.Unwrapper | UV unwrapping |
| Prowl.Photonic | Progressive lightmapper |

## 📜 License

Prowl is distributed under the [MIT License](LICENSE). Free to use, modify and ship, with no royalties.

<p align="center">
  <a href="https://discord.gg/BqnJ9Rn4sn"><b>Join the Discord</b></a> ·
  <a href="https://ko-fi.com/R7B4280JI3"><b>Support on Ko-fi</b></a>
</p>

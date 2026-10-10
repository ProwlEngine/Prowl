# Anthology

The collection of modular libraries that power the [Prowl game engine](https://github.com/ProwlEngine/Prowl).

Each library ships as its own `Prowl.*` NuGet package and can be used independently, but
they live and version together in this one repository so that a change spanning several of
them lands in a single commit, no cross-repo version juggling, no publish chain.

## Libraries

| Folder       | Package           | Purpose                          |
| ------------ | ----------------- | -------------------------------- |
| `Vector`     | Prowl.Vector      | Math and geometry primitives     |
| `Scribe`     | Prowl.Scribe      | TrueType fonts, layout, Markdown |
| `Quill`      | Prowl.Quill       | GPU 2D vector graphics           |
| `Paper`      | Prowl.Paper       | Immediate-mode UI framework      |
| `Scaffold`   | Prowl.Scaffold    | Incremental UI layout engine     |
| `Quire`      | Prowl.Quire       | Markdown parser                  |
| `Origami`    | Prowl.Origami     | UI Widgets/Components for paper  |
| `Echo`       | Prowl.Echo        | Serialization                    |
| `Clay`       | Prowl.Clay        | 3D Model Loading (GLTF/FBX/Obj)  |
| `Crumb`      | Prowl.Crumb       | A simple lightweight Tokenizer   |
| `Photonic`   | Prowl.Photonic    | Progressive CPU Lightmapper      |
| `Unwrapper`  | Prowl.Unwrapper   | Mesh Unwrapping for UV's 	      |
| `Rosetta`    | Prowl.Rosetta     | Localization utilities           |
| `Slang`      | Prowl.Slang       | Bindings for the Sland Compiler  |
| `Graphite`   | Prowl.Graphite    | Low-level GPU graphics (Vulkan / D3D11); also `.Compiler`, `.ShaderDef`, `.Variants` |
| `Recast`     | Prowl.Recast      | DotRecast fork for Navmesh generation and pathfinding |
| `Aperture`   | Prowl.Aperture    | Image loading (BMP/DDS/EXR/GIF/HDR/ICO/JPEG/PNG/PNM/PSD/RAW/TGA/TIFF/WebP) |
| `Motion`     | Prowl.Motion      | Skeletal animation, graphs, IK and humanoid retargeting |

## Versioning

The whole family ships under one version, set once in [`Directory.Build.props`](Directory.Build.props).
Bump that single `<Version>` and tag the commit `vX.Y.Z`; CI builds, packs and pushes every
package at that version.

## Repository layout

Every library is a self-contained folder. Shared build and packaging rules live in the
root `Directory.Build.props`, so individual project files stay small.

History for each library is preserved under its folder, so `git log -- Scribe/` shows the
full past of that library.

## Contributors

<!-- readme: collaborators,contributors -start -->
<table>
	<tbody>
		<tr>
            <td align="center">
                <a href="https://github.com/sinnwrig">
                    <img src="https://avatars.githubusercontent.com/u/116330012?v=4" width="100;" alt="sinnwrig"/>
                    <br />
                    <sub><b>Kai Angulo</b></sub>
                </a>
            </td>
            <td align="center">
                <a href="https://github.com/michaelsakharov">
                    <img src="https://avatars.githubusercontent.com/u/8621606?v=4" width="100;" alt="michaelsakharov"/>
                    <br />
                    <sub><b>Wulferis</b></sub>
                </a>
            </td>
            <td align="center">
                <a href="https://github.com/jhm-ciberman">
                    <img src="https://avatars.githubusercontent.com/u/7988351?v=4" width="100;" alt="jhm-ciberman"/>
                    <br />
                    <sub><b>Javier "Ciberman" Mora</b></sub>
                </a>
            </td>
            <td align="center">
                <a href="https://github.com/mic-code">
                    <img src="https://avatars.githubusercontent.com/u/26720201?v=4" width="100;" alt="mic-code"/>
                    <br />
                    <sub><b>Michael Yuen</b></sub>
                </a>
            </td>
            <td align="center">
                <a href="https://github.com/Exanite">
                    <img src="https://avatars.githubusercontent.com/u/42710136?v=4" width="100;" alt="Exanite"/>
                    <br />
                    <sub><b>William Chen</b></sub>
                </a>
            </td>
            <td align="center">
                <a href="https://github.com/PaperPrototype">
                    <img src="https://avatars.githubusercontent.com/u/48071553?v=4" width="100;" alt="PaperPrototype"/>
                    <br />
                    <sub><b>Abdiel Lopez</b></sub>
                </a>
            </td>
		</tr>
		<tr>
            <td align="center">
                <a href="https://github.com/Acissathar">
                    <img src="https://avatars.githubusercontent.com/u/10227954?v=4" width="100;" alt="Acissathar"/>
                    <br />
                    <sub><b>Will</b></sub>
                </a>
            </td>
            <td align="center">
                <a href="https://github.com/JothamR">
                    <img src="https://avatars.githubusercontent.com/u/5410231?v=4" width="100;" alt="JothamR"/>
                    <br />
                    <sub><b>Jotham</b></sub>
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
                <a href="https://github.com/MiniMe453">
                    <img src="https://avatars.githubusercontent.com/u/25212675?v=4" width="100;" alt="MiniMe453"/>
                    <br />
                    <sub><b>MiniMe453</b></sub>
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
                <a href="https://github.com/EJTP">
                    <img src="https://avatars.githubusercontent.com/u/87308197?v=4" width="100;" alt="EJTP"/>
                    <br />
                    <sub><b>EJTP</b></sub>
                </a>
            </td>
		</tr>
		<tr>
            <td align="center">
                <a href="https://github.com/paulbartrum">
                    <img src="https://avatars.githubusercontent.com/u/1572571?v=4" width="100;" alt="paulbartrum"/>
                    <br />
                    <sub><b>paulbartrum</b></sub>
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
                <a href="https://github.com/ctzcs">
                    <img src="https://avatars.githubusercontent.com/u/47210521?v=4" width="100;" alt="ctzcs"/>
                    <br />
                    <sub><b>tianwen</b></sub>
                </a>
            </td>
            <td align="center">
                <a href="https://github.com/Fauxgott">
                    <img src="https://avatars.githubusercontent.com/u/258977954?v=4" width="100;" alt="Fauxgott"/>
                    <br />
                    <sub><b>Duel</b></sub>
                </a>
            </td>
            <td align="center">
                <a href="https://github.com/mberthaud">
                    <img src="https://avatars.githubusercontent.com/u/26536123?v=4" width="100;" alt="mberthaud"/>
                    <br />
                    <sub><b>Michael Berthaud</b></sub>
                </a>
            </td>
		</tr>
	<tbody>
</table>
<!-- readme: collaborators,contributors -end -->


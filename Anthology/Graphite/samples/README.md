# Samples

Sample applications demonstrating Veldrid's graphics API. On Windows, samples default to D3D11. On other platforms, they default to Vulkan.

## Running

```bash
# From the repo root:
dotnet run --project samples/<SampleName>/Desktop/<SampleName>.Desktop.csproj

# Standalone samples (no Desktop subfolder):
dotnet run --project samples/GettingStarted/GettingStarted.csproj
dotnet run --project samples/NeoDemo/NeoDemo.csproj
```

## Samples (simplest to most complex)

| Sample | Description | Run command |
|--------|-------------|-------------|
| **GettingStarted** | Colored quad. Minimal Veldrid usage. | `dotnet run --project samples/GettingStarted/GettingStarted.csproj` |
| **ImageTint** | Compute shader that tints an image (headless). | `dotnet run --project samples/ImageTint/ImageTint.csproj -- <input.png> <output.png>` |
| **TexturedCube** | Textured 3D cube with depth buffer. | `dotnet run --project samples/TexturedCube/Desktop/TexturedCube.Desktop.csproj` |
| **Offscreen** | Render-to-texture using framebuffers. | `dotnet run --project samples/Offscreen/Desktop/Offscreen.Desktop.csproj` |
| **Instancing** | Instanced drawing with texture arrays. | `dotnet run --project samples/Instancing/Desktop/Instancing.Desktop.csproj` |
| **ComputeTexture** | Compute shader writing to a texture. | `dotnet run --project samples/ComputeTexture/Desktop/ComputeTexture.Desktop.csproj` |
| **ComputeParticles** | Compute shader particle simulation. | `dotnet run --project samples/ComputeParticles/Desktop/ComputeParticles.Desktop.csproj` |
| **AnimatedMesh** | Skeletal animation loaded via Assimp. | `dotnet run --project samples/AnimatedMesh/Desktop/AnimatedMesh.Desktop.csproj` |
| **NeoDemo** | Full scene: Sponza atrium, shadow maps, reflections, ImGui overlay. | `dotnet run --project samples/NeoDemo/NeoDemo.csproj` |

## Support Libraries

| Project | Description |
|---------|-------------|
| **SampleBase** | Shared window setup and render loop used by most samples. |
| **AssetPrimitives** | Serialization types for processed mesh/texture assets. |
| **AssetProcessor** | Converts raw assets (models, textures) into binary format for samples. |
| **Common** | Shared shader cross-compilation utilities. |

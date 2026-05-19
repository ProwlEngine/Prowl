# Prowl.Clay

A 3D model importing library for the Prowl Game Engine. Loads glTF 2.0 / GLB / VRM, Wavefront OBJ, and Autodesk FBX (binary + ASCII) into an Assimp-flavoured read-only `Model` graph with a configurable post-process pipeline.

Pure C# .NET 10, no native dependencies, AOT-friendly. Math via [Prowl.Vector](https://github.com/ProwlEngine/Prowl.Vector).

## Features

- **Format Support**
  - glTF 2.0 (`.gltf` + external resources, `.glb` chunked binary, `.vrm`)
  - Wavefront OBJ (`.obj` + `.mtl`)
  - Autodesk FBX binary and ASCII (versions 7100-7700)

- **glTF Material Extensions**
  - `KHR_materials_unlit`, `KHR_materials_clearcoat`, `KHR_materials_sheen`,
    `KHR_materials_transmission`, `KHR_materials_volume`, `KHR_materials_ior`,
    `KHR_materials_specular`, `KHR_materials_emissive_strength`,
    `KHR_materials_pbrSpecularGlossiness`
  - `KHR_texture_transform` per slot
  - Unknown extensions pass through to `Material.RawExtensions`

- **FBX Coverage**
  - Binary + ASCII readers, multi-UV channels, per-polygon material partitioning,
    UV transforms, geometric transform baking, axis conversion (RH-Y-up, RH-Z-up),
    `Pose::BindPose` preference for stale `cluster.Transform`, skinning with
    8+ influences per vertex, blend shapes, animation curves

- **Post-Process Pipeline** (toggle per-step via `PostProcessFlags`)
  - Triangulate, JoinIdenticalVertices, GenerateNormals / GenerateSmoothNormals
  - CalcTangentSpace (MikkTSpace), LimitBoneWeights, RemoveDegenerates
  - FlipUVs, FlipWindingOrder, ConvertCoordinateSystem, GlobalScale
  - GenerateBounds, EmbedTextures, PopulateSkeletons
  - OptimizeMeshes, OptimizeGraph, ImproveCacheLocality (Tipsify)
  - SplitByBoneCount, SplitLargeMeshes, Debone, SortByPrimitiveType
  - ValidateDataStructure

- **Misc**
  - Bundled presets: `Raw`, `GameFast`, `GameQuality`, `EditorMaxQuality`
  - Pluggable `IFileResolver` for custom asset stores
  - Detailed `ImportLog` per model for warnings and info messages

## Usage

### Basic Loading

```csharp
using Prowl.Clay;
using Prowl.Clay.Importer;

var model = ModelImporter.Load("character.glb");

Console.WriteLine($"{model.Nodes.Count} nodes, {model.Meshes.Count} meshes");
foreach (var mesh in model.Meshes)
    Console.WriteLine($"  {mesh.Name}: {mesh.VertexCount} verts, {mesh.SubMeshes.Length} submeshes");
```

### Configuring Post-Processing

```csharp
var settings = ModelImporterSettings.GameQuality with
{
    BoneWeightLimit       = 4,
    SmoothNormalsAngleDeg = 60f,
    GlobalScale           = 1f,
    OnLog                 = entry => Console.WriteLine(entry),
};

var model = ModelImporter.Load("scene.gltf", settings);
```

### Loading From a Stream

```csharp
using var stream = File.OpenRead("character.glb");
var model = ModelImporter.Load(stream, format: "glb", ModelImporterSettings.GameFast);
```

### Walking a Model

```csharp
foreach (var node in model.Nodes)
{
    if (node.MeshIndex >= 0)
    {
        var mesh = model.Meshes[node.MeshIndex];
        // ... upload to GPU, etc.
    }
}

foreach (var clip in model.AnimationClips)
{
    foreach (var b in clip.Bindings)
    {
        // b.NodeIndex, b.Property (Position / Rotation / Scale / BlendShapeWeight), b.Curve
    }
}

foreach (var entry in model.Log.Entries)
    Console.WriteLine(entry); // warnings / info from the importer
```

## Conventions

- **Coordinate system**: target is left-handed, Y-up, +Z forward (DirectX convention). Source RH-Y-up (glTF, OBJ, default FBX) and RH-Z-up (3ds Max FBX) are auto-converted by the `ConvertCoordinateSystem` step.
- **UV origin**: top-left (V=0 at the top). The `FlipUVs` step toggles this when needed.
- **Textures**: external textures are returned as resolved file paths; embedded textures (data URIs, GLB chunks, FBX `Video::Content` blobs) are returned as their original encoded bytes (PNG/JPG/KTX2). Decoding and GPU upload is the consumer's job.
- **Errors**: all import failures throw `Prowl.Clay.ImportException`. Recoverable issues land in `Model.Log`.

## Layout

```
Clay/                main library
Tests/               xUnit test suite
DiffDebugger/        CLI sample, structural diff of two models
test-models/         glTF + FBX + OBJ fixtures used by the test suite
```

## License

This component is part of the Prowl Game Engine and is licensed under the MIT License. See the LICENSE file in the project root for details.

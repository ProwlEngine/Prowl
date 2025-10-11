// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Linq;
using Prowl.Runtime;
using Prowl.Vector;
using Prowl.Vector.Geometry;
using Prowl.Runtime.Rendering;
using Prowl.Runtime.Resources;
using Silk.NET.Input;

namespace VoxelEngine;

internal class Program
{
    static void Main(string[] args)
    {
        new VoxelGame().Run("Voxel Engine", 1280, 720);
    }
}

public sealed class VoxelGame : Game
{
    private GameObject cameraGO = null!;
    private Camera camera = null!;
    private Scene scene = null!;
    private VoxelWorld world = null!;

    public override void Initialize()
    {
        scene = new Scene();

        // Create directional light
        GameObject lightGO = new("Directional Light");
        var light = lightGO.AddComponent<DirectionalLight>();
        lightGO.Transform.position = new Double3(0, 64, 0);
        lightGO.Transform.localEulerAngles = new Double3(-45, 45, 0);
        light.shadowResolution = DirectionalLight.Resolution._4096;
        light.shadowDistance = 100f;
        scene.Add(lightGO);

        // Create camera
        cameraGO = new("Main Camera");
        cameraGO.tag = "Main Camera";
        cameraGO.Transform.position = new(0, 70, 0);
        camera = cameraGO.AddComponent<Camera>();
        camera.Depth = -1;
        camera.HDR = true;

        var sl = cameraGO.AddComponent<SpotLight>();
        //sl.

        camera.Effects = new List<ImageEffect>()
        {
            new ScreenSpaceReflectionEffect(),
            new KawaseBloomEffect(),
            new BokehDepthOfFieldEffect(),
            new TonemapperEffect(),
        };

        scene.Add(cameraGO);

        // Create voxel world
        GameObject worldGO = new GameObject("VoxelWorld");
        world = worldGO.AddComponent<VoxelWorld>();
        scene.Add(worldGO);

        world.GenerateWorld();
    }

    public override void FixedUpdate()
    {
        scene.FixedUpdate();
    }

    public override void Render()
    {
        scene.RenderScene();
    }

    public override void Update()
    {
        scene.Update();

        // WASD movement
        Double2 movement = Double2.Zero;
        if (Input.GetKey(Key.W)) movement += Double2.UnitY;
        if (Input.GetKey(Key.S)) movement -= Double2.UnitY;
        if (Input.GetKey(Key.A)) movement -= Double2.UnitX;
        if (Input.GetKey(Key.D)) movement += Double2.UnitX;

        float speed = Input.GetKey(Key.ShiftLeft) ? 20f : 10f;

        // forward/back
        cameraGO.Transform.position += cameraGO.Transform.forward * movement.Y * speed * Time.deltaTime;
        // left/right
        cameraGO.Transform.position += cameraGO.Transform.right * movement.X * speed * Time.deltaTime;

        // up/down with Q/E
        float upDown = 0;
        if (Input.GetKey(Key.E)) upDown += 1;
        if (Input.GetKey(Key.Q)) upDown -= 1;
        cameraGO.Transform.position += Double3.UnitY * upDown * speed * Time.deltaTime;

        // rotate with mouse
        if (Input.GetMouseButton(1))
        {
            Double2 delta = Input.MouseDelta;
            cameraGO.Transform.localEulerAngles += new Double3(delta.Y, delta.X, 0) * 0.1f;
        }

        // Voxel editing
        if (Input.GetMouseButtonDown(0)) // Left click to destroy
        {
            var ray = camera.ScreenPointToRay(Input.MousePosition, new Int2(Window.Size.X, Window.Size.Y));
            world.RaycastVoxel(ray, 10f, true);
        }
        else if (Input.GetMouseButtonDown(2)) // Middle click to place
        {
            var ray = camera.ScreenPointToRay(Input.MousePosition, new Int2(Window.Size.X, Window.Size.Y));
            world.RaycastVoxel(ray, 10f, false);
        }
    }
}

public class VoxelWorld : MonoBehaviour
{
    private const int ChunkWidth = 16;
    private const int ChunkHeight = 256;
    private const int ChunkDepth = 16;
    private const int RenderDistance = 3; // Chunks in each direction

    private Dictionary<Int3, VoxelChunk> chunks = new Dictionary<Int3, VoxelChunk>();

    public void GenerateWorld()
    {
        // Generate chunks in a grid around origin
        for (int x = -RenderDistance; x <= RenderDistance; x++)
        {
            for (int z = -RenderDistance; z <= RenderDistance; z++)
            {
                CreateChunk(new Int3(x, 0, z));
            }
        }
    }

    private void CreateChunk(Int3 chunkPos)
    {
        GameObject chunkGO = new GameObject($"Chunk_{chunkPos.X}_{chunkPos.Y}_{chunkPos.Z}");
        chunkGO.Transform.position = new Double3(
            chunkPos.X * ChunkWidth,
            chunkPos.Y * ChunkHeight,
            chunkPos.Z * ChunkDepth
        );

        var chunk = chunkGO.AddComponent<VoxelChunk>();
        chunk.Initialize(chunkPos, this);
        chunk.GenerateChunk();

        chunks[chunkPos] = chunk;
        GameObject.Scene.Add(chunkGO);
    }

    public byte GetVoxel(Int3 worldPos)
    {
        Int3 chunkPos = WorldToChunkPos(worldPos);
        if (!chunks.TryGetValue(chunkPos, out var chunk))
            return 0;

        Int3 localPos = WorldToLocalPos(worldPos);
        return chunk.GetVoxel(localPos.X, localPos.Y, localPos.Z);
    }

    public void SetVoxel(Int3 worldPos, byte value)
    {
        Int3 chunkPos = WorldToChunkPos(worldPos);
        if (!chunks.TryGetValue(chunkPos, out var chunk))
            return;

        Int3 localPos = WorldToLocalPos(worldPos);
        chunk.SetVoxel(localPos.X, localPos.Y, localPos.Z, value);

        // Update neighboring chunks if on edge
        if (localPos.X == 0 && chunks.TryGetValue(chunkPos + new Int3(-1, 0, 0), out var leftChunk))
            leftChunk.RegenerateMesh();
        if (localPos.X == ChunkWidth - 1 && chunks.TryGetValue(chunkPos + new Int3(1, 0, 0), out var rightChunk))
            rightChunk.RegenerateMesh();
        if (localPos.Z == 0 && chunks.TryGetValue(chunkPos + new Int3(0, 0, -1), out var backChunk))
            backChunk.RegenerateMesh();
        if (localPos.Z == ChunkDepth - 1 && chunks.TryGetValue(chunkPos + new Int3(0, 0, 1), out var frontChunk))
            frontChunk.RegenerateMesh();
    }

    public bool RaycastVoxel(RayD ray, float maxDistance, bool destroy)
    {
        // DDA Voxel Traversal
        Double3 rayPos = ray.Origin;
        Double3 rayDir = Maths.Normalize(ray.Direction);

        // Current voxel position
        Int3 voxelPos = new Int3(
            (int)Math.Floor(rayPos.X),
            (int)Math.Floor(rayPos.Y),
            (int)Math.Floor(rayPos.Z)
        );

        // Step direction for each axis
        Int3 step = new Int3(
            rayDir.X > 0 ? 1 : -1,
            rayDir.Y > 0 ? 1 : -1,
            rayDir.Z > 0 ? 1 : -1
        );

        // Distance to next voxel boundary on each axis
        Double3 tDelta = new Double3(
            Math.Abs(1.0 / rayDir.X),
            Math.Abs(1.0 / rayDir.Y),
            Math.Abs(1.0 / rayDir.Z)
        );

        // Initial t values to reach next voxel boundary
        Double3 tMax = new Double3(
            rayDir.X > 0 ? (voxelPos.X + 1 - rayPos.X) / rayDir.X : (rayPos.X - voxelPos.X) / -rayDir.X,
            rayDir.Y > 0 ? (voxelPos.Y + 1 - rayPos.Y) / rayDir.Y : (rayPos.Y - voxelPos.Y) / -rayDir.Y,
            rayDir.Z > 0 ? (voxelPos.Z + 1 - rayPos.Z) / rayDir.Z : (rayPos.Z - voxelPos.Z) / -rayDir.Z
        );

        double distance = 0;
        Int3 previousVoxel = voxelPos;

        while (distance < maxDistance)
        {
            // Check current voxel
            byte voxel = GetVoxel(voxelPos);
            if (voxel != 0) // Hit a solid voxel
            {
                if (destroy)
                {
                    SetVoxel(voxelPos, 0); // Destroy voxel
                }
                else
                {
                    SetVoxel(previousVoxel, 1); // Place voxel at previous position (stone)
                }
                return true;
            }

            previousVoxel = voxelPos;

            // Advance to next voxel
            if (tMax.X < tMax.Y)
            {
                if (tMax.X < tMax.Z)
                {
                    voxelPos.X += step.X;
                    distance = tMax.X;
                    tMax.X += tDelta.X;
                }
                else
                {
                    voxelPos.Z += step.Z;
                    distance = tMax.Z;
                    tMax.Z += tDelta.Z;
                }
            }
            else
            {
                if (tMax.Y < tMax.Z)
                {
                    voxelPos.Y += step.Y;
                    distance = tMax.Y;
                    tMax.Y += tDelta.Y;
                }
                else
                {
                    voxelPos.Z += step.Z;
                    distance = tMax.Z;
                    tMax.Z += tDelta.Z;
                }
            }
        }

        return false;
    }

    private Int3 WorldToChunkPos(Int3 worldPos)
    {
        return new Int3(
            worldPos.X >= 0 ? worldPos.X / ChunkWidth : (worldPos.X - ChunkWidth + 1) / ChunkWidth,
            0, // Single layer of chunks vertically for now
            worldPos.Z >= 0 ? worldPos.Z / ChunkDepth : (worldPos.Z - ChunkDepth + 1) / ChunkDepth
        );
    }

    private Int3 WorldToLocalPos(Int3 worldPos)
    {
        int localX = worldPos.X >= 0 ? worldPos.X % ChunkWidth : (ChunkWidth - 1 - ((-worldPos.X - 1) % ChunkWidth));
        int localZ = worldPos.Z >= 0 ? worldPos.Z % ChunkDepth : (ChunkDepth - 1 - ((-worldPos.Z - 1) % ChunkDepth));

        return new Int3(localX, worldPos.Y, localZ);
    }
}

public class VoxelChunk : MonoBehaviour
{
    private const int ChunkWidth = 16;
    private const int ChunkHeight = 256;
    private const int ChunkDepth = 16;

    private byte[,,] voxels = new byte[ChunkWidth, ChunkHeight, ChunkDepth];
    private MeshRenderer? meshRenderer;
    private Int3 chunkPosition;
    private VoxelWorld? world;

    public void Initialize(Int3 chunkPos, VoxelWorld voxelWorld)
    {
        chunkPosition = chunkPos;
        world = voxelWorld;
    }

    public override void Awake()
    {
        meshRenderer = GameObject.AddComponent<MeshRenderer>();
        meshRenderer.Material = new Material(Shader.LoadDefault(DefaultShader.Standard));
    }

    public byte GetVoxel(int x, int y, int z)
    {
        if (x < 0 || x >= ChunkWidth || y < 0 || y >= ChunkHeight || z < 0 || z >= ChunkDepth)
            return 0;
        return voxels[x, y, z];
    }

    public void SetVoxel(int x, int y, int z, byte value)
    {
        if (x < 0 || x >= ChunkWidth || y < 0 || y >= ChunkHeight || z < 0 || z >= ChunkDepth)
            return;
        voxels[x, y, z] = value;
        RegenerateMesh();
    }

    public void GenerateChunk()
    {
        // Generate simple terrain with world coordinates
        int worldOffsetX = chunkPosition.X * ChunkWidth;
        int worldOffsetZ = chunkPosition.Z * ChunkDepth;

        for (int x = 0; x < ChunkWidth; x++)
        {
            for (int z = 0; z < ChunkDepth; z++)
            {
                // Use world coordinates for noise
                float worldX = worldOffsetX + x;
                float worldZ = worldOffsetZ + z;

                // Create a simple height map
                int baseHeight = 64;
                int heightVariation = (int)(Math.Sin(worldX * 0.1) * 10 + Math.Cos(worldZ * 0.1) * 10 +
                                           Math.Sin(worldX * 0.05) * 5 + Math.Cos(worldZ * 0.05) * 5);
                int height = baseHeight + heightVariation;

                for (int y = 0; y < ChunkHeight; y++)
                {
                    if (y < height - 5)
                    {
                        voxels[x, y, z] = 1; // Stone
                    }
                    else if (y < height - 1)
                    {
                        voxels[x, y, z] = 2; // Dirt
                    }
                    else if (y < height)
                    {
                        voxels[x, y, z] = 3; // Grass
                    }
                    else
                    {
                        voxels[x, y, z] = 0; // Air
                    }
                }
            }
        }

        RegenerateMesh();
    }

    public void RegenerateMesh()
    {
        GenerateMesh();
    }

    private void GenerateMesh()
    {
        List<Double3> vertices = new List<Double3>();
        List<int> triangles = new List<int>();
        List<Float4> colors = new List<Float4>();

        for (int x = 0; x < ChunkWidth; x++)
        {
            for (int y = 0; y < ChunkHeight; y++)
            {
                for (int z = 0; z < ChunkDepth; z++)
                {
                    if (voxels[x, y, z] == 0) continue; // Skip air

                    Float4 voxelColor = GetVoxelColor(voxels[x, y, z]);

                    // Check each face and only add if adjacent voxel is air
                    // Top face (+Y)
                    if (y == ChunkHeight - 1 || voxels[x, y + 1, z] == 0)
                        AddFace(vertices, triangles, colors, x, y, z, 0, voxelColor);

                    // Bottom face (-Y)
                    if (y == 0 || voxels[x, y - 1, z] == 0)
                        AddFace(vertices, triangles, colors, x, y, z, 1, voxelColor);

                    // Front face (+Z)
                    if (z == ChunkDepth - 1 || voxels[x, y, z + 1] == 0)
                        AddFace(vertices, triangles, colors, x, y, z, 2, voxelColor);

                    // Back face (-Z)
                    if (z == 0 || voxels[x, y, z - 1] == 0)
                        AddFace(vertices, triangles, colors, x, y, z, 3, voxelColor);

                    // Right face (+X)
                    if (x == ChunkWidth - 1 || voxels[x + 1, y, z] == 0)
                        AddFace(vertices, triangles, colors, x, y, z, 4, voxelColor);

                    // Left face (-X)
                    if (x == 0 || voxels[x - 1, y, z] == 0)
                        AddFace(vertices, triangles, colors, x, y, z, 5, voxelColor);
                }
            }
        }

        if (vertices.Count == 0)
        {
            // Clear mesh if empty
            if (meshRenderer?.Mesh != null)
            {
                meshRenderer.Mesh = null!;
            }
            return;
        }

        // Create mesh
        Mesh mesh = new Mesh();
        mesh.Vertices = vertices.Select(v => new Float3((float)v.X, (float)v.Y, (float)v.Z)).ToArray();
        mesh.Indices = triangles.Select(i => (uint)i).ToArray();
        mesh.Colors = colors.ToArray();

        // Generate normals for proper lighting
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        meshRenderer!.Mesh = mesh;
    }

    private Float4 GetVoxelColor(byte voxelType)
    {
        return voxelType switch
        {
            1 => new Float4(0.5f, 0.5f, 0.5f, 1f),    // Stone - gray
            2 => new Float4(0.6f, 0.4f, 0.2f, 1f),    // Dirt - brown
            3 => new Float4(0.2f, 0.8f, 0.2f, 1f),    // Grass - green
            _ => Colors.White
        };
    }

    private void AddFace(List<Double3> vertices, List<int> triangles, List<Float4> colors,
                        int x, int y, int z, int face, Float4 color)
    {
        int vertexIndex = vertices.Count;

        // Define the 4 vertices of the face based on face direction
        Double3[] faceVertices = face switch
        {
            0 => [ // Top (+Y)
                new Double3(x, y + 1, z),
                new Double3(x, y + 1, z + 1),
                new Double3(x + 1, y + 1, z + 1),
                new Double3(x + 1, y + 1, z)
            ],
            1 => [ // Bottom (-Y)
                new Double3(x, y, z + 1),
                new Double3(x, y, z),
                new Double3(x + 1, y, z),
                new Double3(x + 1, y, z + 1),
            ],
            2 => [ // Front (+Z)
                new Double3(x, y, z + 1),
                new Double3(x + 1, y, z + 1),
                new Double3(x + 1, y + 1, z + 1),
                new Double3(x, y + 1, z + 1)
            ],
            3 => [ // Back (-Z)
                new Double3(x + 1, y, z),
                new Double3(x, y, z),
                new Double3(x, y + 1, z),
                new Double3(x + 1, y + 1, z)
            ],
            4 => [ // Right (+X)
                new Double3(x + 1, y, z + 1),
                new Double3(x + 1, y, z),
                new Double3(x + 1, y + 1, z),
                new Double3(x + 1, y + 1, z + 1)
            ],
            5 => [ // Left (-X)
                new Double3(x, y, z),
                new Double3(x, y, z + 1),
                new Double3(x, y + 1, z + 1),
                new Double3(x, y + 1, z)
            ],
            _ => throw new ArgumentException("Invalid face index")
        };

        // Add vertices
        foreach (var vertex in faceVertices)
        {
            vertices.Add(vertex);
            colors.Add(color);
        }

        // Add triangles (two triangles per face)
        triangles.Add(vertexIndex);
        triangles.Add(vertexIndex + 1);
        triangles.Add(vertexIndex + 2);

        triangles.Add(vertexIndex);
        triangles.Add(vertexIndex + 2);
        triangles.Add(vertexIndex + 3);
    }
}

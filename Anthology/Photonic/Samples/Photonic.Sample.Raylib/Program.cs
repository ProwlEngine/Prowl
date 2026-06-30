using System;
using System.Collections.Generic;
using System.Numerics;
using Photonic.Demo;            // re-using CombinedSponza + TrianglePacker
using Prowl.Photonic;
using Raylib_cs;
using static Raylib_cs.Raylib;

// Both Prowl.Vector and Raylib_cs declare Color/Float3/etc. Alias the Prowl ones we use so the
// compiler doesn't see them as ambiguous against Raylib's types.
using Float2 = Prowl.Vector.Float2;
using Float3 = Prowl.Vector.Float3;
using Float4 = Prowl.Vector.Float4;
using Float4x4 = Prowl.Vector.Float4x4;

namespace Photonic.Sample.Raylib;

/// <summary>
/// Minimal Raylib-cs sample: load Sponza, bake a lightmap with the per-texel path tracer,
/// render the result. Uses only Prowl.Photonic's public API.
/// </summary>
internal static class Program
{
    // Asset path resolved next to the executable; the .csproj copies the Sponza glTF tree there.
    private static readonly string SponzaPath = System.IO.Path.Combine(System.AppContext.BaseDirectory, "Assets", "Sponza", "glTF", "Sponza.gltf");

    private const int AtlasSize = 512;
    private const int WindowWidth = 1280;
    private const int WindowHeight = 720;

    private static unsafe int Main()
    {
        // ---- load & unwrap Sponza ------------------------------------------------------------
        Console.WriteLine("Loading Sponza...");
        CombinedSponza sponza;
        try
        {
            sponza = CombinedSponza.Load(SponzaPath, CombinedSponza.UV1Mode.AutoUnwrap, AtlasSize, AtlasSize, Console.WriteLine);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to load Sponza: {ex.Message}");
            return 1;
        }

        // ---- build the Prowl.Photonic bake scene ---------------------------------------------
        using var baker = new LightmapBaker();
        baker.Options.PathTracer = PathTracerKind.PerTexel;
        baker.Options.Bounces = 2;
        baker.Options.SamplesPerIteration = 1;
        baker.Options.IncludeDirectLighting = true;
        baker.Options.DilatePixels = 2;
        baker.Options.SkyColor = new Float3(0.05f, 0.07f, 0.12f);

        var target = baker.CreateTextureTarget("atlas", AtlasSize, AtlasSize);
        var scene = baker.BeginScene("Sponza");

        // textures + materials
        var bakeTextures = new BakeTexture?[sponza.Textures.Length];
        for (int t = 0; t < sponza.Textures.Length; t++)
        {
            var blob = sponza.Textures[t];
            if (blob is null) continue;
            bakeTextures[t] = scene.CreateTextureRGBA($"tex_{t}", blob.Width, blob.Height, blob.RGBA, inputGamma: 2.2f);
        }
        for (int m = 0; m < sponza.Materials.Length; m++)
        {
            var info = sponza.Materials[m];
            var bm = scene.CreateMaterial($"mat_{m}");
            bm.DiffuseColor = info.BaseColor;
            if (info.DiffuseTextureIndex >= 0)
                bm.DiffuseTexture = bakeTextures[info.DiffuseTextureIndex];
        }

        // mesh as a single BakeMesh with one MaterialGroup per submesh slice
        var meshBuilder = scene.BeginMesh("sponza")
            .AddVertices(sponza.Vertices, sponza.Normals)
            .AddUVLayer("UV0", sponza.UV0)
            .AddUVLayer("UV1", sponza.UV1);
        foreach (var slice in sponza.SubMeshes)
        {
            var slIndices = new int[slice.IndexCount];
            Array.Copy(sponza.Indices, slice.IndexStart, slIndices, 0, slice.IndexCount);
            meshBuilder.AddMaterialGroup($"mat_{slice.MaterialIndex}", slIndices);
        }
        var bakeMesh = meshBuilder.End();
        target.AddBakeInstance(bakeMesh, Float4x4.Identity);

        // one sun-ish directional light
        scene.CreateDirectionalLight("sun",
            Float4x4.Identity,
            new Float3(8f, 7f, 6f));
        // bias the direction by overwriting the transform's forward
        scene.Lights[0].Transform = LookDirection(new Float3(-0.4f, -0.8f, 0.2f));

        scene.End();

        Console.WriteLine("Starting bake...");
        var job = baker.Start();

        // ---- raylib window + GPU upload --------------------------------------------------------
        InitWindow(WindowWidth, WindowHeight, "Photonic.Sample.Raylib -- Sponza per-texel bake");
        SetTargetFPS(60);
        SetExitKey(KeyboardKey.Null);

        var camera = new Camera3D
        {
            Position = new Vector3(-9f, 4.5f, 0f),
            Target = new Vector3(0f, 4f, 0f),
            Up = new Vector3(0f, 1f, 0f),
            FovY = 60f,
            Projection = CameraProjection.Perspective,
        };

        var shader = LoadShaderFromMemory(VertexShader, FragmentShader);
        int locExposure   = GetShaderLocation(shader, "uExposure");
        int locHasDiffuse = GetShaderLocation(shader, "uHasDiffuse");

        // per-material diffuse textures
        var materialTextures = new Texture2D[sponza.Materials.Length];
        for (int m = 0; m < sponza.Materials.Length; m++)
        {
            int ti = sponza.Materials[m].DiffuseTextureIndex;
            if (ti < 0 || sponza.Textures[ti] is null) continue;
            materialTextures[m] = UploadRGBA8Texture(sponza.Textures[ti]!);
        }

        // one Raylib Mesh per submesh (compact-indexed to stay under the ushort index limit)
        var subMeshes = new Mesh[sponza.SubMeshes.Length];
        for (int s = 0; s < sponza.SubMeshes.Length; s++)
            subMeshes[s] = BuildSubmeshMesh(sponza, sponza.SubMeshes[s]);

        // lightmap texture (re-uploaded when the job's iteration count changes)
        Texture2D lightmapTex = CreateBlankRGBATexture(AtlasSize, AtlasSize);
        int lastUploadedIter = -1;
        float exposure = 1.0f;

        // Single material reused across submeshes. We assign our shader once and swap the Albedo
        // texture / base color per submesh. The lightmap is bound to the Metalness map slot, which
        // raylib auto-binds to the shader's "texture1" uniform during DrawMesh. Crucially we do NOT
        // call UnloadMaterial inside the loop, otherwise raylib would also unload material.Shader
        // (our shared shader), wrecking the GL state on the next frame.
        var material = LoadMaterialDefault();
        material.Shader = shader;
        SetMaterialTexture(ref material, MaterialMapIndex.Metalness, lightmapTex);

        // ---- main loop -----------------------------------------------------------------------
        while (!WindowShouldClose())
        {
            // Re-upload the atlas whenever the bake has folded in another iteration.
            if (job.IterationCount != lastUploadedIter)
            {
                lastUploadedIter = job.IterationCount;
                UploadLightmap(ref lightmapTex, target);
            }

            // Camera controls: WASD/QE, mouse-drag with right button to look.
            UpdateCameraSimple(ref camera, GetFrameTime());

            // Hotkeys
            if (IsKeyPressed(KeyboardKey.R)) job.Cancel();
            if (IsKeyPressed(KeyboardKey.E)) exposure *= 1.25f;
            if (IsKeyPressed(KeyboardKey.Q)) exposure /= 1.25f;

            BeginDrawing();
            ClearBackground(new Color(15, 18, 22, 255));

            BeginMode3D(camera);
            SetShaderValue(shader, locExposure, exposure, ShaderUniformDataType.Float);

            for (int s = 0; s < subMeshes.Length; s++)
            {
                int matIdx = sponza.SubMeshes[s].MaterialIndex;
                var diffuseTex = matIdx >= 0 ? materialTextures[matIdx] : default;
                bool hasDiffuse = diffuseTex.Id != 0;
                SetShaderValue(shader, locHasDiffuse, hasDiffuse ? 1f : 0f, ShaderUniformDataType.Float);

                SetMaterialTexture(ref material, MaterialMapIndex.Albedo, hasDiffuse ? diffuseTex : lightmapTex);
                material.Maps[(int)MaterialMapIndex.Albedo].Color = matIdx >= 0
                    ? ToColor(sponza.Materials[matIdx].BaseColor)
                    : new Color(180, 180, 180, 255);
                DrawMesh(subMeshes[s], material, Matrix4x4.Identity);
            }
            EndMode3D();

            // Atlas thumbnail (bottom-right)
            int thumbSize = 200;
            int thumbX = GetScreenWidth() - thumbSize - 12;
            int thumbY = GetScreenHeight() - thumbSize - 12;
            DrawRectangle(thumbX - 2, thumbY - 2, thumbSize + 4, thumbSize + 4, new Color(40, 40, 40, 200));
            DrawTexturePro(lightmapTex,
                new Rectangle(0, 0, lightmapTex.Width, lightmapTex.Height),
                new Rectangle(thumbX, thumbY, thumbSize, thumbSize),
                new Vector2(0, 0), 0f, Color.White);
            DrawText("atlas", thumbX + 4, thumbY + 4, 16, Color.RayWhite);

            // HUD
            DrawText($"Iter: {job.IterationCount}     Status: {job.Activity}", 12, 12, 18, Color.RayWhite);
            DrawText("Q / E: exposure   R: cancel bake   right-mouse drag + WASD: fly camera", 12, 36, 14, Color.LightGray);
            DrawText($"Exposure: {exposure:0.00}", 12, 56, 14, Color.LightGray);

            EndDrawing();
        }

        baker.Cancel();
        job.Wait();
        CloseWindow();
        return 0;
    }

    // ---- camera ----------------------------------------------------------------------------

    private static float _yaw   = (float)Math.PI;     // start looking down +X (Sponza nave runs east-west)
    private static float _pitch = -0.05f;

    private static void UpdateCameraSimple(ref Camera3D cam, float dt)
    {
        if (IsMouseButtonDown(MouseButton.Right))
        {
            var d = GetMouseDelta();
            _yaw   += d.X * 0.003f;
            _pitch -= d.Y * 0.003f;
            _pitch = Math.Clamp(_pitch, -1.5f, 1.5f);
        }

        var fwd = new Vector3(
            (float)(Math.Cos(_pitch) * Math.Cos(_yaw)),
            (float)Math.Sin(_pitch),
            (float)(Math.Cos(_pitch) * Math.Sin(_yaw)));
        var right = Vector3.Normalize(Vector3.Cross(fwd, new Vector3(0, 1, 0)));

        float speed = IsKeyDown(KeyboardKey.LeftShift) ? 12f : 4f;
        if (IsKeyDown(KeyboardKey.W)) cam.Position += fwd * speed * dt;
        if (IsKeyDown(KeyboardKey.S)) cam.Position -= fwd * speed * dt;
        if (IsKeyDown(KeyboardKey.A)) cam.Position -= right * speed * dt;
        if (IsKeyDown(KeyboardKey.D)) cam.Position += right * speed * dt;
        if (IsKeyDown(KeyboardKey.Space))       cam.Position += new Vector3(0, speed * dt, 0);
        if (IsKeyDown(KeyboardKey.LeftControl)) cam.Position -= new Vector3(0, speed * dt, 0);

        cam.Target = cam.Position + fwd;
    }

    // ---- mesh upload -----------------------------------------------------------------------

    private static unsafe Mesh BuildSubmeshMesh(CombinedSponza sponza, CombinedSponza.SubMeshSlice slice)
    {
        // Compact: gather only the verts this submesh actually references, remap indices to 0..n.
        var remap = new Dictionary<int, ushort>(slice.IndexCount);
        var localPositions = new List<Float3>(slice.IndexCount);
        var localNormals   = new List<Float3>(slice.IndexCount);
        var localUV0       = new List<Float2>(slice.IndexCount);
        var localUV1       = new List<Float2>(slice.IndexCount);
        var localIndices   = new List<ushort>(slice.IndexCount);

        for (int k = 0; k < slice.IndexCount; k++)
        {
            int srcV = sponza.Indices[slice.IndexStart + k];
            if (!remap.TryGetValue(srcV, out ushort dst))
            {
                if (localPositions.Count > 65535)
                    throw new InvalidOperationException("Submesh exceeds raylib's ushort index limit; split required.");
                dst = (ushort)localPositions.Count;
                remap[srcV] = dst;
                localPositions.Add(sponza.Vertices[srcV]);
                localNormals.Add(sponza.Normals[srcV]);
                localUV0.Add(sponza.UV0[srcV]);
                localUV1.Add(sponza.UV1[srcV]);
            }
            localIndices.Add(dst);
        }

        int vertexCount = localPositions.Count;
        int triangleCount = localIndices.Count / 3;
        var mesh = new Mesh
        {
            VertexCount = vertexCount,
            TriangleCount = triangleCount,
        };
        mesh.AllocVertices();
        mesh.AllocTexCoords();
        mesh.AllocTexCoords2();
        mesh.AllocNormals();
        mesh.AllocIndices();

        var verts   = mesh.VerticesAs<Vector3>();
        var tcs     = mesh.TexCoordsAs<Vector2>();
        var tcs2    = mesh.TexCoords2As<Vector2>();
        var norms   = mesh.NormalsAs<Vector3>();
        var indices = mesh.IndicesAs<ushort>();

        for (int i = 0; i < vertexCount; i++)
        {
            var p = localPositions[i];
            var n = localNormals[i];
            var u0 = localUV0[i];
            var u1 = localUV1[i];
            verts[i] = new Vector3((float)p.X, (float)p.Y, (float)p.Z);
            norms[i] = new Vector3((float)n.X, (float)n.Y, (float)n.Z);
            tcs[i]   = new Vector2((float)u0.X, (float)u0.Y);
            tcs2[i]  = new Vector2((float)u1.X, (float)u1.Y);
        }
        for (int i = 0; i < localIndices.Count; i++) indices[i] = localIndices[i];

        UploadMesh(ref mesh, false);
        return mesh;
    }

    private static unsafe Texture2D UploadRGBA8Texture(CombinedSponza.TextureBlob blob)
    {
        fixed (byte* p = blob.RGBA)
        {
            var img = new Image
            {
                Data = p,
                Width = blob.Width,
                Height = blob.Height,
                Mipmaps = 1,
                Format = PixelFormat.UncompressedR8G8B8A8,
            };
            var tex = LoadTextureFromImage(img);
            GenTextureMipmaps(ref tex);
            SetTextureFilter(tex, TextureFilter.Trilinear);
            SetTextureWrap(tex, TextureWrap.Repeat);
            return tex;
        }
    }

    private static unsafe Texture2D CreateBlankRGBATexture(int w, int h)
    {
        var bytes = new byte[w * h * 4];
        fixed (byte* p = bytes)
        {
            var img = new Image
            {
                Data = p,
                Width = w,
                Height = h,
                Mipmaps = 1,
                Format = PixelFormat.UncompressedR8G8B8A8,
            };
            var tex = LoadTextureFromImage(img);
            SetTextureFilter(tex, TextureFilter.Bilinear);
            SetTextureWrap(tex, TextureWrap.Clamp);
            return tex;
        }
    }

    private static unsafe void UploadLightmap(ref Texture2D tex, LightmapTarget src)
    {
        // ReadLDR gives RGB; we pad to RGBA8 for Raylib's update path.
        var rgb = src.ReadLDR(exposure: 1.0f, gamma: 1f / 2.2f);
        int pixelCount = src.Width * src.Height;
        var rgba = new byte[pixelCount * 4];
        for (int i = 0; i < pixelCount; i++)
        {
            rgba[i * 4    ] = rgb[i * 3    ];
            rgba[i * 4 + 1] = rgb[i * 3 + 1];
            rgba[i * 4 + 2] = rgb[i * 3 + 2];
            rgba[i * 4 + 3] = 255;
        }
        fixed (byte* p = rgba)
        {
            UpdateTexture(tex, p);
        }
    }

    // ---- helpers ---------------------------------------------------------------------------

    private static Color ToColor(Float3 c)
    {
        byte r = (byte)Math.Clamp((int)(c.X * 255f), 0, 255);
        byte g = (byte)Math.Clamp((int)(c.Y * 255f), 0, 255);
        byte b = (byte)Math.Clamp((int)(c.Z * 255f), 0, 255);
        return new Color(r, g, b, (byte)255);
    }

    /// <summary>
    /// Build a transform whose +Z column points along <paramref name="dir"/>, which is the
    /// direction the light travels. Surfaces receive radiance from -dir, per
    /// <see cref="Prowl.Photonic.Scene.Lights.DirectionalLight"/>'s convention.
    /// </summary>
    private static Float4x4 LookDirection(Float3 dir)
    {
        var f = Float3.Normalize(dir);
        var m = Float4x4.Identity;
        m.c2 = new Float4(f.X, f.Y, f.Z, 0);
        return m;
    }

    // ---- shaders ---------------------------------------------------------------------------

    private const string VertexShader = @"
#version 330

in vec3 vertexPosition;
in vec2 vertexTexCoord;
in vec2 vertexTexCoord2;
in vec3 vertexNormal;

uniform mat4 mvp;

out vec2 fragTexCoord;
out vec2 fragTexCoord2;

void main()
{
    fragTexCoord  = vertexTexCoord;
    fragTexCoord2 = vertexTexCoord2;
    gl_Position   = mvp * vec4(vertexPosition, 1.0);
}
";

    private const string FragmentShader = @"
#version 330

in vec2 fragTexCoord;
in vec2 fragTexCoord2;

uniform sampler2D texture0;   // diffuse
uniform sampler2D texture1;   // lightmap
uniform vec4 colDiffuse;
uniform float uHasDiffuse;
uniform float uExposure;

out vec4 finalColor;

void main()
{
    vec3 albedo = (uHasDiffuse > 0.5) ? texture(texture0, fragTexCoord).rgb : vec3(1.0);
    albedo *= colDiffuse.rgb;
    vec3 light = texture(texture1, fragTexCoord2).rgb;
    vec3 lit = albedo * light * uExposure;
    finalColor = vec4(lit, 1.0);
}
";
}

// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

using Prowl.Aperture;
using Prowl.Runtime.Resources;

using Xunit;

namespace Prowl.Runtime.Test;

/// <summary>Reads a .crate file holding just a number, to prove a format the engine does not know can be loaded.</summary>
public sealed class CrateRuntimeImporter : RuntimeImporter
{
    public override IReadOnlyList<string> Extensions { get; } = [".crate"];

    public override Type AssetType => typeof(Crate);

    public override void Import(RuntimeImportContext context)
        => context.SetMain(new Crate { Size = int.Parse(context.ReadAllText().Trim()) });
}

/// <summary>Assets loaded straight from files in a folder, a zip or on a web server, through mounted sources.</summary>
public class AssetSourceTests : RuntimeTestBase
{
    private const string StandardShader = "87bedf66-4e8b-0056-bc2e-4f8c7fc22a5a";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "ProwlAssetSourceTests", Guid.NewGuid().ToString("N"));
    private readonly AssetBackend? _previous = AssetDatabase.Backend;

    public AssetSourceTests()
    {
        AssetDatabase.ClearForTests();
        AssetDatabase.Backend = null;
        RuntimeImporters.Register(new CrateRuntimeImporter());
        Directory.CreateDirectory(_root);
    }

    public override void Dispose()
    {
        AssetDatabase.Backend = _previous;
        AssetDatabase.ClearForTests();
        try { Directory.Delete(_root, true); } catch (IOException) { }
        base.Dispose();
        GC.SuppressFinalize(this);
    }

    private string Folder(string name)
    {
        string folder = Path.Combine(_root, name);
        Directory.CreateDirectory(folder);
        return folder;
    }

    private static void Write(string folder, string path, string text)
    {
        string full = Path.Combine(folder, path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, text);
    }

    private static void WritePng(string folder, string path, int size)
    {
        string full = Path.Combine(folder, path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        byte[] pixels = new byte[size * size * 4];
        Array.Fill(pixels, (byte)200);
        Image.FromPixels(pixels, size, size, PixelFormat.Rgba8).Save(full);
    }

    private static SourceAssetBackend MountFolder(string folder, string? name = null)
    {
        var backend = new SourceAssetBackend(new FolderAssetSource(folder, name));
        AssetDatabase.Mount(backend);
        return backend;
    }

    [Fact]
    public void AFileIsFoundByItsPathWithoutTheExtension()
    {
        string folder = Folder("Pack");
        Write(folder, "Crates/Big.crate", "12");
        MountFolder(folder);

        Crate crate = AssetDatabase.FindResource<Crate>("Crates/Big")!;

        Assert.NotNull(crate);
        Assert.Equal(12, crate.Size);
        Assert.Equal("Big", crate.Name);
    }

    [Fact]
    public void AnImageLoadsAsATexture()
    {
        string folder = Folder("Pack");
        WritePng(folder, "Textures/Paint.png", 8);
        MountFolder(folder);

        Texture2D texture = AssetDatabase.FindResource<Texture2D>("Textures/Paint")!;

        Assert.NotNull(texture);
        Assert.Equal(8u, texture.Width);
    }

    [Fact]
    public void AComputeShaderLoadsWithItsKernelsAndIncludes()
    {
        string folder = Folder("Pack");
        Write(folder, "Compute/Common.glsl", "float Twice(float x) { return x * 2.0; }");
        Write(folder, "Compute/Blur.compute", """
            #pragma kernel Horizontal 64
            #include "Common"
            void Horizontal() { }
            """);
        MountFolder(folder);

        ComputeShader shader = AssetDatabase.FindResource<ComputeShader>("Compute/Blur")!;

        Assert.NotNull(shader);
        int kernel = shader.FindKernel("Horizontal");
        shader.GetKernelThreadGroupSizes(kernel, out uint x, out _, out _);
        Assert.Equal(64u, x);
        Assert.Contains("float Twice(float x)", shader.KernelSource(kernel));
    }

    [Fact]
    public void AShaderInASubfolder_FindsTheIncludeBesideIt()
    {
        string folder = Folder("Pack");
        Write(folder, "Shaders/Common.glsl", "#define TINT_VALUE 0.5");
        Write(folder, "Shaders/Tint.shader", """
            Shader "Test/Tint"

            Pass "Forward"
            {
                GLSLPROGRAM
                    Vertex
                    {
                        #include "Common"
                        void main() { gl_Position = vec4(TINT_VALUE); }
                    }
                    Fragment
                    {
                        out vec4 color;
                        void main() { color = vec4(1.0); }
                    }
                ENDGLSL
            }
            """);
        MountFolder(folder);

        Shader shader = AssetDatabase.FindResource<Shader>("Shaders/Tint")!;

        Assert.NotNull(shader);
        Assert.Contains("#define TINT_VALUE 0.5", shader.GetPass(0).VertexSource);
    }

    [Fact]
    public void AMaterialNamesItsTextureByLoadPath()
    {
        string folder = Folder("Pack");
        WritePng(folder, "Textures/Paint.png", 4);
        Write(folder, "Materials/Painted.mat", $$"""
            {
              "$type": "Prowl.Runtime.Resources.Material, Prowl.Runtime",
              "_shader": { "$asset": "{{StandardShader}}" },
              "_properties": {
                "_textures": { "_MainTex": { "$asset": "Textures/Paint" } }
              },
              "Name": "Painted"
            }
            """);
        MountFolder(folder);

        Material material = AssetDatabase.FindResource<Material>("Materials/Painted")!;
        Texture2D paint = AssetDatabase.FindResource<Texture2D>("Textures/Paint")!;

        Assert.NotNull(material);
        Assert.Same(paint, material._properties.GetTexture("_MainTex"));
    }

    [Fact]
    public void AModelLoadsAsAPrefabWithItsMeshesAsSubAssets()
    {
        string folder = Folder("Pack");
        Write(folder, "Models/Wedge.obj", """
            o Wedge
            v 0 0 0
            v 1 0 0
            v 0 1 0
            v 0 0 1
            f 1 2 3
            f 1 3 4
            f 1 4 2
            f 2 4 3
            """);
        MountFolder(folder);

        PrefabAsset prefab = AssetDatabase.FindResource<PrefabAsset>("Models/Wedge")!;
        prefab.Load();

        Assert.True(prefab.IsLoaded);
        List<Mesh> meshes = AssetDatabase.FindAllResources<Mesh>("Models");
        Assert.NotEmpty(meshes);
        Assert.All(meshes, mesh => Assert.True(mesh.VertexCount > 0));
    }

    [Fact]
    public void AModelUsesTheMaterialFilesItNames()
    {
        string folder = Folder("Pack");
        Write(folder, "Models/Wedge.mtl", "newmtl Paint\nKd 1 0 0\n");
        Write(folder, "Models/Wedge.obj", """
            mtllib Wedge.mtl
            o Wedge
            v 0 0 0
            v 1 0 0
            v 0 1 0
            usemtl Paint
            f 1 2 3
            """);
        Write(folder, "Materials/Paint.mat", $$"""
            {
              "$type": "Prowl.Runtime.Resources.Material, Prowl.Runtime",
              "_shader": { "$asset": "{{StandardShader}}" },
              "Name": "Paint"
            }
            """);
        MountFolder(folder);

        PrefabAsset prefab = AssetDatabase.FindResource<PrefabAsset>("Models/Wedge")!;
        GameObject model = GameObject.InstantiateDetached(prefab)!;

        MeshRenderer renderer = model.GetComponentInChildren<MeshRenderer>()!;
        Assert.Same(AssetDatabase.FindResource<Material>("Materials/Paint"), renderer.Material);
    }

    [Fact]
    public void TheNewestMountWinsAndUnmountingGivesTheOldOneBack()
    {
        string baseGame = Folder("Base");
        string patch = Folder("Patch");
        Write(baseGame, "Crates/Box.crate", "1");
        Write(patch, "Crates/Box.crate", "2");
        MountFolder(baseGame);

        Assert.Equal(1, AssetDatabase.FindResource<Crate>("Crates/Box")!.Size);

        SourceAssetBackend patchBackend = MountFolder(patch);
        Assert.Equal(2, AssetDatabase.FindResource<Crate>("Crates/Box")!.Size);

        AssetDatabase.Unmount(patchBackend);
        Assert.Equal(1, AssetDatabase.FindResource<Crate>("Crates/Box")!.Size);
    }

    [Fact]
    public void UnmountingTheOnlySourceLeavesTheAssetMissing()
    {
        string folder = Folder("Pack");
        Write(folder, "Crates/Box.crate", "5");
        SourceAssetBackend backend = MountFolder(folder);
        Crate crate = AssetDatabase.FindResource<Crate>("Crates/Box")!;

        AssetDatabase.Unmount(backend);

        Assert.Equal(AssetState.Missing, crate.State);
        Assert.Null(AssetDatabase.FindResource<Crate>("Crates/Box"));
    }

    [Fact]
    public void AZipLoadsLikeAFolder()
    {
        string zipPath = Path.Combine(_root, "Pack.zip");
        using (ZipArchive zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        using (var writer = new StreamWriter(zip.CreateEntry("Crates/Zipped.crate").Open()))
            writer.Write("7");

        using var source = new ZipAssetSource(zipPath);
        AssetDatabase.Mount(new SourceAssetBackend(source));

        Assert.Equal(7, AssetDatabase.FindResource<Crate>("Crates/Zipped")!.Size);
    }

    private sealed class FakeServer(Dictionary<string, string> files) : HttpMessageHandler
    {
        public readonly List<string> Asked = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string path = request.RequestUri!.AbsolutePath.TrimStart('/');
            Asked.Add(path);
            var response = files.TryGetValue(path, out string? text)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(text) }
                : new HttpResponseMessage(HttpStatusCode.NotFound);
            return Task.FromResult(response);
        }
    }

    [Fact]
    public void AWebServerListedInAnIndexLoadsOnlyWhatIsAskedFor()
    {
        var server = new FakeServer(new()
        {
            ["dlc/index.txt"] = "# crates\nCrates/Far.crate\nCrates/Other.crate\n",
            ["dlc/Crates/Far.crate"] = "9",
            ["dlc/Crates/Other.crate"] = "3",
        });
        var source = new HttpAssetSource(new Uri("http://content.test/dlc"), "Dlc", new HttpClient(server));
        AssetDatabase.Mount(new SourceAssetBackend(source));

        Assert.Equal(9, AssetDatabase.FindResource<Crate>("Crates/Far")!.Size);
        Assert.Contains("dlc/Crates/Far.crate", server.Asked);
        Assert.DoesNotContain("dlc/Crates/Other.crate", server.Asked);
    }

    [Fact]
    public void ReloadingReadsTheChangedFile()
    {
        string folder = Folder("Pack");
        Write(folder, "Crates/Box.crate", "1");
        MountFolder(folder);
        Crate crate = AssetDatabase.FindResource<Crate>("Crates/Box")!;

        Write(folder, "Crates/Box.crate", "4");
        AssetDatabase.Reload(crate);

        Assert.Equal(4, crate.Size);
    }

    [Fact]
    public void TheSamePathInTwoSourcesIsTwoAssets()
    {
        string a = Folder("A");
        string b = Folder("B");
        Write(a, "Crates/Box.crate", "1");
        Write(b, "Crates/Box.crate", "2");

        var first = new SourceAssetBackend(new FolderAssetSource(a));
        var second = new SourceAssetBackend(new FolderAssetSource(b));

        Assert.NotEqual(first.GuidFor("Crates/Box.crate"), second.GuidFor("Crates/Box.crate"));
        Assert.Equal(first.GuidFor("Crates/Box.crate"), new SourceAssetBackend(new FolderAssetSource(a)).GuidFor("Crates/Box.crate"));
    }
}

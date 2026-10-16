// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Echo;
using Prowl.Editor.Importers;
using Prowl.Editor.Migration;
using Prowl.Editor.Migration.Releases;
using Prowl.Editor.Projects;
using Prowl.Runtime;
using Prowl.Runtime.Resources;

using Xunit;

namespace Prowl.Editor.Test;

public class TextureMigrationTests : IDisposable
{
    private readonly string _root;

    public TextureMigrationTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "ProwlTextureMigrationTest", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_root, "Assets"));
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
        GC.SuppressFinalize(this);
    }

    private static void RunTextureSteps(Project project)
    {
        var context = new MigrationContext(project);
        foreach (MigrationStep step in Release_1_0_Preview5.GraphiteTextureSteps)
            step.Run(context);
    }

    private static EchoObject OldImportSettings(int min, int mag, int wrap)
    {
        var settings = EchoObject.NewCompound();
        settings["generateMipmaps"] = new EchoObject(true);
        settings["minFilter"] = new EchoObject(min);
        settings["magFilter"] = new EchoObject(mag);
        settings["wrapMode"] = new EchoObject(wrap);
        return settings;
    }

    [Theory]
    [InlineData(5, 0, 0)] // LinearMipmapLinear with Nearest mag is Point
    [InlineData(0, 0, 0)]
    [InlineData(5, 1, 2)] // LinearMipmapLinear is Trilinear
    [InlineData(4, 1, 2)] // NearestMipmapLinear is Trilinear
    [InlineData(3, 1, 1)] // LinearMipmapNearest is Bilinear
    [InlineData(1, 1, 1)]
    public void ImportSettings_FilterFollowsMagThenMipBlend(int min, int mag, int expected)
    {
        EchoObject converted = Release_1_0_Preview5.ConvertTextureImportSettings(OldImportSettings(min, mag, 0))!;
        Assert.Equal(expected, converted["filter"].IntValue);
    }

    [Theory]
    [InlineData(0, WrapMode.Repeat)]
    [InlineData(1, WrapMode.Border)]
    [InlineData(2, WrapMode.Clamp)]
    [InlineData(3, WrapMode.Mirror)]
    public void ImportSettings_WrapBecomesPerAxis(int oldWrap, WrapMode expected)
    {
        EchoObject converted = Release_1_0_Preview5.ConvertTextureImportSettings(OldImportSettings(5, 1, oldWrap))!;
        Assert.Equal((int)expected, converted["wrapU"].IntValue);
        Assert.Equal((int)expected, converted["wrapV"].IntValue);
        Assert.False(converted.TryGet("minFilter", out _));
        Assert.False(converted.TryGet("magFilter", out _));
        Assert.False(converted.TryGet("wrapMode", out _));
        Assert.Null(Release_1_0_Preview5.ConvertTextureImportSettings(converted));
    }

    [Fact]
    public void ImportSettings_OtherImportersAreLeftAlone()
    {
        var settings = EchoObject.NewCompound();
        settings["scale"] = new EchoObject(1f);
        Assert.Null(Release_1_0_Preview5.ConvertTextureImportSettings(settings));
    }

    private static EchoObject OldTexture2D(int format, byte[] data, int width, int height, bool mipmapped, int wrap = 2)
    {
        var tag = EchoObject.NewCompound();
        tag["Width"] = new EchoObject((uint)width);
        tag["Height"] = new EchoObject((uint)height);
        tag["IsMipMapped"] = new EchoObject(mipmapped);
        tag["ImageFormat"] = new EchoObject(format);
        tag["MinFilter"] = new EchoObject(5);
        tag["MagFilter"] = new EchoObject(1);
        tag["Wrap"] = new EchoObject(wrap);
        tag["Data"] = new EchoObject(data);
        return tag;
    }

    [Fact]
    public void Texture2D_OldDataLoadsInTheNewShape()
    {
        byte[] pixels = [1, 2, 3, 4, 5, 6, 7, 8];
        EchoObject tag = OldTexture2D(26, pixels, 2, 1, mipmapped: true);
        EchoObject root = EchoObject.NewCompound();
        root["Embedded"] = tag;

        Assert.NotNull(Release_1_0_Preview5.ConvertTextureData(root));
        Assert.Null(Release_1_0_Preview5.ConvertTextureData(root));

        Assert.Equal((int)TextureFormat.RGBA8, tag["Format"].IntValue);
        Assert.True(tag["Srgb"].BoolValue);
        Assert.Equal((int)FilterMode.Trilinear, tag["Filter"].IntValue);
        Assert.Equal((int)WrapMode.Clamp, tag["WrapU"].IntValue);
        Assert.Equal((int)WrapMode.Clamp, tag["WrapV"].IntValue);
        Assert.Equal(pixels, tag["Mips"][0].ByteArrayValue);
        Assert.True(tag["GenerateMipmaps"].BoolValue);

        var texture = new Texture2D();
        texture.Deserialize(tag, new SerializationContext());
        Assert.Equal(TextureFormat.RGBA8, texture.Format);
        Assert.True(texture.Srgb);
        Assert.Equal(FilterMode.Trilinear, texture.Filter);
        Assert.Equal(WrapMode.Clamp, texture.WrapV);
        Assert.Equal(2u, texture.MipCount);
    }

    [Fact]
    public void Texture2D_ThreeChannelFloatsGainAnAlphaOfOne()
    {
        byte[] texel = [.. BitConverter.GetBytes(0.25f), .. BitConverter.GetBytes(0.5f), .. BitConverter.GetBytes(0.75f)];
        EchoObject tag = OldTexture2D(8, texel, 1, 1, mipmapped: false);
        Release_1_0_Preview5.ConvertTextureData(tag);

        byte[] data = tag["Mips"][0].ByteArrayValue;
        Assert.Equal((int)TextureFormat.RGBA32Float, tag["Format"].IntValue);
        Assert.Equal(16, data.Length);
        Assert.Equal(0.75f, BitConverter.ToSingle(data, 8));
        Assert.Equal(1f, BitConverter.ToSingle(data, 12));
        Assert.False(tag.TryGet("GenerateMipmaps", out _));
    }

    [Fact]
    public void Texture2D_OldShortsBecomeHalfFloats()
    {
        EchoObject tag = OldTexture2D(2, BitConverter.GetBytes((short)-32767), 1, 1, mipmapped: false);
        Release_1_0_Preview5.ConvertTextureData(tag);

        Assert.Equal((int)TextureFormat.R16Float, tag["Format"].IntValue);
        Assert.Equal(-1f, (float)BitConverter.ToHalf(tag["Mips"][0].ByteArrayValue, 0));
    }

    [Fact]
    public void Cubemap_FacesAreConverted()
    {
        var tag = EchoObject.NewCompound();
        tag["Size"] = new EchoObject(1u);
        tag["MipLevels"] = new EchoObject(1);
        tag["ImageFormat"] = new EchoObject(9);
        tag["MinFilter"] = new EchoObject(1);
        tag["MagFilter"] = new EchoObject(1);
        tag["Wrap"] = new EchoObject(2);
        EchoObject faces = EchoObject.NewList();
        for (int i = 0; i < 6; i++) faces.ListAdd(new EchoObject(new byte[16]));
        tag["Faces"] = faces;

        Release_1_0_Preview5.ConvertTextureData(tag);

        Assert.Equal((int)TextureFormat.RGBA32Float, tag["Format"].IntValue);
        Assert.Equal((int)FilterMode.Bilinear, tag["Filter"].IntValue);
        Assert.Equal(6, tag["Faces"].Count);
        Assert.False(tag.TryGet("ImageFormat", out _));
    }

    [Fact]
    public void RenderTexture_FormatsAndFlagsConvert()
    {
        var tag = EchoObject.NewCompound();
        tag["Width"] = new EchoObject(64);
        tag["Height"] = new EchoObject(32);
        tag["HasDepthAttachment"] = new EchoObject((byte)1);
        EchoObject formats = EchoObject.NewList();
        formats.ListAdd(new EchoObject((byte)26));
        formats.ListAdd(new EchoObject((byte)5));
        tag["TextureFormats"] = formats;
        tag["EnableRandomWrite"] = new EchoObject((byte)0);

        Release_1_0_Preview5.ConvertTextureData(tag);

        Assert.Equal((int)TextureFormat.RGBA8, tag["ColorFormats"][0].IntValue);
        Assert.Equal((int)TextureFormat.RGBA16Float, tag["ColorFormats"][1].IntValue);
        Assert.True(tag["Srgb"].BoolValue);
        Assert.True(tag["HasDepthAttachment"].BoolValue);

        var renderTexture = new RenderTexture();
        renderTexture.Deserialize(tag, new SerializationContext());
        Assert.Equal([TextureFormat.RGBA8, TextureFormat.RGBA16Float], renderTexture.TextureFormats);
        Assert.True(renderTexture.Srgb);
        Assert.Equal(64, renderTexture.Width);
    }

    private string WriteTexture(string name, EchoObject settings)
    {
        string path = Path.Combine(_root, "Assets", name);
        File.WriteAllBytes(path, []);
        MetaFileData meta = MetaFile.CreateNew("Prowl.Editor.Importers.TextureImporter");
        meta.Settings = settings;
        MetaFile.Write(path + ".meta", meta);
        return path + ".meta";
    }

    private static string MaterialUsing(params (string Slot, Guid Texture)[] slots)
    {
        EchoObject entries = EchoObject.NewList();
        foreach ((string slot, Guid texture) in slots)
        {
            var entry = EchoObject.NewCompound();
            entry["key"] = new EchoObject(slot);
            var reference = EchoObject.NewCompound();
            reference["$asset"] = new EchoObject(texture.ToString());
            entry["value"] = reference;
            entries.ListAdd(entry);
        }
        var textures = EchoObject.NewCompound();
        textures["entries"] = entries;
        var properties = EchoObject.NewCompound();
        properties["_textures"] = textures;
        var material = EchoObject.NewCompound();
        material["_properties"] = properties;
        return material.WriteToString();
    }

    [Fact]
    public void Project_MetasAndMaterialSlotsConvert()
    {
        string normal = WriteTexture("Bricks.png", OldImportSettings(5, 1, 0));
        string surface = WriteTexture("Panel.png", OldImportSettings(5, 1, 0));
        string both = WriteTexture("Shared.png", OldImportSettings(5, 0, 2));
        string misnamed = WriteTexture("Leaf Mask.png", OldImportSettings(5, 1, 0));
        string albedo = WriteTexture("Albedo.png", OldImportSettings(5, 1, 0));
        Guid Id(string meta) => MetaFile.Read(meta).Guid;

        string material = Path.Combine(_root, "Assets", "Wall.mat");
        File.WriteAllText(material, MaterialUsing(("_NormalTex", Id(normal)), ("_SurfaceTex", Id(surface)),
            ("_OcclusionTex", Id(both)), ("_MainTex", Id(albedo)), ("_EmissionTex", Id(misnamed))));
        MetaFile.Write(material + ".meta", MetaFile.CreateNew("MaterialImporter"));
        File.WriteAllText(Path.Combine(_root, "Assets", "Other.mat"), MaterialUsing(("_MainTex", Id(both))));
        MetaFile.Write(Path.Combine(_root, "Assets", "Other.mat.meta"), MetaFile.CreateNew("MaterialImporter"));

        RunTextureSteps(Project.Open(_root));
        RunTextureSteps(Project.Open(_root));

        EchoObject Settings(string meta) => MetaFile.Read(meta).Settings!;
        Assert.Equal((int)TextureImportType.NormalMap, Settings(normal)["textureType"].IntValue);
        Assert.Equal((int)TextureImportType.Mask, Settings(surface)["textureType"].IntValue);
        Assert.False(Settings(both).TryGet("textureType", out _));
        Assert.False(Settings(albedo).TryGet("textureType", out _));
        Assert.Equal((int)TextureImportType.Default, Settings(misnamed)["textureType"].IntValue);
        Assert.Equal((int)FilterMode.Point, Settings(both)["filter"].IntValue);
        Assert.Equal((int)WrapMode.Clamp, Settings(both)["wrapU"].IntValue);
        Assert.False(TextureImporter.IsSrgb(Settings(normal), normal[..^5]));
        Assert.True(TextureImporter.IsSrgb(Settings(albedo), albedo[..^5]));
    }

    [Theory]
    [InlineData("Bricks Normal.png", TextureImportType.NormalMap, false)]
    [InlineData("rock_nrm.png", TextureImportType.NormalMap, false)]
    [InlineData("Panel Surface.png", TextureImportType.Mask, false)]
    [InlineData("leaf-mask.tga", TextureImportType.Mask, false)]
    [InlineData("Abnormality.png", TextureImportType.Default, true)]
    [InlineData("Bricks.png", TextureImportType.Default, true)]
    public void Importer_TypeFollowsTheNameUntilSet(string file, TextureImportType type, bool srgb)
    {
        Assert.Equal(type, TextureImporter.TypeOf(null, file));
        Assert.Equal(srgb, TextureImporter.IsSrgb(null, file));

        var settings = EchoObject.NewCompound();
        settings["textureType"] = new EchoObject((int)TextureImportType.Sprite);
        Assert.Equal(TextureImportType.Sprite, TextureImporter.TypeOf(settings, file));
        Assert.True(TextureImporter.IsSrgb(settings, file));
    }
}

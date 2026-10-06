// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Buffers.Binary;
using System.Reflection;

using Prowl.Scribe;

namespace Tests;

public class FontCollectionTests
{
    private static byte[] FontBytes(string name = "Geist-Regular.ttf")
    {
        using var stream = typeof(FontCollectionTests).Assembly.GetManifestResourceStream(name)!;
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CollectionFacesMatchStandaloneNamesMetricsOutlinesAndRasterization(bool version2)
    {
        byte[][] originals = [FontBytes(), FontBytes("Geist-Bold.ttf")];
        byte[] data = Collection(version2, originals);
        FontFile[] faces = FontFile.LoadCollection(data);
        Assert.Equal(2, faces.Length);
        Assert.Equal(FontStyle.Regular, faces[0].Style);
        Assert.Equal(FontStyle.Bold, faces[1].Style);
        Assert.Equal(faces[0].Style, new FontFile(data).Style);
        Assert.Equal(faces[1].Style, new FontFile(data, 1).Style);

        for (int i = 0; i < faces.Length; i++)
        {
            var original = new FontFile(originals[i]);
            var face = faces[i];
            Assert.Equal(original.FamilyName, face.FamilyName);
            Assert.Equal(original.UnitsPerEm, face.UnitsPerEm);
            Assert.Equal(original.Ascent, face.Ascent);
            Assert.Equal(original.Descent, face.Descent);
            foreach (char c in "Agé") // Includes a composite outline in the fixture fonts.
            {
                int glyph = original.FindGlyphIndex(c);
                Assert.True(glyph > 0);
                Assert.Equal(glyph, face.FindGlyphIndex(c));
                int advance = 0, bearing = 0, expectedAdvance = 0, expectedBearing = 0;
                original.GetGlyphHorizontalMetrics(glyph, ref expectedAdvance, ref expectedBearing);
                face.GetGlyphHorizontalMetrics(glyph, ref advance, ref bearing);
                Assert.Equal(expectedAdvance, advance);
                Assert.Equal(expectedBearing, bearing);
                Assert.Equal(original.GetGlyphShape(glyph, out var expectedShape), face.GetGlyphShape(glyph, out var shape));
                Assert.NotEmpty(shape);
                Assert.Equal(expectedShape, shape);

                var expectedRenderer = new RecordingRenderer();
                var renderer = new RecordingRenderer();
                var expectedSystem = new FontSystem(expectedRenderer, includeWhiteRect: false);
                var system = new FontSystem(renderer, includeWhiteRect: false);
                Assert.NotNull(expectedSystem.GetOrCreateGlyph(c, original, FontQuality.Normal));
                Assert.NotNull(system.GetOrCreateGlyph(c, face, FontQuality.Normal));
                Assert.NotEmpty(renderer.Pixels);
                Assert.Equal(expectedRenderer.Pixels, renderer.Pixels);
            }
        }
    }

    [Fact]
    public void CollectionFacesShareTheOriginalBackingBuffer()
    {
        byte[] data = Collection(false, FontBytes(), FontBytes());
        FontFile[] faces = FontFile.LoadCollection(data);
        // Inspect ownership without adding a public API that exposes mutable font bytes.
        var field = typeof(FontFile).GetField("data", BindingFlags.Instance | BindingFlags.NonPublic)!;
        foreach (var face in faces)
        {
            object pointer = field.GetValue(face)!;
            var array = pointer.GetType().GetField("_array", BindingFlags.Instance | BindingFlags.NonPublic)!;
            Assert.Same(data, array.GetValue(pointer));
        }
    }

    [Fact]
    public void FileAndStreamOverloadsLoadAllFacesAndHonorTheSelectedIndex()
    {
        byte[] data = Collection(true, FontBytes(), FontBytes("Geist-Bold.ttf"));
        string path = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(path, data);
            Assert.Equal(2, FontFile.LoadCollection(path).Length);
            Assert.Equal(2, FontFile.LoadCollection(new FileInfo(path)).Length);
            Assert.Equal(FontStyle.Bold, new FontFile(path, 1).Style);
            Assert.Equal(FontStyle.Bold, new FontFile(new FileInfo(path), 1).Style);
            Assert.Equal(FontStyle.Regular, new FontFile(path).Style);
            Assert.Equal(FontStyle.Regular, new FontFile(new FileInfo(path)).Style);

            using var stream = new MemoryStream();
            stream.Write(new byte[7]);
            stream.Write(data);
            stream.Position = 7;
            Assert.Equal(FontStyle.Bold, new FontFile(stream, 1).Style);
            stream.Position = 7;
            Assert.Equal(2, FontFile.LoadCollection(stream).Length);
            stream.Position = 7;
            Assert.Equal(FontStyle.Regular, new FontFile(stream).Style);
            Assert.True(stream.CanRead);
            using var unseekable = new UnseekableStream(data);
            Assert.Equal(2, FontFile.LoadCollection(unseekable).Length);
            Assert.True(unseekable.CanRead);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void StandaloneFontsRemainSingleFaceAndRejectOtherIndices()
    {
        byte[] data = FontBytes();
        Assert.Single(FontFile.LoadCollection(data));
        Assert.Throws<ArgumentOutOfRangeException>(() => new FontFile(data, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new FontFile(data, 1));
        byte[] collection = Collection(false, data, data);
        Assert.Throws<ArgumentOutOfRangeException>(() => new FontFile(collection, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new FontFile(collection, 2));
        Assert.Throws<ArgumentNullException>(() => new FontFile((byte[])null!));
        Assert.Throws<ArgumentNullException>(() => FontFile.LoadCollection((Stream)null!));
    }

    [Theory]
    [MemberData(nameof(MalformedCollections))]
    public void MalformedCollectionsFailWithInvalidDataException(byte[] data)
    {
        Assert.Throws<InvalidDataException>(() => new FontFile(data));
        Assert.Throws<InvalidDataException>(() => FontFile.LoadCollection(data));
    }

    public static IEnumerable<object[]> MalformedCollections()
    {
        byte[] good = Collection(false, FontBytes());
        foreach (int length in new[] { 0, 4, 11, 12, 15 })
            yield return [good[..length]];
        foreach (var (offset, value) in new (int, uint)[]
        {
            (4, 0x00030000), // unknown version
            (8, 0), (8, uint.MaxValue), // invalid counts
            (12, uint.MaxValue), (12, (uint)good.Length - 4), // invalid face offsets
            (16, 0), // invalid sfnt signature
            (20, 0xFFFF0000), // truncated table directory
            (36, uint.MaxValue), // table offset outside the file
            (40, uint.MaxValue), // table length overflows a uint when added to its offset
        })
        {
            byte[] bad = (byte[])good.Clone();
            Write32(bad, offset, value);
            yield return [bad];
        }
        // Version 2 must contain the DSIG fields after its face-offset array.
        byte[] truncatedV2 = good[..16];
        Write32(truncatedV2, 4, 0x00020000);
        yield return [truncatedV2];
    }

    [Fact]
    public void DiscoveryIncludesEveryCollectionFaceAndContinuesPastBrokenFonts()
    {
        string root = Path.Combine(Path.GetTempPath(), "scribe-ttc-" + Guid.NewGuid());
        Directory.CreateDirectory(Path.Combine(root, "nested"));
        try
        {
            byte[] regular = FontBytes(), bold = FontBytes("Geist-Bold.ttf");
            File.WriteAllBytes(Path.Combine(root, "standalone.ttf"), regular);
            File.WriteAllBytes(Path.Combine(root, "nested", "faces.TTC"), Collection(false, regular, bold));
            byte[] damaged = Collection(false, regular, bold);
            Write32(damaged, 12, uint.MaxValue); // first face broken, second remains usable
            File.WriteAllBytes(Path.Combine(root, "damaged.ttc"), damaged);
            File.WriteAllBytes(Path.Combine(root, "broken.ttc"), new byte[8]);
            File.WriteAllBytes(Path.Combine(root, "ignored.bin"), regular);

            FontFile[] fonts = FontSystem.EnumerateFonts([root, root, Path.Combine(root, "missing")]).ToArray();
            Assert.Equal(4, fonts.Length);
            Assert.Equal(2, fonts.Count(f => f.Style == FontStyle.Regular));
            Assert.Equal(2, fonts.Count(f => f.Style == FontStyle.Bold));
            Assert.All(fonts, f => Assert.True(f.FindGlyphIndex('A') > 0));
        }
        finally { Directory.Delete(root, true); }
    }

    // Build TTC v1/v2 fixtures from existing repository fonts. All face directories precede the
    // tables, and identical tables are stored once. Absolute offsets exercise real TTC sharing.
    private static byte[] Collection(bool version2, params byte[][] fonts)
    {
        int headerSize = 12 + 4 * fonts.Length + (version2 ? 12 : 0);
        int[] directorySizes = fonts.Select(f => 12 + 16 * Read16(f, 4)).ToArray();
        using var output = new MemoryStream();
        output.SetLength(headerSize + directorySizes.Sum());
        output.Position = output.Length;
        byte[] header = new byte[headerSize];
        Write32(header, 0, 0x74746366);
        Write32(header, 4, version2 ? 0x00020000u : 0x00010000u);
        Write32(header, 8, (uint)fonts.Length);
        var tables = new List<(byte[] Bytes, uint Offset)>();
        int directoryOffset = headerSize;
        for (int face = 0; face < fonts.Length; face++)
        {
            byte[] font = fonts[face];
            byte[] directory = font[..directorySizes[face]];
            Write32(header, 12 + 4 * face, (uint)directoryOffset);
            for (int record = 12; record < directory.Length; record += 16)
            {
                int offset = (int)Read32(font, record + 8), length = (int)Read32(font, record + 12);
                byte[] bytes = font.AsSpan(offset, length).ToArray();
                var shared = tables.FirstOrDefault(t => t.Bytes.AsSpan().SequenceEqual(bytes));
                if (shared.Bytes == null)
                {
                    shared = (bytes, (uint)output.Length);
                    output.Position = output.Length;
                    output.Write(bytes);
                    while (output.Length % 4 != 0) output.WriteByte(0);
                    tables.Add(shared);
                }
                Write32(directory, record + 8, shared.Offset);
            }
            output.Position = directoryOffset;
            output.Write(directory);
            directoryOffset += directory.Length;
        }
        output.Position = 0;
        output.Write(header);
        return output.ToArray();
    }

    private static ushort Read16(byte[] data, int offset) => BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(offset));
    private static uint Read32(byte[] data, int offset) => BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(offset));
    private static void Write32(byte[] data, int offset, uint value) => BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(offset), value);

    private sealed class UnseekableStream(byte[] data) : MemoryStream(data)
    {
        public override bool CanSeek => false;
    }

    private sealed class RecordingRenderer : IFontRenderer
    {
        public byte[] Pixels { get; private set; } = [];
        public object CreateTexture(int width, int height) => new object();
        public void UpdateTextureRegion(object texture, AtlasRect bounds, byte[] data) => Pixels = data.ToArray();
        public void DrawQuads(object texture, ReadOnlySpan<IFontRenderer.Vertex> vertices, ReadOnlySpan<int> indices) { }
    }
}

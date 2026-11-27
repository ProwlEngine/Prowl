// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Buffers.Binary;

using Prowl.Scribe;

namespace Tests;

public class FontCollectionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CollectionFacesUseTheirOwnDirectories(bool version2)
    {
        byte[][] originals = [FontBytes("Geist-Regular.ttf"), FontBytes("Geist-Bold.ttf")];
        byte[] collection = Collection(version2, originals);
        Assert.Equal(FontStyle.Regular, new FontFile(collection).Style);
        var fs = new FontSystem(new TestFontRenderer());
        for (int i = 0; i < originals.Length; i++)
        {
            var expected = new FontFile(originals[i]);
            var face = new FontFile(collection, i);
            Assert.Equal(expected.FamilyName, face.FamilyName);
            Assert.Equal(expected.Style, face.Style);
            int glyph = expected.FindGlyphIndex('A');
            Assert.Equal(glyph, face.FindGlyphIndex('A'));
            expected.GetGlyphShape(glyph, out var expectedShape);
            face.GetGlyphShape(glyph, out var shape);
            Assert.NotEmpty(shape);
            Assert.Equal(expectedShape, shape);
            Assert.True(fs.GetOrCreateGlyph('A', face, FontQuality.Normal).IsInAtlas);
        }
    }

    [Fact]
    public void InvalidCollectionHeadersAndIndicesAreRejected()
    {
        byte[] collection = Collection(false, FontBytes("Geist-Regular.ttf"));
        foreach (int length in new[] { 0, 4, 11, 12, 15 })
            Assert.Throws<InvalidDataException>(() => new FontFile(collection[..length]));
        Assert.Throws<InvalidDataException>(() => new FontFile(collection, -1));
        Assert.Throws<InvalidDataException>(() => new FontFile(collection, 1));
        Write32(collection, 12, uint.MaxValue);
        Assert.Throws<InvalidDataException>(() => new FontFile(collection));
        Write32(collection, 8, uint.MaxValue);
        Assert.Throws<InvalidDataException>(() => new FontFile(collection));
    }

    [Fact]
    public void SystemDiscoveryIncludesYaHeiCollectionFacesWhenInstalled()
    {
        string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "msyh.ttc");
        if (!File.Exists(path)) return;

        var fs = new FontSystem(new TestFontRenderer());
        var fonts = fs.EnumerateSystemFonts().ToArray();
        foreach (string family in new[] { "microsoft yahei", "microsoft yahei ui" })
        {
            var font = Assert.Single(fonts.Where(f => f.FamilyName == family && f.Style == FontStyle.Regular));
            foreach (char c in "项开")
                Assert.True(fs.GetOrCreateGlyph(c, font, FontQuality.Normal).IsInAtlas);
        }
        Assert.True(new FontFile(path).FindGlyphIndex('项') > 0);
    }

    private static byte[] FontBytes(string name)
    {
        using var stream = typeof(FontCollectionTests).Assembly.GetManifestResourceStream(name)!;
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }

    // Relocate existing sample fonts behind a TTC header. Table offsets remain file-relative.
    private static byte[] Collection(bool version2, params byte[][] fonts)
    {
        int headerSize = 12 + 4 * fonts.Length + (version2 ? 12 : 0);
        byte[] data = new byte[headerSize + fonts.Sum(f => (f.Length + 3) & ~3)];
        Write32(data, 0, 0x74746366); // ttcf
        Write32(data, 4, version2 ? 0x00020000u : 0x00010000u);
        Write32(data, 8, (uint)fonts.Length);
        int start = headerSize;
        for (int i = 0; i < fonts.Length; i++)
        {
            byte[] font = fonts[i];
            Write32(data, 12 + 4 * i, (uint)start);
            font.CopyTo(data, start);
            int tables = BinaryPrimitives.ReadUInt16BigEndian(font.AsSpan(4));
            for (int table = 0; table < tables; table++)
            {
                int offsetField = 12 + 16 * table + 8;
                uint offset = BinaryPrimitives.ReadUInt32BigEndian(font.AsSpan(offsetField));
                Write32(data, start + offsetField, (uint)start + offset);
            }
            start += (font.Length + 3) & ~3;
        }
        return data;
    }

    private static void Write32(byte[] data, int offset, uint value)
        => BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(offset), value);
}

# Prowl.Aperture

Image loading for the [Prowl game engine](https://github.com/ProwlEngine/Prowl). One
dependency-free API that identifies and decodes fourteen formats.

```csharp
using Prowl.Aperture;

// Header only. Reads a few kilobytes, never touches pixel data.
ImageInfo info = Image.Identify("photo.png");
Console.WriteLine($"{info.Width}x{info.Height} {info.PixelFormat} {info.Compression}");

// Full decode, with limits.
Image image = Image.Load("photo.png", new DecodeOptions
{
    TargetPixelFormat = PixelFormat.Rgba8,
    MaxPixels = 64_000_000,
});

// Nothing here throws, whatever the bytes are.
if (Image.TryLoad(untrusted, DecodeOptions.Default, out Image? loaded, out ApertureError error))
    Upload(loaded!.RootFrame.Pixels);
```

## Formats

| Format | Notes |
| ------ | ----- |
| BMP | Core through V5, every depth, RLE, arbitrary bitfields, embedded JPEG and PNG |
| DDS | BC1 to BC7, ASTC, the video and float forms, with mip chains, cube faces and volume slices |
| EXR | Scanline, tiled with its levels, multi part, brightness and chroma, all eight compressions |
| GIF | 87a and 89a, animation with disposal, interlacing, transparency |
| HDR | Radiance RGBE and XYZE, both run length encodings, all axis orderings |
| ICO | BMP and PNG entries, every size, cursors, 256 pixel entries, transparency masks |
| JPEG | Baseline, extended, progressive and arithmetic, every subsampling, restarts, CMYK |
| PNG | Every colour type and depth, Adam7, tRNS, chunk CRCs, APNG frames |
| PNM | P1 through P6 and PAM, text and binary, every sample range |
| PSD | PSD and PSB, the flattened image, every depth and colour mode |
| RAW | DNG and anything else stored the way the container describes; vendor streams are refused |
| TGA | Uncompressed and RLE, colour mapped, all depths and origins |
| TIFF | Classic and BigTIFF, multi page, strips and tiles, any sample width, both JPEG forms, fax, log luminance |
| WebP | Lossy, lossless, alpha and animation |

## Pixel data

A decoded frame is a tightly packed buffer with a known stride, and the pixel memory is rented
from a pool that `Image.Dispose` returns it to.

```csharp
using Image image = Image.Load(bytes, new DecodeOptions
{
    TargetPixelFormat = PixelFormat.Rgba8,
    RowAlignment = 256,          // the row pitch a graphics API asked for
});

ImageFrame frame = image.RootFrame;
frame.Pixels;                    // Span<byte> over the whole frame
frame.PixelMemory;               // Memory<byte>, for the APIs that cannot take a span
frame.GetRow(y);                 // one row
frame.GetRowAs<Rgba32>(y);       // the same row as your own pixel struct
frame.CopyTo(mapped, pitch);     // straight into a mapped buffer, re-striding on the way
```

Set `UsePooledMemory` to false when the pixels have to outlive the `Image` they came from.

### Uploading to a texture

The decode is asked for the exact layout and row order the graphics API wants, so the upload is
a pointer and nothing else. No flip pass, no repack, no intermediate array.

```csharp
public static Texture2D FromImage(ReadOnlySpan<byte> encoded, bool generateMipmaps = false)
{
    DecodeOptions options = new()
    {
        TargetPixelFormat = PixelFormat.Rgba8,  // tightly packed R,G,B,A
        FlipVertically = true,                  // OpenGL's origin is the lower left
        RowAlignment = 4,                       // matches the default GL_UNPACK_ALIGNMENT
    };

    using Image image = Image.Load(encoded, options);
    ImageFrame frame = image.RootFrame;

    Texture2D texture = new(image.Width, image.Height, false, TextureImageFormat.Color4b);
    try
    {
        unsafe
        {
            fixed (byte* pixels = frame.Pixels)
                Graphics.TexSubImage2D(texture.Handle, 0, 0, 0, image.Width, image.Height, pixels);
        }

        if (generateMipmaps)
            texture.GenerateMipmaps();

        texture.SetTextureFilters(
            generateMipmaps ? TextureMin.LinearMipmapLinear : TextureMin.Linear, TextureMag.Linear);

        return texture;
    }
    catch
    {
        texture.Dispose();
        throw;
    }
}
```

`FlipVertically` costs nothing: the decoder writes one row at a time and simply picks the
destination row from the other end, where flipping afterwards is a second pass over the image.

`RowAlignment` is `GL_UNPACK_ALIGNMENT`. Four is the driver default and is what the code above
assumes. An RGBA row is always a multiple of four anyway, so it only matters for three channel
uploads of odd width. Going above eight means padding the driver will not infer, so set
`GL_UNPACK_ROW_LENGTH` from `frame.Stride` if you ask for a wider alignment.

The pixel memory is pooled and owned by the `Image`, so the `fixed` block must sit inside the
`using`. `TexSubImage2D` copies synchronously, which is why a pointer is enough here; anything
that reads the buffer later needs `frame.CopyTo` into memory of its own.

### Frames, levels and pages

A file that holds more than one picture hands them over as frames, and which of them come back is
the caller's choice.

```csharp
// Every frame of an animation, every page of a TIFF, every face of a cube map.
using Image animation = Image.Load(bytes, new DecodeOptions { DecodeAllFrames = true });

foreach (ImageFrame frame in animation.Frames)
    Console.WriteLine($"{frame.Width}x{frame.Height} for {frame.Delay}ms");

// The smaller copies a texture file carries, rather than only the largest.
using Image texture = Image.Load(bytes, new DecodeOptions { DecodeMipmaps = true });

foreach (ImageFrame level in texture.Frames)
    Console.WriteLine($"level {level.MipLevel}: {level.Width}x{level.Height}");
```

Block compressed data can also be taken as it lies, for a pipeline that would rather upload the
blocks than a decoded picture:

```csharp
using TextureData data = TextureData.Load("terrain.dds");
Graphics.CompressedTexImage2D(handle, data.Format, data.Width, data.Height, data.GetLevel(0));
```

### Orientation

A camera records which way up it was holding the lens and writes the sensor's own rows. This
library hands back those rows as stored and reports the tag, so the size of the buffer and the
size of the picture are the same thing. Ask for the picture to be turned when that is what you
want:

```csharp
using Image upright = Image.Load(bytes, new DecodeOptions { ApplyExifOrientation = true });
```

It is off by default because a quarter turn trades the width for the height, and a caller who has
already sized a buffer from `Image.Identify` should be the one to ask for that.

## Performance

Measured against ImageSharp and against ImageMagick through Magick.NET, all three asked for the
same eight bit RGBA picture from the same bytes. A ratio above one means Aperture is faster.
These are one desktop, so treat them with a grain of salt

| Case | Pixels | ImageSharp | Magick | Aperture | vs Sharp | vs Magick |
|---|---|---|---|---|---|---|
| PNG palette, 1022x1022 | 1.0 M | 3.7 ms | 9.4 ms | 1.1 ms | **3.35x** | **8.36x** |
| TIFF LZW tiled, 512x384 | 0.20 M | 2.6 ms | 1.10 ms | 1.24 ms | **2.14x** | 0.89x |
| EXR float, 24 bit lossy | 1.0 M | | 23.9 ms | 11.8 ms | | **2.03x** |
| TGA run length, 32 bit | 1.4 M | 7.1 ms | 13.1 ms | 4.2 ms | **1.68x** | **3.11x** |
| PNG photo, 3508x2480 | 8.7 M | 31.8 ms | 79.9 ms | 20.2 ms | **1.57x** | **3.95x** |
| TGA palette | 0.15 M | 1.2 ms | 2.7 ms | 0.76 ms | **1.57x** | **3.34x** |
| EXR half RGB | 0.25 M | | 15.3 ms | 10.5 ms | | **1.46x** |
| PNG art, 1118x1105 | 1.2 M | 2.0 ms | 7.4 ms | 1.4 ms | **1.39x** | **5.28x** |
| JPEG greyscale, 800x800 | 0.64 M | 0.70 ms | 12.8 ms | 0.51 ms | **1.38x** | **25.1x** |
| GIF photo, 800 wide | 0.43 M | 2.0 ms | 8.5 ms | 1.5 ms | **1.33x** | **5.60x** |
| BMP truecolour | 154 k | 0.036 ms | unread | 0.032 ms | **1.29x** | |
| BMP palette | 28 k | 0.010 ms | 0.22 ms | 0.009 ms | **1.17x** | **34.6x** |
| PNG interlaced, 450x332 grey | 0.15 M | 0.36 ms | 2.3 ms | 0.32 ms | **1.15x** | **7.31x** |
| PNM binary, 8 bit | 34 k | 0.011 ms | 0.20 ms | 0.010 ms | **1.07x** | **23.1x** |
| JPEG photo 4:4:4, 1262x860 | 1.1 M | 5.0 ms | 10.5 ms | 4.7 ms | **1.07x** | **2.21x** |
| PNG transparent, 1920x1920 | 3.7 M | 5.8 ms | 32.4 ms | 5.4 ms | **1.07x** | **5.95x** |
| JPEG photo 4:2:0, 1365x2048 | 2.8 M | 7.2 ms | 19.5 ms | 6.8 ms | **1.05x** | **2.86x** |
| TIFF RGB uncompressed | 24 k | 0.010 ms | 0.24 ms | 0.010 ms | **1.01x** | **18.2x** |
| WebP lossless photo | 90 k | 2.5 ms | 2.0 ms | 2.6 ms | 0.95x | 0.77x |
| JPEG progressive, 650x470 | 0.31 M | 3.1 ms | 5.6 ms | 3.4 ms | 0.92x | **1.66x** |
| JPEG photo 4:2:2, 960x720 | 0.69 M | 2.2 ms | 6.9 ms | 2.5 ms | 0.88x | **2.74x** |
| WebP lossless art | 90 k | 2.3 ms | 1.9 ms | 2.7 ms | 0.84x | 0.70x |
| TIFF float, 222 strips | 61 k | 1.9 ms | 4.7 ms | 2.7 ms | 0.70x | **1.77x** |
| PNM binary, 16 bit | 34 k | 0.048 ms | 0.24 ms | 0.08 ms | 0.65x | **3.13x** |
| WebP lossy photo | 0.20 M | 2.4 ms | 2.5 ms | 3.9 ms | 0.63x | 0.64x |
| ICO, 256 pixel entry | 66 k | | 0.78 ms | 0.09 ms | | **9.02x** |
| PSD layered | 66 k | | 1.08 ms | 0.18 ms | | **6.08x** |
| DDS uncompressed | 66 k | | 1.25 ms | 0.27 ms | | **4.56x** |
| Radiance HDR | 11 k | | 0.41 ms | 0.10 ms | | **4.06x** |
| DDS block compressed | 66 k | | unread | 0.23 ms | | |

The harness decodes real corpus files rather than synthetic data, and checks that the three
libraries produced identical pixels before timing them, so a timing is a timing of the same work.
It lives outside this repository with the corpus it reads.

## Testing

Roughly 47,000 test cases run against a 2,300 file corpus of real images. 
Well formed files must decode, deliberately broken ones must
fail with an error rather than an exception, and the rest must reach either outcome without
crashing or hanging.

Expected values do not come from Aperture. Pixels are compared against readings produced by
Pillow, pypng, libjpeg, libwebp, OpenEXR and libraw, none of which share code with this library,
and against reference renderings shipped with the upstream conformance suites.

The corpus runs to hundreds of megabytes of photos and may not be redistributed, so it lives
outside this repository along with the test and benchmark projects that read it.

using Prowl.Clay.Importer;
using Prowl.Clay.Internal.Intermediate;
using Prowl.Clay.Internal.IO;

namespace Prowl.Clay.Formats.Gltf;

/// <summary>
/// Maps glTF <c>textures</c>/<c>images</c>/<c>samplers</c> into <see cref="IntermediateTexture"/> entries.
/// External image files are referenced by resolved path; data-URI and bufferView-backed images
/// are inlined as encoded bytes.
/// </summary>
internal static class GltfTextureMapper
{
    public static void MapAll(GltfDom dom, GltfBufferStore buffers, IntermediateScene scene, ImportContext ctx)
    {
        if (dom.Textures is null)
            return;

        for (int t = 0; t < dom.Textures.Length; t++)
        {
            var tex = dom.Textures[t];
            var inter = new IntermediateTexture
            {
                Name = tex.Name,
                Sampler = MapSampler(dom, tex.Sampler),
            };

            if (tex.Source is { } sourceIndex && dom.Images is { } images && (uint)sourceIndex < (uint)images.Length)
            {
                var image = images[sourceIndex];
                inter.Name ??= image.Name;
                ResolveImage(image, buffers, ctx, inter);
            }
            else
            {
                ctx.Log.Warning($"Texture {t} has no resolvable image source.", "GltfTextureMapper");
            }

            scene.Textures.Add(inter);
        }
    }

    private static void ResolveImage(GltfImage image, GltfBufferStore buffers, ImportContext ctx, IntermediateTexture inter)
    {
        if (image.Uri is string uri)
        {
            if (DataUri.TryDecode(uri, out string mime, out byte[] data))
            {
                inter.EncodedBytes = data;
                inter.MimeType = !string.IsNullOrEmpty(image.MimeType) ? image.MimeType : mime;
                return;
            }

            if (ctx.SourcePath is null)
            {
                ctx.Log.Warning($"Texture image '{uri}' is external but no source path is set; image bytes unavailable.",
                    "GltfTextureMapper");
                return;
            }

            string? resolved = ctx.Resolver.Resolve(ctx.SourcePath, Uri.UnescapeDataString(uri));
            if (resolved is null)
            {
                ctx.Log.Warning($"Could not resolve image '{uri}'.", "GltfTextureMapper");
                return;
            }

            inter.SourcePath = resolved;
            inter.MimeType = image.MimeType ?? GuessMime(resolved);
            return;
        }

        if (image.BufferView is { } viewIndex)
        {
            var view = buffers.GetBufferView(viewIndex);
            inter.EncodedBytes = buffers.GetBufferView(view).ToArray();
            inter.MimeType = image.MimeType;
            return;
        }

        ctx.Log.Warning("Image has neither URI nor bufferView.", "GltfTextureMapper");
    }

    private static IntermediateTextureSampler MapSampler(GltfDom dom, int? samplerIndex)
    {
        var s = new IntermediateTextureSampler();
        if (samplerIndex is not { } idx || dom.Samplers is not { } samplers || (uint)idx >= (uint)samplers.Length)
            return s;

        var gs = samplers[idx];

        s.WrapU = MapWrap(gs.WrapS);
        s.WrapV = MapWrap(gs.WrapT);

        s.MagFilter = gs.MagFilter switch
        {
            GltfSamplerFilter.Nearest => TextureFilterMode.Point,
            _ => TextureFilterMode.Bilinear,
        };

        (s.MinFilter, s.GenerateMipmaps) = gs.MinFilter switch
        {
            GltfSamplerFilter.Nearest => (TextureFilterMode.Point, false),
            GltfSamplerFilter.Linear => (TextureFilterMode.Bilinear, false),
            GltfSamplerFilter.NearestMipmapNearest => (TextureFilterMode.Point, true),
            GltfSamplerFilter.LinearMipmapNearest => (TextureFilterMode.Bilinear, true),
            GltfSamplerFilter.NearestMipmapLinear => (TextureFilterMode.Point, true),
            GltfSamplerFilter.LinearMipmapLinear => (TextureFilterMode.Trilinear, true),
            _ => (TextureFilterMode.Trilinear, true),
        };

        return s;
    }

    private static TextureWrapMode MapWrap(int? wrap) => wrap switch
    {
        GltfSamplerWrap.ClampToEdge => TextureWrapMode.Clamp,
        GltfSamplerWrap.MirroredRepeat => TextureWrapMode.Mirror,
        _ => TextureWrapMode.Repeat,
    };

    private static string? GuessMime(string path) =>
        Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".ktx2" => "image/ktx2",
            ".webp" => "image/webp",
            _ => null,
        };
}

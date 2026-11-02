using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

using ImageMagick;

using Prowl.Clay;
using Prowl.Clay.Importer;
using Prowl.Graphite.Rendering;
using Prowl.Vector;

using ClayMaterial = Prowl.Clay.Material;
using ClayMesh = Prowl.Clay.Mesh;
using ClayTexture = Prowl.Clay.Texture;
using Material = Prowl.Graphite.Rendering.Material;


namespace Prowl.Graphite.Samples.PBRRenderer;


/// <summary>
/// A glTF model loaded through Clay onto Graphite's PBR shader, one mesh per node with its transform baked in, each
/// Clay material mapped to the PBR inputs and its textures uploaded as sRGB for colours and linear for data.
/// </summary>
internal sealed class PbrModel : IDisposable
{
    private readonly List<Texture> _textures = new();
    private readonly Dictionary<(int, bool, float), Texture> _decoded = new();
    private readonly Dictionary<Material, float> _depths = new();
    private readonly Dictionary<(Material, float), Material> _scaled = new();
    private readonly Demo _demo;
    private readonly Model _model;
    private readonly Material _fallback;

    private PbrModel(Demo demo, Model model, float emissionNits)
    {
        _demo = demo;
        _model = model;
        _fallback = demo.Renderer.CreateMaterial(demo.Renderer.Standard.Lit);
        Materials = new Material[model.Materials.Count];
        for (int i = 0; i < Materials.Length; i++)
            Materials[i] = CreateMaterial(demo, model.Materials[i], emissionNits);

        Bounds bounds = Bounds.Empty;
        List<Part> parts = new();
        foreach (ModelNode node in model.Nodes)
        {
            if (node.MeshIndex < 0)
                continue;
            (MeshData data, Material[] materials) = BuildPart(model.Meshes[node.MeshIndex], node.WorldMatrix, ref bounds);
            parts.Add(new Part(demo.Renderer.CreateMesh(data), materials, node.Name));
        }

        Parts = parts.ToArray();
        Bounds = bounds;
    }

    public readonly record struct Part(GpuMesh Mesh, Material[] Materials, string Name);

    public Part[] Parts { get; }

    public Material[] Materials { get; }

    public Bounds Bounds { get; }

    /// <summary>Loads a glTF, with emissive 1 at <paramref name="emissionNits"/> nits before KHR_materials_emissive_strength.</summary>
    public static PbrModel Load(Demo demo, string path, float emissionNits = 1000f)
        => new(demo, ModelImporter.Load(path), emissionNits);

    /// <summary>
    /// Adds every part to the world, the model scaled so its largest side is <paramref name="size"/> and its base centred
    /// on <paramref name="position"/>. Volume attenuation distances scale with it, per added copy.
    /// </summary>
    public RenderableHandle[] AddTo(RenderWorld world, Double3 position, double size, float yaw = 0f)
    {
        float scale = Scale(size);
        Double4x4 transform = Placement(position, size, yaw);
        RenderableHandle[] handles = new RenderableHandle[Parts.Length];
        for (int i = 0; i < Parts.Length; i++)
        {
            Part part = Parts[i];
            RenderableDesc desc = new() { Mesh = part.Mesh, Name = part.Name, Transform = transform, Flags = RenderableFlags.CastShadows | RenderableFlags.ReceiveShadows };
            handles[i] = world.Add(desc, Array.ConvertAll(part.Materials, m => Scaled(m, scale)));
        }
        return handles;
    }

    /// <summary>The transform <see cref="AddTo"/> gives the parts.</summary>
    public Double4x4 Placement(Double3 position, double size, float yaw = 0f)
    {
        double scale = Scale(size);
        Float3 center = Bounds.Center;
        Double3 pivot = new Double3(center.X, Bounds.Min.Y, center.Z) * scale;
        Quaternion rotation = Quaternion.AxisAngle(Float3.UnitY, yaw);
        Double3 offset = position - (Double3)(rotation * (Float3)pivot);
        return Double4x4.CreateTRS(offset, rotation, new Double3(scale, scale, scale));
    }

    /// <summary>The uniform scale that makes the model's largest side <paramref name="size"/>.</summary>
    public float Scale(double size)
    {
        Float3 extents = Bounds.Extents;
        return (float)(size / (MathF.Max(extents.X, MathF.Max(extents.Y, extents.Z)) * 2.0));
    }

    // A child of a volume material with its attenuation distance in world units at this scale
    private Material Scaled(Material material, float scale)
    {
        if (!_depths.TryGetValue(material, out float depth))
            return material;
        if (!_scaled.TryGetValue((material, scale), out Material? child))
        {
            child = material.CreateChild();
            child.SetFloat("transmission_depth", depth * scale);
            _scaled[(material, scale)] = child;
        }
        return child;
    }

    private (MeshData, Material[]) BuildPart(ClayMesh mesh, Float4x4 world, ref Bounds bounds)
    {
        int count = mesh.VertexCount;
        Float3[] positions = new Float3[count];
        Float3[] normals = new Float3[count];
        Float4[] tangents = new Float4[count];
        Float4[] colors = new Float4[count];
        Float3 x = new(world.c0.X, world.c0.Y, world.c0.Z), y = new(world.c1.X, world.c1.Y, world.c1.Z), z = new(world.c2.X, world.c2.Y, world.c2.Z);
        bool mirrored = Float3.Dot(x, Float3.Cross(y, z)) < 0f;
        float sign = mirrored ? -1f : 1f;
        // The cofactor rows, the inverse transpose up to the determinant whose sign is taken back out
        Float3 nx = Float3.Cross(y, z) * sign, ny = Float3.Cross(z, x) * sign, nz = Float3.Cross(x, y) * sign;
        for (int i = 0; i < count; i++)
        {
            positions[i] = Transform(world, mesh.Vertices[i], 1f);
            bounds.Encapsulate(positions[i]);
            normals[i] = mesh.Normals is { } n ? Float3.Normalize(nx * n[i].X + ny * n[i].Y + nz * n[i].Z) : Float3.UnitY;
            Float4 t = mesh.Tangents is { } ts ? ts[i] : new Float4(1, 0, 0, 1);
            Float3 direction = Float3.Normalize(Transform(world, new Float3(t.X, t.Y, t.Z), 0f));
            tangents[i] = new Float4(direction.X, direction.Y, direction.Z, t.W * sign);
            colors[i] = mesh.Colors is { } c ? new Float4(c[i].R, c[i].G, c[i].B, c[i].A) : Float4.One;
        }

        Float2[] uv0 = mesh.UVs[0] ?? new Float2[count];
        Float2[] uv1 = mesh.UVs[1] ?? uv0;

        List<uint> indices = new(mesh.Indices.Length);
        List<Prowl.Graphite.Rendering.SubMesh> subMeshes = new();
        List<Material> materials = new();
        foreach (Prowl.Clay.SubMesh sub in mesh.SubMeshes)
        {
            subMeshes.Add(new Prowl.Graphite.Rendering.SubMesh { IndexStart = (uint)indices.Count, IndexCount = (uint)sub.IndexCount, Topology = PrimitiveTopology.TriangleList });
            for (int k = 0; k < sub.IndexCount; k++)
            {
                // Mirrored nodes swap each triangle's last two corners to keep it front facing
                int corner = k % 3;
                int source = mirrored && corner > 0 ? k + 3 - 2 * corner : k;
                indices.Add((uint)(mesh.Indices[sub.IndexStart + source] + sub.BaseVertex));
            }
            materials.Add(sub.MaterialIndex >= 0 ? Materials[sub.MaterialIndex] : _fallback);
        }

        MeshData data = new()
        {
            VertexCount = (uint)count,
            Streams =
            [
                Stream(VertexSemantic.Position, VertexElementFormat.Float3, positions),
                Stream(VertexSemantic.Normal, VertexElementFormat.Float3, normals),
                Stream(VertexSemantic.Tangent, VertexElementFormat.Float4, tangents),
                Stream(VertexSemantic.Color, VertexElementFormat.Float4, colors),
                Stream(VertexSemantic.UV0, VertexElementFormat.Float2, uv0),
                Stream(VertexSemantic.UV1, VertexElementFormat.Float2, uv1),
            ],
            Indices = indices.ToArray(),
            SubMeshes = subMeshes.ToArray(),
        };
        return (data, materials.ToArray());
    }

    // The glTF metal rough model and its KHR material extensions on the PBR inputs, lengths in the model's own units
    private Material CreateMaterial(Demo demo, ClayMaterial src, float emissionNits)
    {
        Material m = demo.Renderer.CreateMaterial(demo.Renderer.Standard.Lit);
        m.Name = src.Name;

        m.SetColor("base_color", Rgb(src.BaseColor));
        Map(m, "base_color_map", src.BaseColorTexture, srgb: true);
        m.SetFloat("base_metalness", src.Metallic);
        m.SetFloat("specular_roughness", src.Roughness);
        Map(m, "base_metalness_map", src.MetallicRoughnessTexture, channel: 2);
        Map(m, "specular_roughness_map", src.MetallicRoughnessTexture, channel: 1);
        Map(m, "geometry_normal_map", src.NormalTexture);
        m.SetFloat("normal_scale", src.NormalScale);
        if (src.OcclusionTexture != null && src.OcclusionStrength > 0f)
            Map(m, "ambient_occlusion_map", src.OcclusionTexture, channel: 0, occlusionStrength: src.OcclusionStrength);

        Float4 emissive = Rgb(src.EmissiveFactor);
        if (emissive.X + emissive.Y + emissive.Z > 0f)
        {
            m.SetColor("emission_color", emissive);
            m.SetFloat("emission_luminance", emissionNits * src.EmissiveStrength);
            Map(m, "emission_color_map", src.EmissiveTexture, srgb: true);
        }

        if (src.Ior is { } ior)
            m.SetFloat("specular_ior", ior.Ior);

        if (src.Specular is { } specular)
        {
            m.SetFloat("specular_weight", specular.Factor);
            m.SetColor("specular_color", Rgb(specular.ColorFactor));
            Map(m, "specular_weight_map", specular.FactorTexture, channel: 3);
            Map(m, "specular_color_map", specular.ColorTexture, srgb: true);
        }

        if (src.Clearcoat is { Factor: > 0f } coat)
        {
            m.SetFloat("coat_weight", coat.Factor);
            m.SetFloat("coat_roughness", coat.Roughness);
            m.SetFloat("coat_ior", 1.5f);
            Map(m, "coat_weight_map", coat.FactorTexture, channel: 0);
            Map(m, "coat_roughness_map", coat.RoughnessTexture, channel: 1);
            Map(m, "geometry_coat_normal_map", coat.NormalTexture);
            m.SetFloat("coat_normal_scale", coat.NormalScale);
        }

        if (src.Sheen is { } sheen && Peak(sheen.ColorFactor) is float peak and > 0f)
        {
            // glTF dims the base by the sheen colour's peak and the fuzz by its weight, so the peak moves into the weight
            m.SetFloat("fuzz_weight", peak);
            m.SetColor("fuzz_color", new Float4(sheen.ColorFactor.R / peak, sheen.ColorFactor.G / peak, sheen.ColorFactor.B / peak, 1f));
            m.SetFloat("fuzz_roughness", sheen.RoughnessFactor);
            Map(m, "fuzz_color_map", sheen.ColorTexture, srgb: true);
            Map(m, "fuzz_roughness_map", sheen.RoughnessTexture, channel: 3);
        }

        if (src.Iridescence is { Factor: > 0f } film)
        {
            m.SetFloat("thin_film_weight", film.Factor);
            m.SetFloat("thin_film_ior", film.Ior);
            Map(m, "thin_film_weight_map", film.FactorTexture, channel: 0);
            // The PBR thickness is a factor times its map, which only matches glTF's range when it starts at zero
            if (film.ThicknessTexture != null && film.ThicknessMinimum <= 0f)
            {
                m.SetFloat("thin_film_thickness", film.ThicknessMaximum / 1000f);
                Map(m, "thin_film_thickness_map", film.ThicknessTexture, channel: 1);
            }
            else
            {
                m.SetFloat("thin_film_thickness", (film.ThicknessTexture != null ? (film.ThicknessMinimum + film.ThicknessMaximum) * 0.5f : film.ThicknessMaximum) / 1000f);
            }
        }

        if (src.Anisotropy is { Strength: > 0f } anisotropy)
        {
            m.SetFloat("specular_roughness_anisotropy", anisotropy.Strength);
            m.SetFloat("tangent_rotation", anisotropy.Rotation);
            Map(m, "specular_roughness_anisotropy_map", anisotropy.Texture, channel: 2);
            Map(m, "geometry_tangent_map", anisotropy.Texture);
        }

        bool thinWalled = false;
        if (src.Transmission is { Factor: > 0f } transmission)
        {
            m.SetFloat("transmission_weight", transmission.Factor);
            Map(m, "transmission_weight_map", transmission.FactorTexture, channel: 0);
            m.SetColor("transmission_color", Rgb(src.BaseColor));
            Map(m, "transmission_color_map", src.BaseColorTexture, srgb: true);
            if (src.Volume is { ThicknessFactor: > 0f } volume)
            {
                if (float.IsFinite(volume.AttenuationDistance))
                {
                    // A volume tints by attenuation alone, so glTF's base colour tint joins the attenuation colour
                    m.SetColor("transmission_color", new Float4(src.BaseColor.R * volume.AttenuationColor.R, src.BaseColor.G * volume.AttenuationColor.G, src.BaseColor.B * volume.AttenuationColor.B, 1f));
                    m.SetFloat("transmission_depth", volume.AttenuationDistance);
                    _depths[m] = volume.AttenuationDistance;
                }
                if (src.Dispersion is { Dispersion: > 0f } dispersion)
                {
                    m.SetFloat("transmission_dispersion_scale", 1f);
                    m.SetFloat("transmission_dispersion_abbe_number", dispersion.AbbeNumber);
                }
            }
            else
            {
                thinWalled = true;
            }
        }

        if (src.DiffuseTransmission is { Factor: > 0f } diffuse)
        {
            m.SetFloat("subsurface_weight", diffuse.Factor);
            m.SetColor("subsurface_color", Rgb(diffuse.ColorFactor));
            Map(m, "subsurface_weight_map", diffuse.FactorTexture, channel: 3);
            Map(m, "subsurface_color_map", diffuse.ColorTexture, srgb: true);
            // Double sided sheets pass all of it forward as glTF does, closed single sided meshes keep the volumetric wrap
            if (src.DoubleSided)
            {
                m.SetFloat("subsurface_scatter_anisotropy", 1f);
                thinWalled = true;
            }
        }

        if (thinWalled)
            m.SetInt("geometry_thin_walled", 1);

        SurfaceType type = src.AlphaMode switch
        {
            MaterialAlphaMode.Mask => SurfaceType.Cutout,
            MaterialAlphaMode.Blend => SurfaceType.Transparent,
            _ => SurfaceType.Opaque,
        };
        if (type != SurfaceType.Opaque)
        {
            m.SetFloat("geometry_opacity", src.BaseColor.A);
            Map(m, "geometry_opacity_map", src.BaseColorTexture, channel: 3, srgb: true);
            m.SetFloat("alpha_cutoff", src.AlphaCutoff);
        }
        m.Surface = new SurfaceSettings(type, SurfaceBlend.Premultiplied, src.DoubleSided ? SurfaceCull.Off : SurfaceCull.Back);
        return m;
    }

    private void Map(Material m, string name, MaterialTextureSlot? slot, int channel = 0, bool srgb = false, float occlusionStrength = 1f)
    {
        if (slot == null)
            return;
        m.SetTexture(name, Decode(slot.TextureIndex, srgb, occlusionStrength), SamplerDescription.Aniso4x);
        m.SetTextureChannel(name, channel);
        m.SetTextureUVSet(name, Math.Min(slot.UVChannel, 1));
        if (slot.Scale != Float2.One || slot.Offset != Float2.Zero)
            m.SetTextureScaleOffset(name, new Float4(slot.Scale.X, slot.Scale.Y, slot.Offset.X, slot.Offset.Y));
        if (slot.Rotation != 0f)
            m.SetTextureRotation(name, slot.Rotation);
    }

    // sRGB textures keep alpha linear, so one upload serves a colour and its alpha. An occlusion strength below 1 bakes
    // glTF's 1 + strength (R - 1) into the red channel, which the PBR occlusion factor times its map cannot express.
    private Texture Decode(int index, bool srgb, float occlusionStrength)
    {
        if (_decoded.TryGetValue((index, srgb, occlusionStrength), out Texture? existing))
            return existing;

        ClayTexture source = _model.Textures[index];
        byte[] bytes = source.EncodedBytes ?? System.IO.File.ReadAllBytes(source.SourcePath ?? throw new InvalidOperationException($"Texture {index} has no source"));
        using MagickImage image = new(bytes);
        image.Alpha(AlphaOption.Set);
        image.Depth = 8;
        using IUnsafePixelCollection<ushort> pixels = image.GetPixelsUnsafe();
        byte[] rgba = pixels.ToByteArray(PixelMapping.RGBA) ?? throw new InvalidOperationException($"Texture {index} did not decode");
        if (occlusionStrength != 1f)
        {
            for (int i = 0; i < rgba.Length; i += 4)
                rgba[i] = (byte)MathF.Round(255f - occlusionStrength * (255 - rgba[i]));
        }

        uint width = image.Width, height = image.Height;
        uint mips = 1 + (uint)Math.Floor(Math.Log2(Math.Max(width, height)));
        PixelFormat format = srgb ? PixelFormat.R8_G8_B8_A8_UNorm_SRgb : PixelFormat.R8_G8_B8_A8_UNorm;
        GraphicsDevice device = _demo.Device;
        Texture texture = device.ResourceFactory.CreateTexture(TextureDescription.Texture2D(width, height, mips, 1, format, TextureUsage.Sampled));
        device.UpdateTexture<byte>(texture, rgba);
        device.Record(cmd => cmd.GenerateMipmaps(texture)).Wait();

        _textures.Add(texture);
        _decoded[(index, srgb, occlusionStrength)] = texture;
        return texture;
    }

    private static Float4 Rgb(Color c) => new(c.R, c.G, c.B, 1f);

    private static float Peak(Color c) => MathF.Max(c.R, MathF.Max(c.G, c.B));

    private static Float3 Transform(Float4x4 m, Float3 v, float w) => new(
        m.c0.X * v.X + m.c1.X * v.Y + m.c2.X * v.Z + m.c3.X * w,
        m.c0.Y * v.X + m.c1.Y * v.Y + m.c2.Y * v.Z + m.c3.Y * w,
        m.c0.Z * v.X + m.c1.Z * v.Y + m.c2.Z * v.Z + m.c3.Z * w);

    private static VertexStream Stream<T>(VertexSemantic semantic, VertexElementFormat format, T[] data) where T : unmanaged => new()
    {
        Semantic = semantic,
        Format = format,
        Data = MemoryMarshal.AsBytes<T>(data).ToArray(),
    };

    public void Dispose()
    {
        foreach (Part part in Parts)
            part.Mesh.Dispose();
        foreach (Material material in _scaled.Values)
            material.Dispose();
        foreach (Material material in Materials)
            material.Dispose();
        _fallback.Dispose();
        foreach (Texture texture in _textures)
            texture.Dispose();
    }
}

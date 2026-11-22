// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Prowl.Clay;
using Prowl.Clay.Formats.Gltf;
using Prowl.Editor.Importers;
using Prowl.Runtime;
using Prowl.Runtime.Rendering;
using Prowl.Runtime.Resources;
using Prowl.Vector;

using ClayMaterialAlphaMode = Prowl.Clay.MaterialAlphaMode;
using Mesh = Prowl.Runtime.Resources.Mesh;
using Material = Prowl.Runtime.Resources.Material;
using BlendShape = Prowl.Runtime.Resources.BlendShape;
using BlendShapeFrame = Prowl.Runtime.Resources.BlendShapeFrame;

namespace Prowl.Editor.Modeling;

/// <summary>
/// Writes a model document as binary glTF: each object a node with its mesh built as the import builds it, modifiers
/// applied, its bones as joint nodes under it, its blend shapes as morph targets, and its materials as metallic
/// roughness materials with their textures as PNG images.
/// </summary>
public static class ModelExport
{
    /// <summary>Writes the document to a stream, materials found by their asset id and textures turned into PNG bytes.</summary>
    public static void WriteGlb(PMeshDocument document, Stream stream, Func<Guid, Material?> materials, Func<Texture2D, byte[]?> png)
        => GltfWriter.WriteGlb(Build(document, materials, png), stream);

    /// <summary>The document as Clay's export model, in the space and conventions Clay's importer gives models.</summary>
    public static ExportModel Build(PMeshDocument document, Func<Guid, Material?> materials, Func<Texture2D, byte[]?> png)
    {
        var model = new ExportModel();
        var nodeOf = new Dictionary<Guid, int>();
        foreach (PMeshObject item in document.Objects)
        {
            nodeOf[item.Id] = model.Nodes.Count;
            model.Nodes.Add(new ExportNode { Name = item.Name, Position = item.Position, Rotation = item.Rotation, Scale = item.Scale });
        }

        var materialOf = new Dictionary<Guid, int>();
        var textureOf = new Dictionary<Texture2D, int>();
        foreach (PMeshObject item in document.Objects)
        {
            ExportNode node = model.Nodes[nodeOf[item.Id]];
            node.Parent = nodeOf.TryGetValue(item.Parent, out int parent) ? parent : -1;

            using Mesh mesh = PMeshImporter.BuildMesh(item);
            node.Mesh = model.Meshes.Count;
            model.Meshes.Add(MeshOf(mesh, item, slot => MaterialIndex(model, item, slot, materials, png, materialOf, textureOf)));

            if (item.Bones.Count == 0) continue;
            var skin = new ExportSkin { Name = item.Name };
            int first = model.Nodes.Count;
            for (int i = 0; i < item.Bones.Count; i++)
            {
                PMeshBone bone = item.Bones[i];
                model.Nodes.Add(new ExportNode
                {
                    Name = bone.Name,
                    Parent = bone.Parent >= 0 && bone.Parent < item.Bones.Count ? first + bone.Parent : nodeOf[item.Id],
                    Position = bone.Position,
                    Rotation = bone.Rotation,
                    Scale = bone.Scale,
                });
                skin.Joints.Add(first + i);
                skin.InverseBindMatrices.Add(bone.BindPose);
            }
            node.Skin = model.Skins.Count;
            model.Skins.Add(skin);
        }
        return model;
    }

    /// <summary>
    /// A built mesh as an export mesh. Prowl flips texture V on importing glTF, and the bitangent with it, so both are
    /// flipped back here.
    /// </summary>
    private static ExportMesh MeshOf(Mesh mesh, PMeshObject item, Func<int, int> material)
    {
        var export = new ExportMesh
        {
            Name = item.Name,
            Positions = mesh.Vertices,
            Normals = mesh.HasNormals ? mesh.Normals : null,
            Tangents = mesh.HasTangents ? [.. mesh.Tangents.Select(t => new Float4(t.X, t.Y, t.Z, -t.W))] : null,
            UV0 = mesh.HasUV ? [.. mesh.UV.Select(Flip)] : null,
            UV1 = mesh.HasUV2 ? [.. mesh.UV2.Select(Flip)] : null,
            Colors = mesh.HasColors ? [.. mesh.Colors.Select(c => new Float4(c.R, c.G, c.B, c.A))] : null,
        };
        if (mesh.HasBoneIndices && mesh.HasBoneWeights)
        {
            export.Joints = [.. mesh.BoneIndices.Select(i => new Int4((int)i.X, (int)i.Y, (int)i.Z, (int)i.W))];
            export.Weights = mesh.BoneWeights;
        }

        uint[] indices = mesh.Indices;
        for (int s = 0; s < mesh.SubMeshCount; s++)
        {
            SubMeshDescriptor sub = mesh.GetSubMesh(s);
            if (sub.IndexCount == 0) continue;
            export.Primitives.Add(new ExportPrimitive { Indices = indices[sub.IndexStart..(sub.IndexStart + sub.IndexCount)], Material = material(s) });
        }

        BlendShape[] shapes = mesh.BlendShapes;
        for (int s = 0; s < shapes.Length; s++)
        {
            // glTF keeps one frame a target, the one reached at full weight
            if (shapes[s].Frames.Length == 0) continue;
            BlendShapeFrame frame = shapes[s].Frames[^1];
            export.MorphTargets.Add(new ExportMorphTarget
            {
                Name = shapes[s].Name,
                DeltaPositions = frame.DeltaVertices,
                DeltaNormals = frame.DeltaNormals,
                DeltaTangents = frame.DeltaTangents,
                Weight = s < item.Shapes.Count ? item.Shapes[s].Weight / 100f : 0f,
            });
        }
        return export;
    }

    private static Float2 Flip(Float2 uv) => new(uv.X, 1f - uv.Y);

    /// <summary>
    /// The metalness and roughness maps as glTF keeps them, roughness in green and metalness in blue of one image. A
    /// map already packed that way is used as it is, others are packed into a new image, a missing one reading full.
    /// </summary>
    private static int MetallicRoughness(ExportModel model, Material material, Texture2D? metalness, Texture2D? roughness, Func<Texture2D, byte[]?> png,
                                         Dictionary<Texture2D, int> textureOf)
    {
        bool hasMetal = metalness.IsValid(), hasRough = roughness.IsValid();
        if (!hasMetal && !hasRough) return -1;
        int metalChannel = hasMetal ? material.GetTextureChannel("base_metalness_map") : 0;
        int roughChannel = hasRough ? material.GetTextureChannel("specular_roughness_map") : 0;
        if (hasMetal && ReferenceEquals(metalness, roughness) && metalChannel == 2 && roughChannel == 1)
        {
            if (textureOf.TryGetValue(metalness!, out int found)) return found;
            if (png(metalness!) is not { } bytes) return -1;
            textureOf[metalness!] = model.Textures.Count;
            model.Textures.Add(new ExportTexture { Name = metalness!.Name, Png = bytes });
            return model.Textures.Count - 1;
        }

        Aperture.Image? Decode(Texture2D? texture) => texture.IsValid() && png(texture!) is { } bytes
            ? Aperture.Image.Load(bytes, new Aperture.DecodeOptions { TargetPixelFormat = Aperture.PixelFormat.Rgba8 })
            : null;
        using Aperture.Image? metal = Decode(metalness), rough = Decode(roughness);
        if (metal == null && rough == null) return -1;
        int width = Math.Max(metal?.Info.Width ?? 1, rough?.Info.Width ?? 1), height = Math.Max(metal?.Info.Height ?? 1, rough?.Info.Height ?? 1);
        static byte Sample(Aperture.Image? image, int x, int y, int width, int height, int channel)
        {
            if (image == null) return 255;
            int sx = x * image.Info.Width / width, sy = y * image.Info.Height / height;
            return image.Pixels[(sy * image.Info.Width + sx) * 4 + channel];
        }
        var pixels = new byte[width * height * 4];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int at = (y * width + x) * 4;
                pixels[at] = 255;
                pixels[at + 1] = Sample(rough, x, y, width, height, roughChannel);
                pixels[at + 2] = Sample(metal, x, y, width, height, metalChannel);
                pixels[at + 3] = 255;
            }
        using Aperture.Image packed = Aperture.Image.FromPixels(pixels, width, height, Aperture.PixelFormat.Rgba8);
        model.Textures.Add(new ExportTexture { Name = material.Name + " Metal Rough", Png = packed.Encode(new Aperture.EncodeOptions { Format = Aperture.ImageFormat.Png }) });
        return model.Textures.Count - 1;
    }

    private static int MaterialIndex(ExportModel model, PMeshObject item, int slot, Func<Guid, Material?> materials, Func<Texture2D, byte[]?> png,
                                     Dictionary<Guid, int> materialOf, Dictionary<Texture2D, int> textureOf)
    {
        Guid id = slot < item.Materials.Count ? item.Materials[slot] : Guid.Empty;
        if (materialOf.TryGetValue(id, out int index)) return index;
        index = model.Materials.Count;
        materialOf[id] = index;
        model.Materials.Add(id != Guid.Empty && materials(id) is { } material
            ? MaterialOf(model, material, png, textureOf)
            : new ExportMaterial { Name = "Default" });
        return index;
    }

    /// <summary>A Prowl lit material as a metallic roughness one, the reverse of how the model importer reads glTF materials.</summary>
    private static ExportMaterial MaterialOf(ExportModel model, Material material, Func<Texture2D, byte[]?> png, Dictionary<Texture2D, int> textureOf)
    {
        int Texture(string name)
        {
            if (material.GetTexture(name) is not { } texture || texture.IsNotValid()) return -1;
            if (textureOf.TryGetValue(texture, out int found)) return found;
            if (png(texture) is not { } bytes) return -1;
            textureOf[texture] = model.Textures.Count;
            model.Textures.Add(new ExportTexture { Name = texture.Name, Png = bytes });
            return model.Textures.Count - 1;
        }

        Color baseColor = material.GetColor("base_color");
        Color emission = material.GetColor("emission_color");
        Float4 scaleOffset = material.GetTextureScaleOffset("base_color_map");
        SurfaceSettings? surface = material.Surface;
        Texture2D? metalness = material.GetTexture("base_metalness_map"), roughness = material.GetTexture("specular_roughness_map");
        return new ExportMaterial
        {
            Name = material.Name,
            BaseColor = new Float4(baseColor.R, baseColor.G, baseColor.B, baseColor.A),
            Metallic = material.GetFloat("base_metalness"),
            Roughness = material.GetFloat("specular_roughness"),
            BaseColorTexture = Texture("base_color_map"),
            MetallicRoughnessTexture = MetallicRoughness(model, material, metalness, roughness, png, textureOf),
            NormalTexture = Texture("geometry_normal_map"),
            NormalScale = material.GetFloat("normal_scale"),
            Emission = new Float3(emission.R, emission.G, emission.B),
            EmissionStrength = material.GetFloat("emission_luminance"),
            EmissionTexture = Texture("emission_color_map"),
            TextureScale = new Float2(scaleOffset.X, scaleOffset.Y),
            TextureOffset = new Float2(scaleOffset.Z, scaleOffset.W),
            AlphaMode = surface is { Type: SurfaceType.Transparent } ? ClayMaterialAlphaMode.Blend
                : material._properties.HasFloat("alpha_cutoff") ? ClayMaterialAlphaMode.Mask : ClayMaterialAlphaMode.Opaque,
            AlphaCutoff = material.GetFloat("alpha_cutoff"),
            DoubleSided = surface is { Cull: SurfaceCull.Off },
        };
    }
}

using Prowl.Clay.Importer;
using Prowl.Clay.Internal.Intermediate;

namespace Prowl.Clay.Formats.Fbx;

/// <summary>
/// Autodesk FBX importer (binary, FBX 2014+). Walks the file tree, builds the object/connection
/// graph, then maps geometry/materials/textures/nodes onto our intermediate scene.
/// </summary>
/// <remarks>
/// Phase 6 covers static meshes, materials, textures, and the full FBX node-transform pipeline
/// (translation/rotation pivots, pre/post rotation, geometric transform, axis + unit conversion
/// from GlobalSettings). Skinning, blend shapes, and animation curves arrive in Phase 7.
/// ASCII FBX is detected and rejected with a clear error; the binary form covers 99%+ of files
/// in the wild.
/// </remarks>
internal sealed class FbxFormat : IModelFormat
{
    public string Token => "fbx";

    public bool CanRead(string formatToken) => formatToken == "fbx";

    public IntermediateScene Read(Stream stream, ImportContext context)
    {
        byte[] bytes;
        if (stream is MemoryStream ms && ms.TryGetBuffer(out ArraySegment<byte> seg) && seg.Offset == 0 && seg.Count == ms.Length)
        {
            bytes = seg.Array!;
            if (bytes.Length != seg.Count)
            {
                // Defensive copy when the underlying buffer is larger than the stream's logical length.
                bytes = new byte[seg.Count];
                Buffer.BlockCopy(seg.Array!, 0, bytes, 0, seg.Count);
            }
        }
        else
        {
            using var copy = new MemoryStream();
            stream.CopyTo(copy);
            bytes = copy.ToArray();
        }

        FbxNode root;
        uint fbxVersion;
        if (FbxAsciiReader.LooksLikeAscii(bytes))
        {
            // ASCII path: the ASCII reader produces an FbxNode tree shaped identically to what
            // FbxBinaryReader emits, plus a FBXVersion lifted from FBXHeaderExtension.
            var asciiReader = FbxAsciiReader.Create(bytes);
            root = asciiReader.ReadRoot();
            fbxVersion = asciiReader.Version;
        }
        else
        {
            if (!FbxBinaryReader.TryCreate(bytes, out FbxBinaryReader? binReader, out string error))
                throw new ImportException($"Failed to open FBX file: {error}", context.SourcePath, context.Format);
            root = binReader!.ReadRoot();
            fbxVersion = binReader.Version;
        }

        // FBX 6.x (versions 6000-6999) uses a completely different document layout: name-based
        // connections, no 64-bit object IDs, separate Properties60 schema. We require 7100+
        // (FBX 2011); pre-2011 files need to be re-exported from the source DCC.
        if (fbxVersion < 7100)
        {
            throw new ImportException(
                $"FBX version {fbxVersion} (pre-FBX 2011) is a legacy document format that is not supported. " +
                "Re-export this model as FBX 2013 (7.3) or newer from your DCC (Blender / Maya / Max / etc.).",
                context.SourcePath, context.Format);
        }
        if (fbxVersion > 7900)
        {
            context.Log.Warning(
                $"FBX version {fbxVersion} is newer than tested (7100-7700); the file may load with quirks.",
                "FbxFormat");
        }

        string? creator = root.FindChild("FBXHeaderExtension")?.FindChild("Creator")?.StringAt(0);
        var doc = new FbxDocument(root, fbxVersion, creator);

        // GlobalSettings axis + unit conversion: FBX scenes declare their up axis (1=Y, 2=Z),
        // front axis, coord axis, and a UnitScaleFactor. UnitScaleFactor is applied later by
        // GlobalScaleStep, not here, so we don't double-bake it. Common cases:
        //   - UpAxis=1 (Y), default FBX  -> already RH-Y-up
        //   - UpAxis=2 (Z), Max-style    -> RH-Z-up (Y and Z axes swap to reach RH-Y-up basis)
        double unitScale = doc.GlobalSettings.GetDoubleOr("UnitScaleFactor", 1d);
        double unitToMeters = unitScale * 0.01; // cm -> m
        int upAxis = doc.GlobalSettings.GetIntOr("UpAxis", 1);
        int upAxisSign = doc.GlobalSettings.GetIntOr("UpAxisSign", 1);
        int frontAxis = doc.GlobalSettings.GetIntOr("FrontAxis", 2);
        int frontAxisSign = doc.GlobalSettings.GetIntOr("FrontAxisSign", 1);
        int coordAxis = doc.GlobalSettings.GetIntOr("CoordAxis", 0);
        int coordAxisSign = doc.GlobalSettings.GetIntOr("CoordAxisSign", 1);

        CoordinateSystem sourceCoord;
        if (upAxis == 1 && upAxisSign == 1 && frontAxis == 2 && frontAxisSign == 1 && coordAxis == 0 && coordAxisSign == 1)
            sourceCoord = CoordinateSystem.RightHandedYUp;
        else if (upAxis == 2 && upAxisSign == 1 && frontAxis == 1 && frontAxisSign == 1 && coordAxis == 0 && coordAxisSign == 1)
            sourceCoord = CoordinateSystem.RightHandedZUp;
        else
        {
            // Uncommon sign-flipped basis combinations are treated as Y-up with a warning; we
            // only handle the two configurations that actually appear in real-world files.
            context.Log.Warning(
                $"FBX axes (Up={upAxis}/{upAxisSign}, Front={frontAxis}/{frontAxisSign}, Coord={coordAxis}/{coordAxisSign}) are non-standard; treating as RH Y-up.",
                "FbxFormat");
            sourceCoord = CoordinateSystem.RightHandedYUp;
        }

        var scene = new IntermediateScene
        {
            Format = context.Format,
            FormatVersion = fbxVersion.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Generator = creator,
            SourceCoordinateSystem = sourceCoord,
            SourceUnitToMeters = (float)unitToMeters,
        };

        // 1. Textures - first so material wiring can resolve them.
        var textureMapping = FbxTextureMapper.MapAll(doc, scene, context);
        // 2. Materials.
        var materialMapping = FbxMaterialMapper.MapAll(doc, scene, textureMapping, context);
        // 3. Mesh geometry (un-skinned for now).
        var meshMapping = FbxMeshMapper.MapAll(doc, scene, context);
        // 4. Model nodes + parenting + mesh attachment + transform composition.
        var modelMapping = FbxModelMapper.Map(doc, meshMapping, materialMapping, scene, context);
        scene.Root = modelMapping.Root;

        // 5. Deformers (skinning + blend shapes) + animations. These all rely on the FBX-vertex
        //    -> intermediate-vertex expansion table that FbxMeshMapper built.
        FbxSkinMapper.MapAll(doc, meshMapping, modelMapping, scene, context);
        var blendShapeChannels = FbxBlendShapeMapper.MapAll(doc, meshMapping, scene, context);
        FbxAnimationMapper.MapAll(doc, modelMapping, scene, context, blendShapeChannels);

        return scene;
    }
}

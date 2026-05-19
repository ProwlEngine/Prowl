using Prowl.Clay.Importer;
using Prowl.Clay.Internal.Intermediate;
using Prowl.Clay.PostProcess;
using Prowl.Vector;

namespace Prowl.Clay.Formats.Fbx;

/// <summary>
/// Builds the <see cref="IntermediateNode"/> hierarchy from FBX <c>Model</c> objects, including
/// the full FBX transform pipeline (translation + rotation pivots, offsets, pre-/post-rotation,
/// rotation order, geometric transform).
/// </summary>
/// <remarks>
/// FBX's "Transform" on a Model is the product of nine 4x4 matrices applied in this order:
/// <code>
///   World = Parent * T * Roff * Rp * Rpre * R * Rpost^-1 * Rp^-1 * Soff * Sp * S * Sp^-1
/// </code>
/// We collapse that into a single local TRS by composing the whole chain and decomposing the
/// resulting matrix. Geometric translation/rotation/scale (a per-node "object-space" offset that
/// FBX applies between the node transform and the geometry, NOT inherited by children) is baked
/// directly into the geometry vertices because our node has a single TRS, not a separate slot
/// for it.
/// </remarks>
internal static class FbxModelMapper
{
    public sealed class ModelMapping
    {
        public required IntermediateNode Root { get; init; }
        public required Dictionary<long, IntermediateNode> NodesByFbxId { get; init; }
        /// <summary>
        /// FBX nodes carry separate PreRotation / PostRotation chains that are baked into the
        /// node's <see cref="IntermediateNode.LocalRotation"/> at bind time. Lcl Rotation
        /// animation curves only animate the middle "R" slot of the chain, so any animation
        /// sample that overwrites LocalRotation must compose <c>Pre * R * inverse(Post)</c> to
        /// stay consistent with the baked bind pose. This map carries those (pre, post) Quats so
        /// <see cref="FbxAnimationMapper"/> can apply them.
        /// </summary>
        public required Dictionary<long, (Prowl.Vector.Quaternion Pre, Prowl.Vector.Quaternion Post)> RotationOffsets { get; init; }
    }

    public static ModelMapping Map(
        FbxDocument doc,
        FbxMeshMapper.MeshMapping meshMapping,
        FbxMaterialMapper.MaterialMapping materialMapping,
        IntermediateScene scene,
        ImportContext ctx)
    {
        // 1. Build a flat list of every Model object (excluding RootNode itself which is the
        // virtual scene root referenced by id 0).
        var nodesByFbxId = new Dictionary<long, IntermediateNode>();
        var rotationOffsets = new Dictionary<long, (Prowl.Vector.Quaternion Pre, Prowl.Vector.Quaternion Post)>();
        var modelObjects = new List<FbxObject>();
        foreach (var obj in doc.Objects.Values)
        {
            if (obj.ObjectType != "Model") continue;
            modelObjects.Add(obj);
            var n = new IntermediateNode
            {
                Name = string.IsNullOrEmpty(obj.Name) ? $"Model_{obj.Id}" : obj.Name,
            };
            var (pre, post) = ComposeTRSFromProperties(obj.Properties, n);
            nodesByFbxId[obj.Id] = n;
            rotationOffsets[obj.Id] = (pre, post);
        }

        // 2. Parent each node via OO connections (Model -> Model destination).
        foreach (var src in modelObjects)
        {
            if (!doc.ConnectionsBySource.TryGetValue(src.Id, out var conns)) continue;
            foreach (var c in conns)
            {
                if (c.Type != "OO") continue;
                if (!doc.Objects.TryGetValue(c.Destination, out var dst)) continue;
                if (dst.ObjectType != "Model") continue;
                var childNode = nodesByFbxId[src.Id];
                var parentNode = nodesByFbxId[dst.Id];
                childNode.Parent = parentNode;
                parentNode.Children.Add(childNode);
            }
        }

        // 3. Wire mesh + skin references onto each Model node.
        foreach (var modelObj in modelObjects)
        {
            var node = nodesByFbxId[modelObj.Id];
            AttachMeshes(modelObj, node, doc, meshMapping, materialMapping, scene, ctx);
        }

        // 4. Build the scene root: a synthetic root with the orphans (models that had no parent)
        // as its children. FBX's RootNode id 0 isn't typically materialized as an object.
        var root = new IntermediateNode { Name = "<RootNode>" };
        foreach (var node in nodesByFbxId.Values)
        {
            if (node.Parent is null)
            {
                node.Parent = root;
                root.Children.Add(node);
            }
        }

        scene.Nodes.Clear();
        AppendDepthFirst(root, scene.Nodes);

        return new ModelMapping
        {
            Root = root,
            NodesByFbxId = nodesByFbxId,
            RotationOffsets = rotationOffsets,
        };
    }

    private static void AttachMeshes(
        FbxObject modelObj, IntermediateNode node, FbxDocument doc,
        FbxMeshMapper.MeshMapping meshMapping,
        FbxMaterialMapper.MaterialMapping materialMapping,
        IntermediateScene scene, ImportContext ctx)
    {
        // Materials are connected directly from Material -> Model (OO). Collect them in order.
        var materialSlot = new List<int>();
        if (doc.ConnectionsByDestination.TryGetValue(modelObj.Id, out var conns))
        {
            foreach (var c in conns)
            {
                if (c.Type != "OO") continue;
                if (!doc.Objects.TryGetValue(c.Source, out var srcObj)) continue;
                if (srcObj.ObjectType != "Material") continue;
                materialSlot.Add(materialMapping.MaterialIndex.GetValueOrDefault(srcObj.Id, -1));
            }
        }

        // Find every Geometry attached to this Model. For each, look up the IntermediateMeshes
        // we already produced (one per material slot).
        bool firstMeshOnNode = true;
        if (doc.ConnectionsByDestination.TryGetValue(modelObj.Id, out var conns2))
        {
            foreach (var c in conns2)
            {
                if (c.Type != "OO") continue;
                if (!doc.Objects.TryGetValue(c.Source, out var srcObj)) continue;
                if (srcObj.ObjectType != "Geometry" || srcObj.Subtype != "Mesh") continue;
                if (!meshMapping.GeometryToMeshes.TryGetValue(srcObj.Id, out var range)) continue;
                if (range.MeshCount == 0) continue;

                // Bake geometric transform onto the geometry's vertices (if any).
                BakeGeometricTransformIfNeeded(modelObj, range, scene);

                // Resolve per-mesh material from the per-slot table.
                for (int i = 0; i < range.MeshCount; i++)
                {
                    int meshIdx = range.FirstMeshIndex + i;
                    int slot = meshMapping.MaterialSlotPerMesh[meshIdx];
                    scene.Meshes[meshIdx].MaterialIndex = slot < materialSlot.Count ? materialSlot[slot] : -1;
                }

                if (firstMeshOnNode)
                {
                    node.MeshIndex = range.FirstMeshIndex;
                    firstMeshOnNode = false;
                    for (int i = 1; i < range.MeshCount; i++)
                    {
                        var sub = new IntermediateNode
                        {
                            Name = $"{node.Name}_slot{i}",
                            Parent = node,
                            MeshIndex = range.FirstMeshIndex + i,
                        };
                        node.Children.Add(sub);
                    }
                }
                else
                {
                    for (int i = 0; i < range.MeshCount; i++)
                    {
                        var sub = new IntermediateNode
                        {
                            Name = $"{node.Name}_geom{srcObj.Id}_slot{i}",
                            Parent = node,
                            MeshIndex = range.FirstMeshIndex + i,
                        };
                        node.Children.Add(sub);
                    }
                }
            }
        }
        _ = ctx;
    }

    /// <summary>
    /// Composes the nine-component FBX transform from the property table, decomposes it into TRS,
    /// and assigns to <paramref name="node"/>. Returns the pre- and post-rotation quaternions so
    /// downstream animation can re-apply them at every sample point (Lcl Rotation curves only
    /// drive the middle "R" of the chain, not the surrounding Pre/Post).
    /// </summary>
    private static (Quaternion Pre, Quaternion Post) ComposeTRSFromProperties(FbxPropertyTable p, IntermediateNode node)
    {
        ReadVec3(p, "Lcl Translation", out var lclT, Float3.Zero);
        ReadVec3(p, "Lcl Rotation", out var lclR, Float3.Zero);
        ReadVec3(p, "Lcl Scaling", out var lclS, Float3.One);

        ReadVec3(p, "RotationOffset", out var rotOff, Float3.Zero);
        ReadVec3(p, "RotationPivot", out var rotPiv, Float3.Zero);
        ReadVec3(p, "ScalingOffset", out var scaleOff, Float3.Zero);
        ReadVec3(p, "ScalingPivot", out var scalePiv, Float3.Zero);
        ReadVec3(p, "PreRotation", out var preRot, Float3.Zero);
        ReadVec3(p, "PostRotation", out var postRot, Float3.Zero);

        int rotOrder = p.GetIntOr("RotationOrder", 0); // 0 = XYZ Euler (default)

        // Compose: T * Roff * Rp * Rpre * R * Rpost^-1 * Rp^-1 * Soff * Sp * S * Sp^-1.
        // Build the rotation matrices as quaternions for stability.
        Quaternion R = EulerToQuat(lclR, rotOrder);
        Quaternion Rpre = EulerToQuat(preRot, 0);    // pre/post are always XYZ regardless of RotationOrder
        Quaternion Rpost = EulerToQuat(postRot, 0);

        Float4x4 m = Float4x4.Identity;
        m = SceneBakerHelpers.Mul(m, Translation(lclT));
        m = SceneBakerHelpers.Mul(m, Translation(rotOff));
        m = SceneBakerHelpers.Mul(m, Translation(rotPiv));
        m = SceneBakerHelpers.Mul(m, RotationMatrix(Rpre));
        m = SceneBakerHelpers.Mul(m, RotationMatrix(R));
        m = SceneBakerHelpers.Mul(m, RotationMatrix(InverseUnit(Rpost)));
        m = SceneBakerHelpers.Mul(m, Translation(Neg(rotPiv)));
        m = SceneBakerHelpers.Mul(m, Translation(scaleOff));
        m = SceneBakerHelpers.Mul(m, Translation(scalePiv));
        m = SceneBakerHelpers.Mul(m, ScaleMatrix(lclS));
        m = SceneBakerHelpers.Mul(m, Translation(Neg(scalePiv)));

        SceneBakerHelpers.DecomposeMatrix(m, out var t, out var rDecomposed, out var s);
        node.LocalPosition = t;
        node.LocalRotation = rDecomposed;
        node.LocalScale = s;
        return (Rpre, Rpost);
    }

    /// <summary>
    /// Bakes the model's geometric transform (which doesn't inherit to children) directly into
    /// the geometry's vertex positions. Only applied once per geometry by sentinel check.
    /// </summary>
    private static void BakeGeometricTransformIfNeeded(
        FbxObject modelObj, FbxMeshMapper.GeometryMapping range, IntermediateScene scene)
    {
        var p = modelObj.Properties;
        bool hasT = p.TryGetVec3("GeometricTranslation", out var gtX, out var gtY, out var gtZ);
        bool hasR = p.TryGetVec3("GeometricRotation", out var grX, out var grY, out var grZ);
        bool hasS = p.TryGetVec3("GeometricScaling", out var gsX, out var gsY, out var gsZ);
        if (!hasT && !hasR && !hasS) return;

        Float3 gt = new((float)gtX, (float)gtY, (float)gtZ);
        Float3 gr = new((float)grX, (float)grY, (float)grZ);
        Float3 gs = hasS ? new Float3((float)gsX, (float)gsY, (float)gsZ) : Float3.One;
        if (gt == Float3.Zero && gr == Float3.Zero && gs == Float3.One) return;

        var geomMatrix = SceneBakerHelpers.ComposeTRS(gt, EulerToQuat(gr, 0), gs);

        for (int m = range.FirstMeshIndex; m < range.FirstMeshIndex + range.MeshCount; m++)
        {
            var mesh = scene.Meshes[m];
            for (int i = 0; i < mesh.Positions.Count; i++)
            {
                var v = mesh.Positions[i];
                var v4 = SceneBakerHelpers.MulColumn(geomMatrix, new Float4(v.X, v.Y, v.Z, 1f));
                mesh.Positions[i] = new Float3(v4.X, v4.Y, v4.Z);
            }
            // Normals + tangents would also need the geometric rotation applied. For Phase 6
            // we leave the source layered data alone - JoinIdenticalVertices doesn't run before
            // this point, and normals/tangents are typically re-derived at the runtime layer.
        }
    }

    private static void ReadVec3(FbxPropertyTable p, string name, out Float3 dst, Float3 fallback)
    {
        if (p.TryGetVec3(name, out double x, out double y, out double z))
            dst = new Float3((float)x, (float)y, (float)z);
        else
            dst = fallback;
    }

    private static Quaternion EulerToQuat(Float3 eulerDegrees, int rotationOrder)
    {
        float rx = eulerDegrees.X * MathF.PI / 180f;
        float ry = eulerDegrees.Y * MathF.PI / 180f;
        float rz = eulerDegrees.Z * MathF.PI / 180f;
        Quaternion qx = AxisAngle(new Float3(1f, 0f, 0f), rx);
        Quaternion qy = AxisAngle(new Float3(0f, 1f, 0f), ry);
        Quaternion qz = AxisAngle(new Float3(0f, 0f, 1f), rz);

        // FBX rotation orders: 0=XYZ, 1=XZY, 2=YZX, 3=YXZ, 4=ZXY, 5=ZYX, 6=SphericXYZ (rare, skip).
        return rotationOrder switch
        {
            0 => MulQuat(MulQuat(qz, qy), qx), // R = Rz * Ry * Rx (Euler XYZ extrinsic)
            1 => MulQuat(MulQuat(qy, qz), qx),
            2 => MulQuat(MulQuat(qx, qz), qy),
            3 => MulQuat(MulQuat(qz, qx), qy),
            4 => MulQuat(MulQuat(qy, qx), qz),
            5 => MulQuat(MulQuat(qx, qy), qz),
            _ => MulQuat(MulQuat(qz, qy), qx),
        };
    }

    private static Quaternion AxisAngle(Float3 axis, float radians)
    {
        float half = radians * 0.5f;
        float s = MathF.Sin(half);
        return new Quaternion(axis.X * s, axis.Y * s, axis.Z * s, MathF.Cos(half));
    }

    private static Quaternion MulQuat(Quaternion a, Quaternion b) => new(
        a.W * b.X + a.X * b.W + a.Y * b.Z - a.Z * b.Y,
        a.W * b.Y - a.X * b.Z + a.Y * b.W + a.Z * b.X,
        a.W * b.Z + a.X * b.Y - a.Y * b.X + a.Z * b.W,
        a.W * b.W - a.X * b.X - a.Y * b.Y - a.Z * b.Z);

    private static Quaternion InverseUnit(Quaternion q) => new(-q.X, -q.Y, -q.Z, q.W);

    private static Float3 Neg(Float3 v) => new(-v.X, -v.Y, -v.Z);

    private static Float4x4 Translation(Float3 t) => new(
        new Float4(1f, 0f, 0f, 0f),
        new Float4(0f, 1f, 0f, 0f),
        new Float4(0f, 0f, 1f, 0f),
        new Float4(t.X, t.Y, t.Z, 1f));

    private static Float4x4 ScaleMatrix(Float3 s) => new(
        new Float4(s.X, 0f, 0f, 0f),
        new Float4(0f, s.Y, 0f, 0f),
        new Float4(0f, 0f, s.Z, 0f),
        new Float4(0f, 0f, 0f, 1f));

    private static Float4x4 RotationMatrix(Quaternion q) =>
        SceneBakerHelpers.ComposeTRS(Float3.Zero, q, Float3.One);

    private static void AppendDepthFirst(IntermediateNode node, List<IntermediateNode> list)
    {
        list.Add(node);
        foreach (var c in node.Children)
            AppendDepthFirst(c, list);
    }
}

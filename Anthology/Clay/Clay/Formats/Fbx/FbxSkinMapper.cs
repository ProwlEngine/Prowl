using Prowl.Clay.Importer;
using Prowl.Clay.Internal.Intermediate;
using Prowl.Vector;

namespace Prowl.Clay.Formats.Fbx;

/// <summary>
/// Reads FBX <c>Deformer::Skin</c> + <c>Deformer::Cluster</c> objects and produces
/// <see cref="IntermediateSkin"/>s plus per-vertex influence arrays on the affected
/// <see cref="IntermediateMesh"/>es.
/// </summary>
/// <remarks>
/// FBX stores skinning "by cluster": each cluster references one bone node and a sparse list of
/// (FBX-vertex-index, weight) pairs. We invert this into the per-vertex form the intermediate
/// scene wants, scattering through the FBX -&gt; intermediate vertex expansion table that
/// <see cref="FbxMeshMapper"/> built during geometry unpack.
/// <para>
/// Bind matrices: each cluster stores <c>Transform</c> (mesh-space at bind time, identity for the
/// vast majority of files) and <c>TransformLink</c> (the bone's world matrix at bind time). The
/// inverse-bind matrix is <c>inverse(TransformLink) * Transform</c>, mapping a vertex from mesh
/// space into the bone's local space at bind.
/// </para>
/// </remarks>
internal static class FbxSkinMapper
{
    public sealed class SkinMapping
    {
        /// <summary>Maps an FBX Model id (the mesh-bearing Model) to the IntermediateSkin index.</summary>
        public Dictionary<long, int> ModelToSkinIndex { get; } = new();
    }

    public static SkinMapping MapAll(
        FbxDocument doc,
        FbxMeshMapper.MeshMapping meshMapping,
        FbxModelMapper.ModelMapping modelMapping,
        IntermediateScene scene,
        ImportContext ctx)
    {
        var result = new SkinMapping();

        // Accumulated node -> bind world matrix. Used after every skin is processed to override
        // each node's LocalPosition/Rotation/Scale so the static hierarchy matches the bind pose.
        // FBX exporters often save the file with the skeleton in some non-bind pose (e.g. paused
        // on frame 0 of an animation), which causes catastrophic skinning errors when the
        // inverse-bind matrices (also baked in the file) don't match the runtime bone transforms.
        //
        // Two complementary sources feed this dictionary:
        //   1. Pose::BindPose objects, which list a matrix for every node participating in the
        //      bind (mesh + every bone, including non-clustered intermediates).
        //   2. Each cluster's TransformLink, which is the bone's bind world matrix per skin.
        //
        // We prefer cluster.TransformLink for clustered bones (because that's what the file's
        // baked inverse-bind matrix was derived from, so it's always self-consistent) and fall
        // back to Pose::BindPose for intermediate / leaf bones that no cluster references but
        // that still need to sit at the correct bind pose so the skeleton looks right.
        var boneBindWorld = new Dictionary<IntermediateNode, Float4x4>();
        IngestBindPoseFromPoseObjects(doc, modelMapping, boneBindWorld);

        foreach (var skin in doc.Objects.Values)
        {
            if (skin.ObjectType != "Deformer" || skin.Subtype != "Skin") continue;

            // The Skin attaches to a Geometry via OO (Skin -> Geometry).
            FbxObject? geometry = null;
            if (doc.ConnectionsBySource.TryGetValue(skin.Id, out var skinOut))
            {
                foreach (var c in skinOut)
                {
                    if (c.Type != "OO") continue;
                    if (!doc.Objects.TryGetValue(c.Destination, out var dst)) continue;
                    if (dst.ObjectType == "Geometry" && dst.Subtype == "Mesh")
                    {
                        geometry = dst;
                        break;
                    }
                }
            }
            if (geometry is null)
            {
                ctx.Log.Warning($"Skin {skin.Name} is not connected to any Geometry; ignored.", "FbxSkinMapper");
                continue;
            }

            if (!meshMapping.GeometryToMeshes.TryGetValue(geometry.Id, out var geoMapping))
                continue;

            // Resolve the mesh's bind-pose world matrix. The FBX spec says ibm = inv(TransformLink)
            // * cluster.Transform, where cluster.Transform is the mesh's world matrix at bind. BUT
            // many real-world files (Maya / Sketchfab exports of Mixamo content for example) have
            // a stale cluster.Transform that doesn't match where the mesh actually was at bind —
            // typically because the mesh was moved (or zeroed) after the skin was bound, and the
            // exporter didn't re-bake the cluster matrices. In those files, vertex data is in the
            // post-move space and cluster.Transform reflects the pre-move pivot, so the standard
            // formula double-applies the offset and the rendered mesh slides off the bones.
            //
            // The Pose::BindPose object (when present) lists the mesh's authoritative bind-pose
            // world matrix and is updated by the exporter alongside vertex moves, so we prefer it.
            // Falls back to cluster.Transform when there's no Pose::BindPose entry for the mesh.
            Float4x4? meshBindWorldOverride = null;
            foreach (var meshModelObj in doc.GetDestinationObjects(geometry.Id, "Model"))
            {
                if (!modelMapping.NodesByFbxId.TryGetValue(meshModelObj.Id, out var meshNode)) continue;
                if (boneBindWorld.TryGetValue(meshNode, out var mbw))
                {
                    meshBindWorldOverride = mbw;
                    break;
                }
            }

            // Gather every cluster connected to this skin (Cluster -> Skin OO).
            var clusters = new List<FbxObject>();
            if (doc.ConnectionsByDestination.TryGetValue(skin.Id, out var clusterIn))
            {
                foreach (var c in clusterIn)
                {
                    if (c.Type != "OO") continue;
                    if (!doc.Objects.TryGetValue(c.Source, out var src)) continue;
                    if (src.ObjectType == "Deformer" && src.Subtype == "Cluster")
                        clusters.Add(src);
                }
            }
            if (clusters.Count == 0)
                continue;

            // Build the IntermediateSkin with one bone per cluster.
            var iSkin = new IntermediateSkin { Name = skin.Name };
            int boneCount = clusters.Count;

            // Pre-size the per-vertex influence buffers on every affected mesh. We pick an
            // initial influences-per-vertex of 8 as headroom; the LimitBoneWeights step caps to
            // 4 later. If a vertex ends up needing more we grow on demand.
            int initialInfluences = 8;
            for (int m = geoMapping.FirstMeshIndex; m < geoMapping.FirstMeshIndex + geoMapping.MeshCount; m++)
            {
                var mesh = scene.Meshes[m];
                int vc = mesh.Positions.Count;
                mesh.VertexJoints = new int[vc * initialInfluences];
                mesh.VertexWeights = new float[vc * initialInfluences];
                mesh.MaxInfluencesPerVertex = initialInfluences;
            }
            // Per-vertex usage cursor (how many slots filled so far) for each mesh.
            var cursorPerMesh = new Dictionary<int, int[]>(geoMapping.MeshCount);
            for (int m = geoMapping.FirstMeshIndex; m < geoMapping.FirstMeshIndex + geoMapping.MeshCount; m++)
                cursorPerMesh[m] = new int[scene.Meshes[m].Positions.Count];

            // Walk clusters in order, assigning a bone index per cluster.
            for (int bi = 0; bi < clusters.Count; bi++)
            {
                var cluster = clusters[bi];

                // Find the bone Model via OO (Model -> Cluster).
                IntermediateNode? boneNode = null;
                if (doc.ConnectionsByDestination.TryGetValue(cluster.Id, out var clusterDestIn))
                {
                    foreach (var c in clusterDestIn)
                    {
                        if (c.Type != "OO") continue;
                        if (!doc.Objects.TryGetValue(c.Source, out var src)) continue;
                        if (src.ObjectType != "Model") continue;
                        if (modelMapping.NodesByFbxId.TryGetValue(src.Id, out boneNode))
                            break;
                    }
                }
                if (boneNode is null)
                {
                    ctx.Log.Warning($"Cluster '{cluster.Name}' has no bound Model; using identity bone.", "FbxSkinMapper");
                    boneNode = new IntermediateNode { Name = $"<missing_bone_{cluster.Id}>" };
                }

                // Read TransformLink (bone bind-pose world) and Transform (mesh bind-pose world).
                Float4x4 transformLink = ReadMatrix(cluster.Node, "TransformLink") ?? Float4x4.Identity;
                Float4x4 transform = meshBindWorldOverride ?? ReadMatrix(cluster.Node, "Transform") ?? Float4x4.Identity;
                Float4x4 invBind = Multiply(Inverse4x4(transformLink), transform);

                iSkin.BoneNodes.Add(boneNode);
                iSkin.InverseBindPoses.Add(invBind);

                // Capture the bone's bind-time world matrix so the post-pass can override the
                // node's LocalRotation/Position/Scale to the actual bind pose. (FBX-saved Lcl
                // T/R/S may be a non-bind frame; clusters always reference the bind state.)
                boneBindWorld[boneNode] = transformLink;

                // Read sparse weights: Indexes (per-FBX-vertex indices) + Weights (per-vertex weights).
                int[] indexes = cluster.Node.FindChild("Indexes")?.Properties.ElementAtOrDefault(0)?.AsIntArray()
                                 ?? Array.Empty<int>();
                double[] weights = cluster.Node.FindChild("Weights")?.Properties.ElementAtOrDefault(0)?.AsDoubleArray()
                                 ?? Array.Empty<double>();

                if (indexes.Length != weights.Length)
                {
                    ctx.Log.Warning(
                        $"Cluster '{cluster.Name}': Indexes/Weights length mismatch ({indexes.Length} vs {weights.Length}); skipping.",
                        "FbxSkinMapper");
                    continue;
                }

                for (int i = 0; i < indexes.Length; i++)
                {
                    int fbxV = indexes[i];
                    float w = (float)weights[i];
                    if (w == 0f) continue;
                    if ((uint)fbxV >= (uint)(geoMapping.Starts.Length - 1)) continue;

                    int start = geoMapping.Starts[fbxV];
                    int end = geoMapping.Starts[fbxV + 1];
                    for (int e = start; e < end; e++)
                    {
                        int meshIdx = geoMapping.MeshIndices[e];
                        int vIdx = geoMapping.VertexIndices[e];
                        var mesh = scene.Meshes[meshIdx];
                        int slot = cursorPerMesh[meshIdx][vIdx]++;

                        // Grow if necessary: extend per-vertex influence buffers.
                        if (slot >= mesh.MaxInfluencesPerVertex)
                            GrowInfluences(mesh, mesh.MaxInfluencesPerVertex + 4);

                        int basev = vIdx * mesh.MaxInfluencesPerVertex + slot;
                        mesh.VertexJoints![basev] = bi;
                        mesh.VertexWeights![basev] = w;
                    }
                }
            }

            // Finalize: trim trailing unused slots back down (so JoinIdenticalVertices's hash
            // doesn't include garbage joint indices for unused slots).
            // We don't strictly need to compact here; LimitBoneWeights re-normalizes to a fixed N.

            int skinIndex = scene.Skins.Count;
            scene.Skins.Add(iSkin);

            // Wire SkinIndex onto every Model node that renders this mesh.
            foreach (var node in scene.Nodes)
            {
                if (node.MeshIndex < geoMapping.FirstMeshIndex) continue;
                if (node.MeshIndex >= geoMapping.FirstMeshIndex + geoMapping.MeshCount) continue;
                node.SkinIndex = skinIndex;
            }

            // Record the model -> skin mapping for downstream wiring.
            foreach (var modelObj in doc.GetSourceObjects(geometry.Id, "Model"))
                result.ModelToSkinIndex[modelObj.Id] = skinIndex;
        }

        // Override every clustered bone's LocalPosition/Rotation/Scale so the static hierarchy
        // matches the bind pose. Walk in scene.Nodes order (depth-first / topological), so when
        // we compute a bone's local from inv(parent.bindWorld) * boneBindWorld, the parent has
        // already been overridden when applicable.
        OverrideBindPoseFromCluster(scene, boneBindWorld);

        return result;
    }

    /// <summary>
    /// Pre-populates <paramref name="boneBindWorld"/> from any <c>Pose::BindPose</c> objects in
    /// the document. A Pose::BindPose lists a world matrix for every node that participated in
    /// the bind (the mesh + every bone, including intermediate / leaf bones that no cluster
    /// references but that still need to sit at the correct bind pose so the skeleton looks
    /// right). Cluster.TransformLink takes precedence for clustered bones and overwrites these
    /// entries later in <see cref="MapAll"/>.
    /// </summary>
    private static void IngestBindPoseFromPoseObjects(
        FbxDocument doc,
        FbxModelMapper.ModelMapping modelMapping,
        Dictionary<IntermediateNode, Float4x4> boneBindWorld)
    {
        foreach (var pose in doc.Objects.Values)
        {
            if (pose.ObjectType != "Pose") continue;
            if (!string.Equals(pose.Subtype, "BindPose", StringComparison.OrdinalIgnoreCase)) continue;

            foreach (var poseNode in pose.Node.FindChildren("PoseNode"))
            {
                long modelId = poseNode.FindChild("Node")?.LongAt(0) ?? 0;
                if (modelId == 0) continue;
                if (!modelMapping.NodesByFbxId.TryGetValue(modelId, out var node)) continue;

                var matrixChild = poseNode.FindChild("Matrix");
                if (matrixChild is null || matrixChild.Properties.Count == 0) continue;
                double[] flat = matrixChild.Properties[0].AsDoubleArray();
                if (flat.Length != 16) continue;

                var bindWorld = new Float4x4(
                    new Float4((float)flat[0],  (float)flat[1],  (float)flat[2],  (float)flat[3]),
                    new Float4((float)flat[4],  (float)flat[5],  (float)flat[6],  (float)flat[7]),
                    new Float4((float)flat[8],  (float)flat[9],  (float)flat[10], (float)flat[11]),
                    new Float4((float)flat[12], (float)flat[13], (float)flat[14], (float)flat[15]));

                // First-write-wins: if the same node appears in multiple Pose::BindPose objects
                // we trust the first. Cluster.TransformLink (filled later) takes precedence over
                // any entry we record here for the clustered subset of nodes.
                boneBindWorld.TryAdd(node, bindWorld);
            }
        }
    }

    /// <summary>
    /// For every node with a recorded bind world matrix, derive its LocalPosition/Rotation/Scale
    /// from that bind world matrix, composing inv(parent's bind world) * bind world to recover
    /// the local TRS. Falls back to the parent's currently-baked world transform when the parent
    /// isn't recorded (typical for the synthetic scene root).
    /// </summary>
    private static void OverrideBindPoseFromCluster(IntermediateScene scene, Dictionary<IntermediateNode, Float4x4> boneBindWorld)
    {
        if (boneBindWorld.Count == 0) return;

        // Resolved world-at-bind per node, accumulated as we walk top-down.
        var resolvedWorld = new Dictionary<IntermediateNode, Float4x4>(boneBindWorld.Count);

        foreach (var node in scene.Nodes)
        {
            // Compute this node's parent's world-at-bind.
            Float4x4 parentWorld = Float4x4.Identity;
            if (node.Parent is { } parent && parent.Parent is not null) // skip the synthetic root
            {
                if (!resolvedWorld.TryGetValue(parent, out parentWorld))
                {
                    // Parent isn't a clustered bone; use its current TRS-composed world by
                    // walking up the chain. Fall back to identity for the synthetic root.
                    parentWorld = ComputeCurrentWorldFromTRS(parent);
                    resolvedWorld[parent] = parentWorld;
                }
            }

            // If this node is clustered, override its local TRS from the bind world.
            if (boneBindWorld.TryGetValue(node, out var bindWorld))
            {
                Float4x4 boneLocal = Multiply(Inverse4x4(parentWorld), bindWorld);
                Prowl.Clay.PostProcess.SceneBakerHelpers.DecomposeMatrix(boneLocal, out var t, out var r, out var s);
                node.LocalPosition = t;
                node.LocalRotation = r;
                node.LocalScale = s;
                resolvedWorld[node] = bindWorld;
            }
            else if (node.Parent is not null)
            {
                // Non-clustered node: keep its current TRS but record its world for descendants.
                var local = Prowl.Clay.PostProcess.SceneBakerHelpers.ComposeTRS(node.LocalPosition, node.LocalRotation, node.LocalScale);
                resolvedWorld[node] = Multiply(parentWorld, local);
            }
        }
    }

    private static Float4x4 ComputeCurrentWorldFromTRS(IntermediateNode node)
    {
        Float4x4 local = Prowl.Clay.PostProcess.SceneBakerHelpers.ComposeTRS(node.LocalPosition, node.LocalRotation, node.LocalScale);
        if (node.Parent is null) return local;
        return Multiply(ComputeCurrentWorldFromTRS(node.Parent), local);
    }

    private static Float4x4? ReadMatrix(FbxNode node, string childName)
    {
        var child = node.FindChild(childName);
        if (child is null || child.Properties.Count == 0) return null;
        double[] flat = child.Properties[0].AsDoubleArray();
        if (flat.Length != 16) return null;
        return new Float4x4(
            new Float4((float)flat[0],  (float)flat[1],  (float)flat[2],  (float)flat[3]),
            new Float4((float)flat[4],  (float)flat[5],  (float)flat[6],  (float)flat[7]),
            new Float4((float)flat[8],  (float)flat[9],  (float)flat[10], (float)flat[11]),
            new Float4((float)flat[12], (float)flat[13], (float)flat[14], (float)flat[15]));
    }

    private static void GrowInfluences(IntermediateMesh mesh, int newInfluences)
    {
        int vc = mesh.Positions.Count;
        int oldInf = mesh.MaxInfluencesPerVertex;
        if (newInfluences <= oldInf) return;

        int[] oldJ = mesh.VertexJoints!;
        float[] oldW = mesh.VertexWeights!;
        int[] newJ = new int[vc * newInfluences];
        float[] newW = new float[vc * newInfluences];
        for (int v = 0; v < vc; v++)
        {
            for (int k = 0; k < oldInf; k++)
            {
                newJ[v * newInfluences + k] = oldJ[v * oldInf + k];
                newW[v * newInfluences + k] = oldW[v * oldInf + k];
            }
        }
        mesh.VertexJoints = newJ;
        mesh.VertexWeights = newW;
        mesh.MaxInfluencesPerVertex = newInfluences;
    }

    private static Float4x4 Multiply(Float4x4 a, Float4x4 b) =>
        Prowl.Clay.PostProcess.SceneBakerHelpers.Mul(a, b);

    private static Float4x4 Inverse4x4(Float4x4 m)
    {
        // General 4x4 inverse via cofactor expansion (same form used in OptimizeMeshesStep).
        float a00 = m.c0.X, a01 = m.c1.X, a02 = m.c2.X, a03 = m.c3.X;
        float a10 = m.c0.Y, a11 = m.c1.Y, a12 = m.c2.Y, a13 = m.c3.Y;
        float a20 = m.c0.Z, a21 = m.c1.Z, a22 = m.c2.Z, a23 = m.c3.Z;
        float a30 = m.c0.W, a31 = m.c1.W, a32 = m.c2.W, a33 = m.c3.W;

        float b00 = a00 * a11 - a01 * a10;
        float b01 = a00 * a12 - a02 * a10;
        float b02 = a00 * a13 - a03 * a10;
        float b03 = a01 * a12 - a02 * a11;
        float b04 = a01 * a13 - a03 * a11;
        float b05 = a02 * a13 - a03 * a12;
        float b06 = a20 * a31 - a21 * a30;
        float b07 = a20 * a32 - a22 * a30;
        float b08 = a20 * a33 - a23 * a30;
        float b09 = a21 * a32 - a22 * a31;
        float b10 = a21 * a33 - a23 * a31;
        float b11 = a22 * a33 - a23 * a32;

        float det = b00 * b11 - b01 * b10 + b02 * b09 + b03 * b08 - b04 * b07 + b05 * b06;
        if (MathF.Abs(det) < 1e-12f) return Float4x4.Identity;
        float invDet = 1f / det;

        return new Float4x4(
            new Float4(
                (a11 * b11 - a12 * b10 + a13 * b09) * invDet,
                (-a10 * b11 + a12 * b08 - a13 * b07) * invDet,
                (a10 * b10 - a11 * b08 + a13 * b06) * invDet,
                (-a10 * b09 + a11 * b07 - a12 * b06) * invDet),
            new Float4(
                (-a01 * b11 + a02 * b10 - a03 * b09) * invDet,
                (a00 * b11 - a02 * b08 + a03 * b07) * invDet,
                (-a00 * b10 + a01 * b08 - a03 * b06) * invDet,
                (a00 * b09 - a01 * b07 + a02 * b06) * invDet),
            new Float4(
                (a31 * b05 - a32 * b04 + a33 * b03) * invDet,
                (-a30 * b05 + a32 * b02 - a33 * b01) * invDet,
                (a30 * b04 - a31 * b02 + a33 * b00) * invDet,
                (-a30 * b03 + a31 * b01 - a32 * b00) * invDet),
            new Float4(
                (-a21 * b05 + a22 * b04 - a23 * b03) * invDet,
                (a20 * b05 - a22 * b02 + a23 * b01) * invDet,
                (-a20 * b04 + a21 * b02 - a23 * b00) * invDet,
                (a20 * b03 - a21 * b01 + a22 * b00) * invDet));
    }
}

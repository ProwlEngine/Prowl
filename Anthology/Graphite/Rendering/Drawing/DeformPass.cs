using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

using Prowl.Graphite.ShaderDef;
using Prowl.Vector;

namespace Prowl.Graphite.Rendering;

/// <summary>
/// Fills the frame's new deformed copies from their meshes, then deforms the copies a view sees this frame whose bones
/// or blend shape weights changed: morphing copies take their rest pose, each slot of active frames adds its deltas
/// across every copy, then skinning reads the result. Barriers fall between those steps only, as copies never share
/// words. A copy deformed the frame it is made skips the streams the deform writes. After the upload and before the
/// fence every view follows.
/// </summary>
internal sealed class DeformPass(ComputeProgram program, GeometryPool geometry) : IDisposable
{
    [StructLayout(LayoutKind.Sequential)]
    private struct Push
    {
        public uint Mode, First, Count, Streams;
        public uint SourceWord, TargetWord, Normal, TargetNormal;
        public uint Tangent, TargetTangent, BoneIndices, BoneWeights;
        public uint BoneBase, BoneCount, Deltas;
        public float Weight;
    }

    private const uint MaxPerDispatch = 65535 * 64;
    private const uint SkinnedStreams = (1u << (int)VertexSemantic.Position) | (1u << (int)VertexSemantic.Normal) | (1u << (int)VertexSemantic.Tangent);

    private readonly PropertySet _properties = new();
    private readonly List<(RenderWorld.RenderSide World, int Index)> _jobs = new();

    public readonly List<RenderWorld.RenderSide> Worlds = new();

    public void Add(FrameGraph graph)
    {
        graph.AddPass("Graphite.Deform", builder =>
        {
            foreach (GeometryPage page in geometry.Pages)
            {
                builder.Write(graph.Import(page.Vertices), BufferAccess.ShaderRead | BufferAccess.ShaderWrite);
                builder.Write(graph.Import(page.Indices), BufferAccess.ShaderRead | BufferAccess.ShaderWrite);
            }
            foreach (RenderWorld.RenderSide world in Worlds)
                builder.Read(graph.Import(world.SkinMatrices.Buffer), BufferAccess.ShaderRead);
        }, Render);
    }

    private void Render(PassContext context)
    {
        CommandBuffer cmd = context.Command;
        _jobs.Clear();
        bool bound = false, morphs = false;
        int slots = 0;
        foreach (RenderWorld.RenderSide world in Worlds)
        {
            if (world.NewCopies.Count == 0 && world.Deforming.Count == 0)
                continue;
            if (!bound)
            {
                cmd.SetComputeShader(program);
                bound = true;
            }

            foreach ((int index, MeshPlacement from, MeshPlacement to) in world.NewCopies)
            {
                uint skipped = world.DeformNow[index] ? SkinnedStreams : 0;
                DeviceBuffer source = geometry.Pages[from.Page].Vertices, target = geometry.Pages[to.Page].Vertices;
                for (int s = 0; s < MeshLayout.StreamCount; s++)
                {
                    if ((from.StreamsPresent & (1u << s) & ~skipped) != 0)
                        Copy(cmd, world, source, target, from.StreamOffsets[s], to.StreamOffsets[s], MeshLayout.WordsPerVertex((VertexSemantic)s) * from.VertexCount);
                }
                Copy(cmd, world, geometry.Pages[from.Page].Indices, geometry.Pages[to.Page].Indices, from.IndexBase, to.IndexBase, from.Indices.Size);
            }

            foreach (int index in world.Deforming)
            {
                if (!world.DeformNow[index])
                    continue;
                _jobs.Add((world, index));
                MeshPlacement from = world.Mesh[index]!.Placement!, to = world.Deformed[index]!;
                if (from.BlendFrames.Length == 0)
                    continue;
                morphs = true;
                slots = Math.Max(slots, world.ActiveFrames[index].Length);
                DeviceBuffer source = geometry.Pages[from.Page].Vertices, target = geometry.Pages[to.Page].Vertices;
                for (int s = 0; s < MeshLayout.StreamCount; s++)
                {
                    if ((from.StreamsPresent & (1u << s) & SkinnedStreams) != 0)
                        Copy(cmd, world, source, target, from.StreamOffsets[s], s == (int)VertexSemantic.Position ? world.PoseTarget[index] : to.StreamOffsets[s], MeshLayout.WordsPerVertex((VertexSemantic)s) * from.VertexCount);
                }
            }
        }
        if (morphs)
        {
            cmd.ComputeBarrier();
            for (int slot = 0; slot < slots; slot++)
            {
                foreach ((RenderWorld.RenderSide world, int index) in _jobs)
                {
                    if (slot < world.ActiveFrames[index].Length)
                        Morph(cmd, world, index, world.ActiveFrames[index][slot]);
                }
                cmd.ComputeBarrier();
            }
        }
        foreach ((RenderWorld.RenderSide world, int index) in _jobs)
            Skin(cmd, world, index);
        foreach (RenderWorld.RenderSide world in Worlds)
            world.CommitDeformation();
    }

    // Adds one frame's weighted deltas to the copy's pose
    private void Morph(CommandBuffer cmd, RenderWorld.RenderSide world, int index, (int Frame, float Weight) active)
    {
        MeshPlacement from = world.Mesh[index]!.Placement!, to = world.Deformed[index]!;
        BlendFrame frame = from.BlendFrames[active.Frame];
        bool normals = frame.Normals != BlendFrame.None, tangents = frame.Tangents != BlendFrame.None;
        Bind(cmd, world, geometry.Pages[from.Page].Vertices, geometry.Pages[to.Page].Vertices);
        Dispatch(cmd, new Push
        {
            Mode = 2,
            Count = frame.Count,
            Streams = (normals ? 1u : 0) | (tangents ? 2u : 0),
            SourceWord = frame.Vertices,
            Deltas = frame.Positions,
            Normal = frame.Normals,
            Tangent = frame.Tangents,
            TargetWord = world.PoseTarget[index],
            TargetNormal = to.StreamOffsets[(int)VertexSemantic.Normal],
            TargetTangent = to.StreamOffsets[(int)VertexSemantic.Tangent],
            Weight = active.Weight,
        });
    }

    // Skins the copy from the mesh's rest pose, or from its own pose when it morphs
    private void Skin(CommandBuffer cmd, RenderWorld.RenderSide world, int index)
    {
        MeshPlacement from = world.Mesh[index]!.Placement!, to = world.Deformed[index]!;
        if (!world.Skins(index, from))
            return;
        (uint first, uint count) = world.BonesOf(index);
        uint ownPose = from.BlendFrames.Length > 0 ? 4u : 0;
        Bind(cmd, world, geometry.Pages[from.Page].Vertices, geometry.Pages[to.Page].Vertices);
        Dispatch(cmd, new Push
        {
            Mode = 1,
            Count = from.VertexCount,
            Streams = ((from.StreamsPresent >> (int)VertexSemantic.Normal) & 1) | (((from.StreamsPresent >> (int)VertexSemantic.Tangent) & 1) << 1) | ownPose,
            SourceWord = from.StreamOffsets[(int)VertexSemantic.Position],
            TargetWord = world.PoseTarget[index],
            Normal = from.StreamOffsets[(int)VertexSemantic.Normal],
            TargetNormal = to.StreamOffsets[(int)VertexSemantic.Normal],
            Tangent = from.StreamOffsets[(int)VertexSemantic.Tangent],
            TargetTangent = to.StreamOffsets[(int)VertexSemantic.Tangent],
            BoneIndices = from.StreamOffsets[(int)VertexSemantic.BoneIndices],
            BoneWeights = from.StreamOffsets[(int)VertexSemantic.BoneWeights],
            BoneBase = first,
            BoneCount = count,
        });
    }


    private void Copy(CommandBuffer cmd, RenderWorld.RenderSide world, DeviceBuffer source, DeviceBuffer target, uint sourceWord, uint targetWord, uint words)
    {
        Bind(cmd, world, source, target);
        Dispatch(cmd, new Push { Mode = 0, Count = words, SourceWord = sourceWord, TargetWord = targetWord });
    }

    private void Bind(CommandBuffer cmd, RenderWorld.RenderSide world, DeviceBuffer source, DeviceBuffer target)
    {
        _properties.SetBuffer("Graphite_DeformSource", source);
        _properties.SetBuffer("Graphite_DeformTarget", target);
        _properties.SetBuffer("Graphite_SkinMatrices", world.SkinMatrices.Buffer);
        cmd.SetProperties(_properties);
    }

    // In runs of at most 65535 groups
    private static void Dispatch(CommandBuffer cmd, Push push)
    {
        for (uint first = 0; first < push.Count; first += MaxPerDispatch)
        {
            push.First = first;
            cmd.SetPushConstants(push);
            cmd.Dispatch((Math.Min(MaxPerDispatch, push.Count - first) + 63) / 64, 1, 1);
        }
    }

    public void Dispose() => program.Dispose();
}

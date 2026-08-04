using System;

using Silk.NET.Vulkan;

namespace Prowl.Graphite.Vk;

// Lets VkDescriptorBinder read the Vulkan-specific descriptor plumbing off a ShaderProgram without
// caring whether it is backing a graphics draw or a compute dispatch.
internal interface IVkDescriptorProgram
{
    DescriptorSetLayout[] DescriptorSetLayouts { get; }
    DescriptorResourceCounts[] PerSetCounts { get; }
    PipelineLayout PipelineLayout { get; }
    uint ResourceSetCount { get; }
    VkDescriptorSetCache DescriptorCache { get; }
}

// Owns the per-command-buffer descriptor resolve/cache/bind pipeline.
// Each command buffer has one, and its bind state is cleared at the start of every recording.
internal unsafe sealed partial class VkDescriptorBinder
{
    internal const int MaxSetElements = ResourceLayoutDescription.MaxElementsPerSet;

    private sealed class SetBindState
    {
        public readonly ulong[] Identity = new ulong[1 + MaxSetElements * 3];
        public int IdentityLen = -1;
        public DescriptorSet Set;
        public uint[] DynOffsets = new uint[16];
        public int DynOffsetCount;

        public DescriptorSet BoundSet;
        public uint[] BoundDynOffsets = new uint[16];
        public int BoundDynOffsetCount;
        public bool Bound;
    }

    private readonly VkCommandBuffer _cbOwner;
    private readonly VkGraphicsDevice _gd;

    // Resolve-once scratch: every element of the current set resolved a single time per draw, then
    // read by identity-building, dynamic-offset gathering, texture transitions, and descriptor writes.
    private readonly ResolvedBinding[] _resolveScratch = new ResolvedBinding[MaxSetElements];

    // Scratch identity built each draw, compared against the per-set cached identity below.
    private readonly ulong[] _identityScratch = new ulong[1 + MaxSetElements * 3];

    private SetBindState[] _setBindStates = Array.Empty<SetBindState>();
    private ShaderProgram _bindCacheProgram;

    // Whole-draw fast path: the program + property epoch a graphics draw was last prepared for.
    private ShaderProgram _lastPreparedProgram;
    private uint _lastPreparedEpoch;

    // Set count and first-changed-set index from the most recent Prepare() call that returned true;
    // EmitBind() reads them back so callers don't have to re-derive them from the program.
    private uint _preparedSetCount;
    private uint _preparedFirstSet;

    public VkDescriptorBinder(VkCommandBuffer owner, VkGraphicsDevice gd)
    {
        _cbOwner = owner;
        _gd = gd;
    }

    // Bind state does not survive a command buffer boundary. Packed uniform blocks do - they live on the
    // execution's arena, which is what keeps them shared across the passes of one execution.
    internal void ClearForNewRecording()
    {
        _bindCacheProgram = null;
        _lastPreparedProgram = null;
        _lastPreparedEpoch = 0;
        for (int i = 0; i < _setBindStates.Length; i++)
        {
            _setBindStates[i].IdentityLen = -1;
            _setBindStates[i].Bound = false;
        }
    }

    // Resolves sets, transitions textures, and prepares descriptors for binding. Returns true if binding needed, false otherwise
    internal bool Prepare(ShaderProgram program, ShaderProgram reportProgram, bool isGraphics, bool renderPassActive)
    {
        IVkDescriptorProgram descProgram = (IVkDescriptorProgram)program;
        uint setCount = descProgram.ResourceSetCount;
        if (setCount == 0) return false;

        // No bind needed, everything is the same as last draw
        if (isGraphics
            && renderPassActive
            && ReferenceEquals(program, _lastPreparedProgram)
            && _cbOwner.ActivePropertiesEpoch == _lastPreparedEpoch)
        {
            return false;
        }

        VkDescriptorSetCache cache = descProgram.DescriptorCache;
        ResourceLayoutDescription[] resourceLayouts = program.ResourceLayoutsArray;
        SetBindingMetadata[] metadata = program.BindingMetadata;
        DescriptorSetLayout[] dslLayouts = descProgram.DescriptorSetLayouts;
        DescriptorResourceCounts[] perSetCounts = descProgram.PerSetCounts;

        EnsureBindCacheFor(program, (int)setCount);
        ulong executionId = _cbOwner.ExecutionId;

        int firstChanged = -1;
        for (int setIdx = 0; setIdx < (int)setCount; setIdx++)
        {
            ResourceLayoutElementDescription[] elements = resourceLayouts[setIdx].Elements ?? Array.Empty<ResourceLayoutElementDescription>();
            SetBindingMetadata meta = metadata[setIdx];
            SetBindState state = _setBindStates[setIdx];

            ResolveSet(setIdx, elements, meta, reportProgram);
            TransitionResolvedTextures(elements);
            SyncSet(cache, state, setIdx, elements, dslLayouts[setIdx], in perSetCounts[setIdx], executionId, reportProgram);
            GatherDynOffsets(meta, state);

            if (firstChanged < 0 && SetDiffersFromBound(state))
                firstChanged = setIdx;
        }

        _lastPreparedProgram = isGraphics ? program : null;
        _lastPreparedEpoch = _cbOwner.ActivePropertiesEpoch;
        _preparedSetCount = setCount;

        if (firstChanged < 0) return false;

        _preparedFirstSet = (uint)firstChanged;
        return true;
    }

    private static bool SetDiffersFromBound(SetBindState state)
    {
        if (!state.Bound) return true;
        if (state.Set.Handle != state.BoundSet.Handle) return true;
        if (state.DynOffsetCount != state.BoundDynOffsetCount) return true;

        for (int i = 0; i < state.DynOffsetCount; i++)
        {
            if (state.DynOffsets[i] != state.BoundDynOffsets[i])
                return true;
        }

        return false;
    }

    internal void EmitBind(PipelineLayout pipelineLayout, PipelineBindPoint bindPoint)
    {
        uint firstSet = _preparedFirstSet;
        uint setCount = _preparedSetCount;
        uint count = setCount - firstSet;
        Silk.NET.Vulkan.CommandBuffer cb = _cbOwner.CommandBuffer;

        DescriptorSet* sets = stackalloc DescriptorSet[(int)count];
        int totalDyn = 0;
        for (int i = (int)firstSet; i < (int)setCount; i++)
            totalDyn += _setBindStates[i].DynOffsetCount;

        uint* dynOffsets = stackalloc uint[totalDyn > 0 ? totalDyn : 1];
        int d = 0;
        for (int i = (int)firstSet; i < (int)setCount; i++)
        {
            SetBindState st = _setBindStates[i];
            sets[i - firstSet] = st.Set;
            for (int k = 0; k < st.DynOffsetCount; k++)
                dynOffsets[d++] = st.DynOffsets[k];
        }

        _gd.Vk.CmdBindDescriptorSets(cb, bindPoint, pipelineLayout, firstSet, count, sets, (uint)d, dynOffsets);
        _cbOwner.RecordResourceSetBind(count);

        for (int i = (int)firstSet; i < (int)setCount; i++)
        {
            SetBindState st = _setBindStates[i];
            st.BoundSet = st.Set;
            if (st.BoundDynOffsets.Length < st.DynOffsetCount)
                st.BoundDynOffsets = new uint[st.DynOffsetCount];
            Array.Copy(st.DynOffsets, st.BoundDynOffsets, st.DynOffsetCount);
            st.BoundDynOffsetCount = st.DynOffsetCount;
            st.Bound = true;
        }
    }

    private void EnsureBindCacheFor(ShaderProgram program, int setCount)
    {
        if (_setBindStates.Length < setCount)
        {
            int old = _setBindStates.Length;
            Array.Resize(ref _setBindStates, setCount);
            for (int i = old; i < setCount; i++)
                _setBindStates[i] = new SetBindState();
        }

        if (!ReferenceEquals(program, _bindCacheProgram))
        {
            for (int i = 0; i < _setBindStates.Length; i++)
            {
                _setBindStates[i].IdentityLen = -1;
                _setBindStates[i].Bound = false;
            }
            _bindCacheProgram = program;
        }
    }

    // Rebuilds the set's content identity and, when it differs from what this set index last bound,
    // fetches or allocates+writes the matching descriptor set.
    private void SyncSet(
        VkDescriptorSetCache cache, SetBindState state, int setIdx, ResourceLayoutElementDescription[] elements,
        DescriptorSetLayout layout, in DescriptorResourceCounts counts, ulong executionId, ShaderProgram reportProgram)
    {
        int idLen = BuildIdentityFromScratch(setIdx, elements.Length, _identityScratch);
        ReadOnlySpan<ulong> newId = _identityScratch.AsSpan(0, idLen);

        if (state.IdentityLen == idLen && newId.SequenceEqual(state.Identity.AsSpan(0, idLen)))
            return;

        if (!cache.TryGet(newId, executionId, out DescriptorSet ds))
        {
            ds = cache.Allocate(setIdx, layout, in counts, newId, executionId);
            WriteDescriptorsFromScratch(setIdx, elements, ds, reportProgram);
        }

        state.Set = ds;
        newId.CopyTo(state.Identity);
        state.IdentityLen = idLen;
    }

    // Content key for the per-program set cache: resolved handles minus per-draw dynamic UBO offsets.
    // Byte-layout matches the original identity so cached descriptor sets remain compatible.
    private int BuildIdentityFromScratch(int setIdx, int elemCount, ulong[] dst)
    {
        int n = 0;
        dst[n++] = (ulong)setIdx;

        for (int i = 0; i < elemCount; i++)
        {
            ref ResolvedBinding r = ref _resolveScratch[i];
            switch (r.Kind)
            {
                case ResourceKind.UniformBuffer:
                    dst[n++] = r.Buffer.DeviceBuffer.Handle;
                    dst[n++] = r.DescRange;
                    break;

                case ResourceKind.StructuredBufferReadOnly:
                case ResourceKind.StructuredBufferReadWrite:
                    dst[n++] = r.Buffer.DeviceBuffer.Handle;
                    dst[n++] = r.DescOffset;
                    dst[n++] = r.DescRange;
                    break;

                case ResourceKind.TextureReadOnly:
                case ResourceKind.TextureReadWrite:
                    dst[n++] = r.View.ImageView.Handle;
                    break;

                case ResourceKind.Sampler:
                    dst[n++] = r.Sampler.DeviceSampler.Handle;
                    break;
            }
        }

        return n;
    }

    private void GatherDynOffsets(SetBindingMetadata meta, SetBindState state)
    {
        int[] order = meta.SortedUboElementIndices;
        if (state.DynOffsets.Length < order.Length)
            state.DynOffsets = new uint[order.Length];

        for (int i = 0; i < order.Length; i++)
            state.DynOffsets[i] = _resolveScratch[order[i]].DynOffset;

        state.DynOffsetCount = order.Length;
    }
}

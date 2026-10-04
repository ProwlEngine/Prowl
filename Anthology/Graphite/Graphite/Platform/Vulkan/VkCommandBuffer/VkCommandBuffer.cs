using System;
using System.Runtime.CompilerServices;

using Silk.NET.Vulkan;

namespace Prowl.Graphite.Vk;

internal unsafe partial class VkCommandBuffer : CommandBuffer
{
    private readonly VkGraphicsDevice _gd;
    private CommandPool _pool;
    private Silk.NET.Vulkan.CommandBuffer _cb;

    internal bool IsRecording => _commandBufferBegun;

    private bool _commandBufferBegun;
    private readonly System.Collections.Generic.List<VkBuffer> _stagingBuffers = [];

    private readonly VkDescriptorBinder _descriptorBinder;

    // Execution-timing query pool for the current recording, taken by the submission path once
    // End() has written the closing timestamp. Never read back on this object after that point -
    // a later Begin() may reuse this wrapper for a new recording while the old one is still in flight.
    private QueryPool? _pendingTimingPool;

    // GPU vertex/primitive-count query pool for the current recording - same lifecycle as
    // _pendingTimingPool above, just a different query type.
    private QueryPool? _pendingStatsPool;

    public CommandPool CommandPool => _pool;
    public Silk.NET.Vulkan.CommandBuffer CommandBuffer => _cb;


    public VkCommandBuffer(VkGraphicsDevice gd, CommandPool pool)
        : base(gd)
    {
        _gd = gd;
        _pool = pool;
        _descriptorBinder = new VkDescriptorBinder(this, gd);

        Constructor_RecordAllocation();
    }

    internal System.Collections.Generic.IReadOnlyList<VkSwapchain> UsedSwapchains => _usedSwapchains;

    internal override void Begin()
    {
        if (_commandBufferBegun)
        {
            throw new RenderException(
                "CommandBuffer must be in its initial state, or End() must have been called, for Begin() to be valid to call.");
        }
        _usedSwapchains.Clear();
        HasEnded = false;
        _cb = _gd.AllocatePrimaryCommandBuffer(_pool);
        if (Name.Length > 0)
            _gd.SetResourceName(this, Name);

        CommandBufferBeginInfo beginInfo = new()
        {
            SType = StructureType.CommandBufferBeginInfo,
            Flags = CommandBufferUsageFlags.OneTimeSubmitBit
        };
        _gd.Vk.BeginCommandBuffer(_cb, in beginInfo);
        _commandBufferBegun = true;
        _pendingTimingPool = _gd.BeginTiming(_cb);
        _pendingStatsPool = _gd.BeginPipelineStats(_cb);

        ClearCachedState();
        ClearGraphicsState();

        // A fresh recording binds into a fresh execution: previously-resolved descriptor sets and
        // transient UBO ranges belong to the prior execution and must not be reused.
        _descriptorBinder.ClearForNewRecording();
    }

    internal override void End()
    {
        if (!_commandBufferBegun)
        {
            throw new RenderException("CommandBuffer must have been started before End() may be called.");
        }

        _commandBufferBegun = false;
        HasEnded = true;

        if (!_currentFramebufferEverActive && _currentFramebuffer != null)
        {
            BeginCurrentRenderPass();
        }

        if (_activeRenderPass.Handle != default)
        {
            EndCurrentRenderPass();
        }

        _gd.EndTiming(_cb, _pendingTimingPool);
        _gd.EndPipelineStats(_cb, _pendingStatsPool);
        _gd.Vk.EndCommandBuffer(_cb);
    }

    // Reads and clears the timing pool End() wrote into, for the submission path to attach to
    // this specific submission. Must be called before any later Begin() on this wrapper.
    internal QueryPool? TakePendingTimingPool()
    {
        QueryPool? pool = _pendingTimingPool;
        _pendingTimingPool = null;
        return pool;
    }

    // Same as TakePendingTimingPool, for the pipeline-statistics query pool.
    internal QueryPool? TakePendingStatsPool()
    {
        QueryPool? pool = _pendingStatsPool;
        _pendingStatsPool = null;
        return pool;
    }

    internal void SetPool(CommandPool pool) => _pool = pool;

    internal void ResetForReuse()
    {
        _commandBufferBegun = false;
        HasEnded = false;
        _cb = default;
        ReleaseStagingBuffers();
    }

    private VkBuffer GetFilledStagingBuffer(IntPtr source, uint sizeInBytes)
    {
        VkBuffer staging = (VkBuffer)_gd.ResourceFactory.CreateBuffer(new BufferDescription(sizeInBytes, BufferUsage.Staging));
        staging.Name = $"Staging Buffer (CommandBuffer {Name})";
        Unsafe.CopyBlock((byte*)staging.Memory.BlockMappedPointer, source.ToPointer(), sizeInBytes);
        _stagingBuffers.Add(staging);
        return staging;
    }

    internal void ReleaseStagingBuffers()
    {
        foreach (VkBuffer buffer in _stagingBuffers)
            buffer.Dispose();
        _stagingBuffers.Clear();
    }

    private protected override void NameChanged(string name)
    {
        if (_cb.Handle != 0)
            _gd.SetResourceName(this, name);
    }

    private protected override void DisposeCore()
    {
        ReleaseStagingBuffers();
        DisposeCore_RecordFree();
    }
}

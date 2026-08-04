using System.Diagnostics;

using Silk.NET.Vulkan;

namespace Prowl.Graphite.Vk;

internal unsafe partial class VkCommandBuffer : CommandBuffer
{
    private readonly VkGraphicsDevice _gd;
    private CommandPool _pool;
    private Silk.NET.Vulkan.CommandBuffer _cb;

    /// <summary>
    /// True if not mid-recording, safe to reset and reuse. Begun-but-not-ended must dispose instead.
    /// </summary>
    internal bool CanRecycle => !_commandBufferBegun && !IsDisposed;

    private bool _commandBufferBegun;
    private bool _commandBufferEnded;

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

    public ResourceRefCount RefCount { get; }

    public VkCommandBuffer(VkGraphicsDevice gd, ref CommandBufferDescription description)
        : base(gd.Features, gd.UniformBufferMinOffsetAlignment, gd.StructuredBufferMinOffsetAlignment)
    {
        _gd = gd;
        CommandPoolCreateInfo poolCI = new()
        {
            SType = StructureType.CommandPoolCreateInfo,
            Flags = CommandPoolCreateFlags.ResetCommandBufferBit,
            QueueFamilyIndex = gd.GraphicsQueueIndex
        };
        _gd.Vk.CreateCommandPool(_gd.Device, in poolCI, null, out _pool).CheckResult();

        _cb = GetNextCommandBuffer();
        RefCount = new ResourceRefCount(DestroyNative);
        _descriptorBinder = new VkDescriptorBinder(this, gd);

        Constructor_RecordAllocation();
    }

    internal override void Begin()
    {
        if (_commandBufferBegun)
        {
            throw new RenderException(
                "CommandBuffer must be in its initial state, or End() must have been called, for Begin() to be valid to call.");
        }
        if (_commandBufferEnded)
        {
            _commandBufferEnded = false;
            HasEnded = false;
            _cb = GetNextCommandBuffer();
            if (_currentStagingInfo != null)
            {
                RecycleStagingInfo(_currentStagingInfo);
            }
        }

        _currentStagingInfo = GetStagingResourceInfo();

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
        _commandBufferEnded = true;
        HasEnded = true;

        if (!_currentFramebufferEverActive && _currentFramebuffer != null)
        {
            BeginCurrentRenderPass();
        }

        if (_activeRenderPass.Handle != default)
        {
            EndCurrentRenderPass();
            _currentFramebuffer!.TransitionToFinalLayout(_cb);
        }

        _gd.EndTiming(_cb, _pendingTimingPool);
        _gd.EndPipelineStats(_cb, _pendingStatsPool);
        _gd.Vk.EndCommandBuffer(_cb);
        _submittedCommandBuffers.Add(_cb);
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

    private protected override void NameChanged(string name) => _gd.SetResourceName(this, name);

    private protected override void DisposeCore()
    {
        RefCount.Decrement();
    }

    private void DestroyNative()
    {
        _gd.Vk.DestroyCommandPool(_gd.Device, _pool, null);

        Debug.Assert(_submittedStagingInfos.Count == 0);

        foreach (VkBuffer buffer in _availableStagingBuffers)
        {
            buffer.Dispose();
        }

        DisposeCore_RecordFree();
    }
}

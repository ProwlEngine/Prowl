using System;

namespace Prowl.Warp;

[Flags]
public enum DeliverySupport
{
    None = 0,
    Reliable = 1,
    Unreliable = 2,
    All = Reliable | Unreliable
}

/// <summary>Execution requirements declared before a transport is started.</summary>
public enum TransportExecution
{
    WorkerPreferred,
    InlinePreferred,

    /// <summary>The transport must be driven on the thread that created it.</summary>
    CreatingThreadRequired
}

/// <summary>Immutable backend metadata. Wrappers preserve the inner backend's requirements.</summary>
/// <remarks>
///     <see cref="Channels" /> counts the independent channels per delivery mode, numbered from zero. Reliable
///     messages are ordered within a channel only, so a stalled channel never holds up another.
/// </remarks>
public readonly record struct TransportCapabilities(
    bool IsAvailable,
    DeliverySupport Deliveries,
    TransportExecution Execution,
    int Channels = 1)
{
    public bool Supports(Delivery delivery) => delivery switch
    {
        Delivery.Reliable => (Deliveries & DeliverySupport.Reliable) != 0,
        Delivery.Unreliable => (Deliveries & DeliverySupport.Unreliable) != 0,
        _ => false
    };
}

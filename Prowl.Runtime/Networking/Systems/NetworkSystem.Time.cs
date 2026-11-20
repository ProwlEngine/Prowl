// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.Runtime.Resources;
using Prowl.Courier;

namespace Prowl.Runtime.Networking;

// The network tick: one every few fixed steps of the networked world, or of the wall clock while nothing ticks it.
internal sealed partial class NetworkSystem
{
    internal readonly NetworkClock HostClock = new();
    internal uint NetworkTick;
    internal double HostTime;
    private int _stepsIntoTick;
    private int _worldFrame = -1;
    private double _wallStepAt;
    private double _timeSentAt = double.NegativeInfinity;
    private double _serverTimeFloor;
    private bool _tickDue;

    internal static int StepsPerTick =>
        Math.Max(1, (int)Math.Round(1.0 / Time.FixedDeltaTime / Math.Max(Networking.TickRate, 1f)));

    internal static double TickInterval => StepsPerTick * (double)Time.FixedDeltaTime;

    internal double ServerTime
    {
        get
        {
            if (!IsHost)
            {
                return HostClock.ServerTime(Now);
            }

            double partial = WorldPaused ? 0
                : _worldFrame == Frame ? ReplicatedWorld!.Clock.FixedAccumulator
                : Math.Clamp(Now - _wallStepAt, 0, Time.FixedDeltaTime);
            return _serverTimeFloor = Math.Max(_serverTimeFloor, HostTime + partial);
        }
    }

    private bool WorldPaused => ReplicatedWorld.IsValid() && ReplicatedWorld!.Paused;

    // How fast host time runs against the wall clock now: the world's time scale, or nothing while it is paused.
    private float HostRate => WorldPaused ? 0 : _worldFrame == Frame ? ReplicatedWorld!.Clock.EffectiveTimeScale : 1;

    /// <summary>The networked world ran a frame, so its fixed steps drive the tick this frame, or hold it while paused.</summary>
    internal void OnWorldFrame(SceneInstance world)
    {
        if (world == ReplicatedWorld)
        {
            _worldFrame = Frame;
            _wallStepAt = Now;
        }
    }

    internal void OnFixedStep(SceneInstance world)
    {
        if (IsActive && world == ReplicatedWorld)
        {
            Step();
        }
    }

    private void StepWallClock()
    {
        if (_worldFrame == Frame || !IsActive)
        {
            return;
        }

        double step = Time.FixedDeltaTime;
        for (int i = 0; i < Time.MaxFixedIterations && Now - _wallStepAt >= step && IsActive && !WorldPaused; i++)
        {
            _wallStepAt += step;
            Step();
        }

        if (Now - _wallStepAt >= step)
        {
            _wallStepAt = Now;
        }
    }

    private void Step()
    {
        HostTime += Time.FixedDeltaTime;
        if (++_stepsIntoTick < StepsPerTick)
        {
            return;
        }

        if (IsHost)
        {
            NetworkTick++;
        }
        else
        {
            // A client ahead of the host waits for it, and one behind catches up.
            uint next = HostClock.Synced ? HostClock.Tick(Now, TickInterval) : NetworkTick + 1;
            if (next <= NetworkTick)
            {
                _stepsIntoTick = StepsPerTick - 1;
                return;
            }

            NetworkTick = next;
        }

        _stepsIntoTick = 0;
        _tickDue = true;
        if ((IsHost || IsReady) && ReplicatedWorld.IsValid())
        {
            ReplicatedWorld!.RunNetworkTick(NetworkTick);
        }
    }

    // Sent on the wall clock, paused or not, so clients hold their estimate while the host's time stands still.
    private void SendTime()
    {
        if (!IsActive || !IsHost || Now - _timeSentAt < TickInterval)
        {
            return;
        }

        _timeSentAt = Now;
        using NetWriter time = new(20);
        time.WriteVarUInt32(NetworkTick);
        time.WriteDouble(HostTime);
        time.WriteSingle(HostRate);
        foreach (NetPeer peer in Session!.Peers)
        {
            if (!peer.IsLocal)
            {
                SendEngine(peer, EngineMessage.Time, time.WrittenSpan, Delivery.Unreliable);
            }
        }
    }

    internal void OnTime(NetPeer sender, ref NetReader reader, Delivery delivery)
    {
        if (IsHost || !sender.IsHost)
        {
            return;
        }

        uint tick = reader.ReadVarUInt32();
        double time = reader.ReadDouble();
        float rate = reader.ReadSingle();
        HostClock.Observe(tick, time, rate, Now, sender.Connection?.RoundTripTime.TotalSeconds ?? 0);
    }
}

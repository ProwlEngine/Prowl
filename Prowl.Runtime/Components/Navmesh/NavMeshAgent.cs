// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using DotRecast.Core.Numerics;
using DotRecast.Detour;
using DotRecast.Detour.Crowd;

namespace Prowl.Runtime;

public class NavMeshAgent : MonoBehaviour
{
    [ShowInInspector]
    public NavMeshSurface Surface
    {
        get => surface;
        set
        {
            if (InternalAgent != null)
                surface?.UnregisterAgent(this);
            surface = value;
        }
    }


    [SerializeField, HideInInspector] private NavMeshSurface surface;


    public readonly float radius = 0.5f;
    public readonly float height = 1f;
    public readonly float maxAcceleration = 8.0f;
    public readonly float maxSpeed = 3.5f;
    public readonly float collisionQueryRange = 5f;
    public readonly float pathOptimizationRange = 15f;

    public readonly DtCrowdAgentConfig crowdConfig = new();

    public DtCrowdAgent? InternalAgent { get; internal set; } = null;

    public DtCrowdAgentParams GetAgentParams()
    {
        DtCrowdAgentParams ap = new DtCrowdAgentParams();
        ap.radius = radius;
        ap.height = height;
        ap.maxAcceleration = maxAcceleration;
        ap.maxSpeed = maxSpeed;
        ap.collisionQueryRange = collisionQueryRange;
        ap.pathOptimizationRange = pathOptimizationRange;
        ap.updateFlags = crowdConfig.GetUpdateFlags();
        ap.obstacleAvoidanceType = crowdConfig.obstacleAvoidanceType;
        ap.separationWeight = crowdConfig.separationWeight;
        return ap;
    }

    public override void OnValidate()
    {
        Refresh();
    }

    public void Refresh()
    {
        if (InternalAgent == null) return;

        // Save Agent State
        DtCrowdAgentState state = InternalAgent.state;
        bool partial = InternalAgent.partial;
        DtPathCorridor corridor = InternalAgent.corridor;
        DtLocalBoundary boundary = InternalAgent.boundary;
        RcVec3f npos = InternalAgent.npos;
        RcVec3f dvel = InternalAgent.dvel;
        RcVec3f nvel = InternalAgent.nvel;
        RcVec3f vel = InternalAgent.vel;
        DtStraightPath[] corners = InternalAgent.corners;
        int ncorners = InternalAgent.ncorners;
        DtMoveRequestState targetState = InternalAgent.targetState;
        long targetRef = InternalAgent.targetRef;
        RcVec3f targetPos = InternalAgent.targetPos;
        DtPathQueryResult targetPathQueryResult = InternalAgent.targetPathQueryResult;
        bool targetReplan = InternalAgent.targetReplan;
        float targetReplanTime = InternalAgent.targetReplanTime;
        float targetReplanWaitTime = InternalAgent.targetReplanWaitTime;

        surface?.UnregisterAgent(this);
        if (surface?.IsReady ?? false)
            surface?.RegisterAgent(this);

        // Restore Agent State
        InternalAgent.state = state;
        InternalAgent.partial = partial;
        InternalAgent.corridor = corridor;
        InternalAgent.boundary = boundary;
        InternalAgent.npos = npos;
        InternalAgent.dvel = dvel;
        InternalAgent.nvel = nvel;
        InternalAgent.vel = vel;
        InternalAgent.corners = corners;
        InternalAgent.ncorners = ncorners;
        InternalAgent.targetState = targetState;
        InternalAgent.targetRef = targetRef;
        InternalAgent.targetPos = targetPos;
        InternalAgent.targetPathQueryResult = targetPathQueryResult;
        InternalAgent.targetReplan = targetReplan;
        InternalAgent.targetReplanTime = targetReplanTime;
        InternalAgent.targetReplanWaitTime = targetReplanWaitTime;
    }

    public override void OnDisable()
    {
        if (InternalAgent != null && surface != null)
            surface.UnregisterAgent(this);
    }

    public override void LateUpdate()
    {
        if (InternalAgent == null)
        {
            if (surface != null && surface.IsReady)
                surface.RegisterAgent(this);
        }
        else
        {
            // calculate position
            Vector3 pos = surface.Transform.TransformPoint(new(InternalAgent.npos.X, InternalAgent.npos.Y, InternalAgent.npos.Z));
            Transform.position = pos;
        }

    }

}

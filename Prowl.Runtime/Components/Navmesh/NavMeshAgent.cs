using DotRecast.Detour.Crowd;

namespace Prowl.Runtime;

public class NavMeshAgent : MonoBehaviour
{
    [ShowInInspector]
    public NavMeshSurface Surface {
        get => surface;
        set {
            if (InternalAgent != null)
                surface?.UnregisterAgent(this);
            surface = value;
        }
    }


    [SerializeField, HideInInspector] private NavMeshSurface surface;


    public float radius = 0.5f;
    public float height = 1f;
    public float maxAcceleration = 8.0f;
    public float maxSpeed = 3.5f;
    public float collisionQueryRange = 5f;
    public float pathOptimizationRange = 15f;

    public DtCrowdAgentConfig crowdConfig = new();

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
        if (InternalAgent != null)
            surface?.UnregisterAgent(this);
        if (surface?.IsReady ?? false)
            surface?.RegisterAgent(this);
    }

    public override void OnDisable()
    {
        if(InternalAgent != null && surface != null)
            surface.UnregisterAgent(this);
    }

    public override void LateUpdate()
    {
        if (InternalAgent == null)
        {
            if(surface != null && surface.IsReady)
                surface.RegisterAgent(this);
        }
        else
        {
            // calculate position
            Vector3 pos = surface.Transform.TransformPoint(new(InternalAgent.npos.X, InternalAgent.npos.Y, InternalAgent.npos.Z));
            this.Transform.position = pos;
        }

    }

}

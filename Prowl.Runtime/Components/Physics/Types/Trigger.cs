using BepuPhysics.Collidables;

using Prowl.Runtime.Contacts;

namespace Prowl.Runtime;

public delegate void TriggerDelegate(PhysicsBody @this, PhysicsBody other);

/// <summary>
/// A contact event handler without collision response, which runs delegates on enter and exit
/// </summary>
public class Trigger : IContactEventHandler
{
    public bool NoContactResponse => true;
    public event TriggerDelegate? OnEnter, OnLeave;

    void IContactEventHandler.OnStartedTouching<TManifold>(CollidableReference eventSource, CollidableReference other, ref TManifold contactManifold, int contactIndex)
    {
        OnEnter?.Invoke(Physics.GetContainer(eventSource), Physics.GetContainer(other));
    }
    void IContactEventHandler.OnStoppedTouching<TManifold>(CollidableReference eventSource, CollidableReference other, ref TManifold contactManifold, int contactIndex)
    {
        OnLeave?.Invoke(Physics.GetContainer(eventSource), Physics.GetContainer(other));
    }
}

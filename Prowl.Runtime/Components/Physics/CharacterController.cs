// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Vector;

namespace Prowl.Runtime;

/// <summary>
/// A character controller that handles collision detection and movement.
/// provides just the core functionality.
/// </summary>
[AddComponentMenu("Physics/Character Controller")]
[ComponentIcon("\uf70c")] // PersonRunning
public class CharacterController : MonoBehaviour
{
    /// <summary>
    /// The shape type used for the character controller collision detection.
    /// </summary>
    public enum ColliderShape
    {
        Capsule,
        Cylinder
    }

    public ColliderShape Shape = ColliderShape.Cylinder;
    public float Radius = 0.5f;
    public float Height = 1.8f;
    public float SkinWidth = 0.02f;

    /// <summary>Which layers the controller collides with.</summary>
    public LayerMask CollisionMask = LayerMask.Everything;

    /// <summary>
    /// The filter every internal cast uses: the collision mask, minus anything belonging to a
    /// Rigidbody3D on this GameObject. Without that exclusion a controller placed on a body would
    /// immediately collide with itself and refuse to move.
    /// </summary>
    private QueryFilter Filter
    {
        get
        {
            var filter = new QueryFilter(CollisionMask);
            Rigidbody3D self = GetComponentInParent<Rigidbody3D>();
            return self.IsValid() ? filter.Ignoring(self) : filter;
        }
    }

    /// <summary>
    /// Maximum angle in degrees for a surface to be considered walkable (default: 45 degrees)
    /// </summary>
    public float MaxSlopeAngle = 55.0f;

    /// <summary>
    /// Distance to snap down to ground when walking off slopes (default: 0.5)
    /// </summary>
    public float SnapDownDistance = 0.5f;

    /// <summary>
    /// Maximum height the character can step up onto (default: 0.3)
    /// </summary>
    public float StepSize = 0.3f;

    /// <summary>
    /// Whether the character controller is currently grounded.
    /// </summary>
    public bool IsGrounded { get; private set; }

    private ShapeCastHit lastGroundHit;
    private Float3 lastVelocity;

    // Debug visualization for failed height attempts
    private bool failedHeightAttempt = false;
    private float failedAttemptHeight;
    private float failedAttemptRadius;

    /// <summary>
    /// Moves the character controller by the specified motion vector.
    /// This handles collision detection and sliding.
    /// Also updates the IsGrounded state.
    /// </summary>
    public void Move(Float3 motion)
    {
        Float3 position = GameObject.Transform.Position;
        lastVelocity = motion;

        // Use the grounded state from the end of the previous move for this
        // frame's step-up and snap decisions, since we haven't moved yet.
        bool wasGrounded = IsGrounded;

        // Perform movement with collision
        Float3 finalPosition = CollideAndSlide(position, motion, 0, wasGrounded);

        // Snap down to ground if moving horizontally on slopes
        if (wasGrounded && motion.Y <= 0)
        {
            finalPosition = SnapToGround(finalPosition);
        }

        GameObject.Transform.Position = finalPosition;

        // Update grounded state based on where we actually ended up this
        // frame, so callers see an up-to-date value on the next frame
        // (e.g. right after a jump leaves the ground).
        UpdateGroundedState(finalPosition);
    }

    /// <summary>
    /// Updates the grounded state by performing a ground check.
    /// </summary>
    private void UpdateGroundedState(Float3 position)
    {
        float groundCheckDistance = 0.1f;
        IsGrounded = PerformGroundCheck(position, groundCheckDistance, out lastGroundHit);
    }

    /// <summary>
    /// Performs a ground check using shape casting.
    /// Only considers the character grounded if the surface angle is walkable.
    /// </summary>
    private bool PerformGroundCheck(Float3 position, float distance, out ShapeCastHit hitInfo)
    {
        bool hit = PerformShapeCast(position, new Float3(0, -1, 0), distance, out hitInfo);

        if (!hit)
            return false;

        // Check if the surface is walkable
        float slopeAngle = GetSlopeAngle(hitInfo.Normal);
        return slopeAngle <= MaxSlopeAngle;
    }

    /// <summary>
    /// Attempts to set the height of the collider.
    /// Returns true if successful, false if the new size would collide with something.
    /// </summary>
    public bool TrySetHeight(float newHeight)
    {
        float minHeight = Shape == ColliderShape.Capsule ? Radius * 2 : 0.1f;
        if (newHeight <= minHeight)
        {
            failedHeightAttempt = false;
            return false;
        }

        Float3 position = GameObject.Transform.Position;
        bool wouldCollide = CheckShapeOverlap(position, newHeight, Radius);

        if (!wouldCollide)
        {
            Height = newHeight;
            failedHeightAttempt = false;
            return true;
        }

        // Store failed attempt for debug visualization
        failedHeightAttempt = true;
        failedAttemptHeight = newHeight;
        failedAttemptRadius = Radius;

        return false;
    }

    /// <summary>
    /// Attempts to set the radius of the collider.
    /// Returns true if successful, false if the new size would collide with something.
    /// </summary>
    public bool TrySetRadius(float newRadius)
    {
        if (newRadius <= 0)
            return false;

        if (Shape == ColliderShape.Capsule && newRadius * 2 >= Height)
            return false;

        Float3 position = GameObject.Transform.Position;
        bool wouldCollide = CheckShapeOverlap(position, Height, newRadius);

        if (!wouldCollide)
        {
            Radius = newRadius;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Checks if a shape with the given dimensions would overlap with anything.
    /// </summary>
    private bool CheckShapeOverlap(Float3 position, float height, float radius)
    {
        float effectiveRadius = radius - SkinWidth;

        if (Shape == ColliderShape.Capsule)
        {
            Float3 bottom = position + new Float3(0, radius, 0);
            Float3 top = position + new Float3(0, height - radius, 0);
            return GameObject.Scene.Physics.CheckCapsule(bottom, top, effectiveRadius, Filter);
        }
        else // Cylinder
        {
            Float3 center = position + new Float3(0, height * 0.5f, 0);
            return GameObject.Scene.Physics.CheckCylinder(center, effectiveRadius, height, Quaternion.Identity, Filter);
        }
    }

    private Float3 GetShapeCenter(Float3 position)
    {
        if (Shape == ColliderShape.Capsule)
            return position + new Float3(0, Height * 0.5f, 0);
        else // Cylinder
            return position + new Float3(0, Height * 0.5f, 0);
    }

    private Float3 GetCapsuleBottom(Float3 position)
    {
        return position + new Float3(0, GetEffectiveRadius(), 0);
    }

    private Float3 GetCapsuleTop(Float3 position)
    {
        // Keep the segment non-degenerate: Jitter rejects a capsule of zero length outright, and a
        // Height at or below twice the radius would produce one.
        float radius = GetEffectiveRadius();
        return position + new Float3(0, Maths.Max(Height - radius, radius + 0.001f), 0);
    }

    // Shape dimensions must stay positive; Jitter throws on a zero or negative radius.
    private float GetEffectiveRadius()
    {
        return Maths.Max(Radius - SkinWidth, 0.001f);
    }

    /// <summary>
    /// Performs a shape cast based on the current shape type.
    /// </summary>
    private bool PerformShapeCast(Float3 position, Float3 direction, float distance, out ShapeCastHit hitInfo)
    {
        if (Shape == ColliderShape.Capsule)
        {
            return GameObject.Scene.Physics.CapsuleCast(
                GetCapsuleBottom(position),
                GetCapsuleTop(position),
                GetEffectiveRadius(),
                direction,
                distance,
                out hitInfo,
                Filter
            );
        }
        else // Cylinder
        {
            return GameObject.Scene.Physics.CylinderCast(
                GetShapeCenter(position),
                GetEffectiveRadius(),
                Height,
                Quaternion.Identity,
                direction,
                distance,
                out hitInfo,
                Filter
            );
        }
    }

    private Float3 CollideAndSlide(Float3 position, Float3 velocity, int depth, bool grounded)
    {
        const int MaxDepth = 5;
        if (depth >= MaxDepth)
            return position;

        // A degenerate surface normal turns the projected slide into NaN, and NaN fails every "too
        // small to bother" test below, so it would ride all the way into the next cast and take the
        // solver down with it. Stop here instead and keep the last good position.
        if (!float.IsFinite(velocity.X) || !float.IsFinite(velocity.Y) || !float.IsFinite(velocity.Z))
            return position;

        float moveDistance = Float3.Length(velocity);
        if (moveDistance < 0.0001)
            return position;

        Float3 moveDirection = Float3.Normalize(velocity);
        if (Float3.LengthSquared(moveDirection) <= 0.0f)
            return position;

        float castDistance = moveDistance + SkinWidth;
        bool hit = PerformShapeCast(position, moveDirection, castDistance, out ShapeCastHit hitInfo);

        if (!hit)
            return position + velocity;

        // Back off by the skin width, and clamp so a cast that started already touching never walks
        // backwards.
        float safeDistance = Maths.Clamp(hitInfo.Distance - SkinWidth, 0.0f, moveDistance);
        position += moveDirection * safeDistance;

        // Calculate remaining movement after hitting surface
        float remainingDistance = moveDistance - safeDistance;
        Float3 remainingMove = moveDirection * remainingDistance;

        // Check if this is a step we can climb
        // Only attempt step-up if we're grounded and moving mostly horizontally
        float horizontalSpeed = Maths.Sqrt(velocity.X * velocity.X + velocity.Z * velocity.Z);
        if (grounded && horizontalSpeed > 0.0001 && StepSize > 0)
        {
            if (TryStepUp(position, moveDirection, remainingDistance, out Float3 steppedPosition))
            {
                return steppedPosition;
            }
        }

        // Project remaining movement onto the hit surface (slide)
        Float3 slideMove = ProjectOntoSurface(remainingMove, hitInfo.Normal);

        // Recurse with remaining slide movement
        return CollideAndSlide(position, slideMove, depth + 1, grounded);
    }

    /// <summary>
    /// Attempts to step up onto an obstacle.
    /// Returns true if step-up was successful, with the new position.
    /// </summary>
    private bool TryStepUp(Float3 position, Float3 moveDirection, float moveDistance, out Float3 newPosition)
    {
        newPosition = position;

        // Step 1: Extract horizontal direction
        Float3 forwardDirection = new(moveDirection.X, 0, moveDirection.Z);
        if (Float3.LengthSquared(forwardDirection) < 0.0001)
            return false; // No horizontal movement

        forwardDirection = Float3.Normalize(forwardDirection);

        // Step 2: Move up by StepSize
        Float3 upPosition = position + new Float3(0, StepSize, 0);

        // Step 3: Check if there's clearance at the elevated position
        bool hasOverheadClearance = !PerformShapeCast(
            position,
            new Float3(0, 1, 0),
            StepSize + SkinWidth,
            out _
        );

        if (!hasOverheadClearance)
            return false;

        // Step 4: Try to move forward at the elevated position
        float forwardCastDistance = moveDistance + SkinWidth;
        bool hitAtElevated = PerformShapeCast(
            upPosition,
            forwardDirection,
            forwardCastDistance,
            out ShapeCastHit elevatedHit
        );

        // Step 5: Move forward at elevated height, only as far as the cast is actually clear. If most
        // of the move is still blocked up there it is a wall, not a step, so slide instead.
        float forwardDistance = moveDistance;
        if (hitAtElevated)
        {
            forwardDistance = Maths.Clamp(elevatedHit.Distance - SkinWidth, 0.0f, moveDistance);
            if (forwardDistance < moveDistance * 0.5f)
                return false;
        }

        Float3 forwardPosition = upPosition + forwardDirection * forwardDistance;

        // Step 6: Cast down to find the actual step surface
        // Search from StepSize height down to slightly below original position for reliability sake
        float maxStepDownDistance = StepSize + SkinWidth + 0.1f;
        bool hasGroundBelow = PerformShapeCast(
            forwardPosition,
            new Float3(0, -1, 0),
            maxStepDownDistance,
            out ShapeCastHit downHit
        );

        if (hasGroundBelow)
        {
            // Verify the surface is walkable dont want to perform steps on steep surfaces or walls
            float slopeAngle = GetSlopeAngle(downHit.Normal);
            if (slopeAngle > MaxSlopeAngle)
                return false; // Surface is too steep

            // Drop onto the surface. Descending further than we rose means this is a step down, not up.
            float stepDownDistance = Maths.Max(0.0f, downHit.Distance - SkinWidth);
            if (stepDownDistance > StepSize)
                return false;

            newPosition = forwardPosition - new Float3(0, stepDownDistance, 0);
            return true;
        }

        // No ground found - don't allow step up as character would fall
        return false;
    }

    private Float3 ProjectOntoSurface(Float3 movement, Float3 surfaceNormal)
    {
        // Project remaining movement onto the hit surface
        return Float3.ProjectOntoPlane(movement, surfaceNormal);
    }

    /// <summary>
    /// Calculates the angle of a surface in degrees from horizontal.
    /// </summary>
    private float GetSlopeAngle(Float3 normal)
    {
        // Angle between surface normal and up vector. Clamped because a normal component a hair over 1
        // from float error makes Acos return NaN, which fails every walkable test and would silently
        // make the character un-groundable.
        return Maths.Acos(Maths.Clamp(normal.Y, -1.0f, 1.0f)) * (180.0f / Maths.PI);
    }

    /// <summary>
    /// Snaps the character down to the ground when walking on slopes.
    /// This prevents the character from "floating" when transitioning between slopes.
    /// </summary>
    private Float3 SnapToGround(Float3 position)
    {
        // Only snap if we have horizontal velocity
        float horizontalSpeed = Maths.Sqrt(lastVelocity.X * lastVelocity.X + lastVelocity.Z * lastVelocity.Z);
        if (horizontalSpeed < 0.0001f)
            return position;

        // Check if there's ground below us within snap distance
        bool hit = PerformShapeCast(
            position,
            new Float3(0, -1, 0),
            SnapDownDistance,
            out ShapeCastHit hitInfo
        );

        if (hit)
        {
            // Check if the surface is walkable
            float slopeAngle = GetSlopeAngle(hitInfo.Normal);
            if (slopeAngle <= MaxSlopeAngle)
            {
                // Snap down to the surface
                float snapDistance = hitInfo.Distance - SkinWidth;
                if (snapDistance > 0)
                {
                    position.Y -= snapDistance;
                }
            }
        }

        return position;
    }

    public override void DrawGizmos()
    {
        if (GameObject.Scene.Physics == null) return;

        Float3 position = GameObject.Transform.Position;

        if (Shape == ColliderShape.Capsule)
        {
            Debug.DrawWireCapsule(GetCapsuleBottom(position), GetCapsuleTop(position), Radius, Color.Cyan, 16);
        }
        else // Cylinder
        {
            Debug.DrawWireCylinder(GetShapeCenter(position), Quaternion.Identity, Radius, Height, Color.Cyan, 16);
        }

        // Draw ground hit if grounded
        if (lastGroundHit.Hit)
        {
            lastGroundHit.DrawGizmos();
        }

        // Draw failed height attempt in red
        if (failedHeightAttempt)
        {
            if (Shape == ColliderShape.Capsule)
            {
                Float3 bottom = position + new Float3(0, failedAttemptRadius, 0);
                Float3 top = position + new Float3(0, failedAttemptHeight - failedAttemptRadius, 0);
                Debug.DrawWireCapsule(bottom, top, failedAttemptRadius, Color.Red, 16);
            }
            else // Cylinder
            {
                Float3 center = position + new Float3(0, failedAttemptHeight * 0.5f, 0);
                Debug.DrawWireCylinder(center, Quaternion.Identity, failedAttemptRadius, failedAttemptHeight, Color.Red, 16);
            }
        }
    }
}

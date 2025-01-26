// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Linq;

using Prowl.Icons;

namespace Prowl.Runtime;

// Based on the Jitter2 Demo Raycast Car

[AddComponentMenu($"{FontAwesome6.HillRockslide}  Physics/Experimental/{FontAwesome6.Circle}  Wheel Collider")]
[ExecutionOrder(-1000)]
public sealed class WheelCollider : MonoBehaviour
{
    public bool IsLocked;
    public double SteerAngle;
    public double Damping = 1500;
    public double Spring = 35000;
    public double Inertia = 5;
    public double Radius = 0.5f;
    public double SideFriction = 3.2f;
    public double ForwardFriction = 5.0f;
    public double SuspensionTravel = 0.2f;
    public double MaximumAngularVelocity = 200f;
    public int NumberOfRays = 1;

    // Private
    private double _displacement = 0f, _lastDisplacement = 0f;
    private double _upSpeed = 0f;
    private bool _grounded = false;
    private double _driveTorque = 0f;
    private double _angularVelocity = 0f;
    private double _angularVelocityForGrip = 0f;
    private double _torque = 0f;

    private List<Vector3> _debugRayStart = new();
    private List<Vector3> _debugRayEnd = new();

    private bool _hasPreStepped = false;

    // Internal
    internal Rigidbody3D _attachedBody = null;

    public Rigidbody3D Body
    {
        get
        {
            if (_attachedBody == null)
                _attachedBody = GameObject.GetComponentInParent<Rigidbody3D>(true, true) ?? throw new Exception("WheelCollider: No Rigidbody3D component found in parent GameObject.");

            return _attachedBody;
        }
    }

    public bool IsGrounded => _grounded;
    public double Displacement => _displacement;
    public Vector3 WorldPosition => Transform.position + (Transform.up * _displacement);
    public double WheelRotation { get; private set; } = 0.0;

    public void AddTorque(double torque)
    {
        _driveTorque += torque;
    }

    public void PostStep()
    {
        double timeStep = Time.fixedDeltaTime;

        // Check for no update
        if (timeStep <= 0.0 || Body == null || Body.Enabled == false)
            return;

        double origAngVel = _angularVelocity;
        _upSpeed = (_displacement - _lastDisplacement) / timeStep;

        // Check for wheel locked in place
        if (IsLocked == true)
        {
            _angularVelocity = 0;
            _torque = 0;
        }
        else
        {
            _angularVelocity += _torque * timeStep / Inertia;
            _torque = 0;

            // Don't apply much torque if not grounded
            if (_grounded == false)
                _driveTorque *= 0.1;

            // Prevent friction from reversing dir - todo do this better by limiting the torque
            if ((origAngVel > _angularVelocityForGrip && _angularVelocity < _angularVelocityForGrip) ||
                (origAngVel < _angularVelocityForGrip && _angularVelocity > _angularVelocityForGrip))
                _angularVelocity = _angularVelocityForGrip;

            _angularVelocity += _driveTorque * timeStep / Inertia;
            _driveTorque = 0;

            // Update rotation velocity
            double maxAngVel = MaximumAngularVelocity;
            _angularVelocity = Math.Clamp(_angularVelocity, -maxAngVel, maxAngVel);

            // Update rotation value
            WheelRotation += timeStep * _angularVelocity;
        }
    }

    public override void FixedUpdate()
    {
        // First pass wont execute PostStep
        if (_hasPreStepped)
            PostStep();

        _hasPreStepped = true;

        // Check for no update
        if (Body == null || Body.Enabled == false)
            return;

        Vector3 force = Vector3.zero;
        _lastDisplacement = _displacement;
        _displacement = 0.0f;

        Vector3 worldPos = Transform.position;
        Vector3 worldAxis = Body.Transform.up;

        Vector3 forward = Body.Transform.forward;
        Vector3 wheelFwd = Quaternion.AngleAxis((float)SteerAngle, worldAxis) * forward;

        Vector3 wheelLeft = Vector3.Cross(worldAxis, wheelFwd);
        wheelLeft.Normalize();

        Vector3 wheelUp = Vector3.Cross(wheelFwd, wheelLeft);

        double rayLen = 2.0 * Radius + SuspensionTravel;

        Vector3 wheelRayEnd = worldPos - Radius * worldAxis;
        Vector3 wheelRayOrigin = wheelRayEnd + rayLen * worldAxis;
        Vector3 wheelRayDelta = wheelRayEnd - wheelRayOrigin;

        double deltaFwd = 2.0 * Radius / (NumberOfRays + 1);
        double deltaFwdStart = deltaFwd;

        _grounded = false;

        Vector3 groundNormal = Vector3.zero;
        Vector3 groundPos = Vector3.zero;
        Rigidbody3D worldBody = null!;

        double deepestFrac = double.MaxValue;

        _debugRayStart.Clear();
        _debugRayEnd.Clear();
        for (int i = 0; i < NumberOfRays; i++)
        {
            double distFwd = deltaFwdStart + i * deltaFwd - Radius;
            double zOffset = Radius * (1.0 - Math.Cos(Math.PI / 2.0 * (distFwd / Radius)));
            Vector3 newOrigin = wheelRayOrigin + distFwd * wheelFwd + zOffset * wheelUp;

            _debugRayStart.Add(newOrigin);
            bool result = Physics.Raycast(newOrigin, wheelRayDelta, out RaycastHit hitInfo, rayLen, LayerMask.Everything);

            //Vector3 minBox = worldPos - new Vector3(Radius);
            //Vector3 maxBox = worldPos + new Vector3(Radius);

            if (result)
            {
                if (hitInfo.distance < deepestFrac)
                {
                    deepestFrac = hitInfo.distance;
                    groundPos = newOrigin + hitInfo.distance * wheelRayDelta;
                    groundNormal = hitInfo.normal;
                    worldBody = hitInfo.rigidbody;
                }

                _grounded = true;

                _debugRayEnd.Add(newOrigin + (hitInfo.distance * wheelRayDelta));
            }
            else
            {
                _debugRayEnd.Add(newOrigin + wheelRayDelta);
            }
        }

        if (!_grounded) return;

        if (groundNormal.sqrMagnitude > 0.0) groundNormal.Normalize();

        _displacement = (rayLen - deepestFrac);
        _displacement = Math.Clamp(_displacement, 0.0, SuspensionTravel);

        double displacementForceMag = _displacement * Spring;

        // reduce force when suspension is par to ground
        displacementForceMag *= Vector3.Dot(groundNormal, worldAxis);

        // apply damping
        double dampingForceMag = _upSpeed * Damping;

        double totalForceMag = displacementForceMag + dampingForceMag;

        if (totalForceMag < 0.0) totalForceMag = 0.0;

        Vector3 extraForce = totalForceMag * worldAxis;

        force += extraForce;



        // side-slip friction and drive force. Work out wheel- and floor-relative coordinate frame
        Vector3 groundUp = groundNormal;
        Vector3 groundLeft = Vector3.Cross(groundNormal, wheelFwd);
        
        if (groundLeft.sqrMagnitude > 0.0) groundLeft.Normalize();
        
        Vector3 groundFwd = Vector3.Cross(groundLeft, groundUp);
        
        Vector3 wheelCenterVel = Body.Velocity + Vector3.Cross(Body.AngularVelocity, Vector3.Transform(this.Transform.localPosition, Body.Transform.rotation));

        // rimVel=(wxr)*v
        Vector3 rimVel = _angularVelocity * Vector3.Cross(wheelLeft, groundPos - worldPos);
        Vector3 wheelPointVel = wheelCenterVel + rimVel;

        if (worldBody == null) throw new Exception("world Body is null.");

        Vector3 worldVel = worldBody.Velocity + Vector3.Cross(worldBody.AngularVelocity, groundPos - worldBody.Transform.position);

        wheelPointVel -= worldVel;

        // sideways forces
        double noslipVel = 0.2;
        double slipVel = 0.4;
        double slipFactor = 0.7;
        
        double smallVel = 3.0;
        double friction = SideFriction;
        
        double sideVel = Vector3.Dot(wheelPointVel, groundLeft);

        if (sideVel > slipVel || sideVel < -slipVel)
        {
            friction *= slipFactor;
        }
        else if (sideVel > noslipVel || sideVel < -noslipVel)
        {
            friction *= 1.0 - (1.0 - slipFactor) * (Math.Abs(sideVel) - noslipVel) / (slipVel - noslipVel);
        }
        
        if (sideVel < 0.0)
            friction *= -1.0;
        
        if (Math.Abs(sideVel) < smallVel)
            friction *= Math.Abs(sideVel) / smallVel;
        
        double sideForce = -friction * totalForceMag;

        extraForce = sideForce * groundLeft;
        force += extraForce;



        // TODO: These are for some reason unstable to use, once used the results are very chaotic
        // But their kinda needed to actually move the car, and also to track wheel rotation
        //// fwd/back forces
        //friction = ForwardFriction;
        //double fwdVel = Vector3.Dot(wheelPointVel, groundFwd);
        //
        //if (fwdVel > slipVel || fwdVel < -slipVel)
        //{
        //    friction *= slipFactor;
        //}
        //else if (fwdVel > noslipVel || fwdVel < -noslipVel)
        //{
        //    friction *= 1.0 - (1.0 - slipFactor) * (Math.Abs(fwdVel) - noslipVel) / (slipVel - noslipVel);
        //}
        //
        //if (fwdVel < 0.0)
        //    friction *= -1.0;
        //
        //if (Math.Abs(fwdVel) < smallVel)
        //    friction *= Math.Abs(fwdVel) / smallVel;
        //
        //double fwdForce = -friction * totalForceMag;
        //
        //extraForce = fwdForce * groundFwd;
        //force += extraForce;
        //
        //// fwd force also spins the wheel
        //_angularVelocityForGrip = Vector3.Dot(wheelCenterVel, groundFwd) / Radius;
        //_torque += -fwdForce * Radius;

        // add force to car
        Body.AddForceAtPosition(force, groundPos);
        Body._body.DeactivationTime = TimeSpan.MaxValue;
    }

    public override void DrawGizmos()
    {
        Vector3 wheelFwd = Quaternion.AngleAxis((float)SteerAngle, Body.Transform.up) * Body.Transform.forward;
        Vector3 wheelLeft = Vector3.Cross(Body.Transform.up, wheelFwd);
        wheelLeft.Normalize();

        // Debug Draw
        Debug.DrawWireCircle(WorldPosition, wheelLeft, Radius, Color.green, 32);

        // Draw Wheel Rotation With a Line
        Vector3 wheelEnd = WorldPosition + ((Quaternion.AngleAxis((float)WheelRotation, wheelLeft) * wheelFwd) * Radius);
        Debug.DrawLine(WorldPosition, wheelEnd, Color.green);

        // Draw Raycasts
        for (int i = 0; i < _debugRayStart.Count; i++)
        {
            Debug.DrawLine(_debugRayStart[i], _debugRayEnd[i], Color.yellow);
        }
    }

    [GUIButton("Auto-Assign Spring, Inertia, Damping")]
    public void AdjustWheelValues()
    {
        if (Body == null)
        {
            Debug.LogWarning("Cannot Auto-Assign wheel values when no parent rigidbody is present.");
            return;
        }

        int numberOfWheels = Body.GetComponentsInChildren<WheelCollider>(true).Count();

        Debug.LogWarning($"Auto-Assigning wheel parameters, found {numberOfWheels} wheels present on vehicle.");

        const double dampingFrac = 0.8;
        const double springFrac = 0.45;

        double mass = Body.Mass / numberOfWheels;
        double wheelMass = Body.Mass * 0.03;

        Inertia = 0.5 * (Radius * Radius) * wheelMass;
        Spring = mass * Physics.World.Gravity.Length() / (SuspensionTravel * springFrac);
        Damping = 2.0 * Math.Sqrt(Spring * Body.Mass) * 0.25 * dampingFrac;
    }
}
